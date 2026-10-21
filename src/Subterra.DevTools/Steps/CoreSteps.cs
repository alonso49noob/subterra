using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>The built-in scenario steps. Each comment shows the STEP values it reads.</summary>
    [StepProvider]
    internal static class CoreSteps
    {
        // Real seconds allowed for a scene load; the full profile (Parallax) loads slowly.
        private const double SceneTimeout = 300;
        private const double UnpackTimeout = 60;
        private const string DefaultFlag = "Squad/Flags/default";

        internal static void Register()
        {
            StepRegistry.Register("newGame", NewGame);
            StepRegistry.Register("launch", Launch);
            StepRegistry.Register("teleport", Teleport);
            StepRegistry.Register("release", Release);
            StepRegistry.Register("wait", Wait);
            StepRegistry.Register("waitUntil", WaitUntil);
            StepRegistry.Register("eva", Eva);
            StepRegistry.Register("setPosition", SetPosition);
            StepRegistry.Register("snapshot", Snapshot);
            StepRegistry.Register("quicksave", QuickSave);
            StepRegistry.Register("quickload", QuickLoad);
            StepRegistry.Register("warp", Warp);
            StepRegistry.Register("switchTo", SwitchTo);
            StepRegistry.Register("camera", CameraDistance);
            StepRegistry.Register("screenshot", Screenshot);
            StepRegistry.Register("scene", Scene);
            StepRegistry.Register("spike", Spike);
            StepRegistry.Register("assert", Assert);
            StepRegistry.Register("quit", Quit);
        }

        internal static string DetailSuffix(TestContext ctx)
        {
            return string.IsNullOrEmpty(ctx.Detail) ? "" : ": " + ctx.Detail;
        }

        // newGame mode = SANDBOX name = SubterraTest — deletes any save of that name, creates it
        // the way the main menu does, makes it current and waits for the Space Center.
        private static IEnumerator NewGame(ConfigNode node, TestContext ctx)
        {
            var mode = (Game.Modes)Enum.Parse(typeof(Game.Modes), node.Arg("mode", "SANDBOX"), true);
            if (mode != Game.Modes.SANDBOX && mode != Game.Modes.SCIENCE_SANDBOX && mode != Game.Modes.CAREER)
            {
                throw new ArgumentException("mode must be SANDBOX, SCIENCE_SANDBOX or CAREER");
            }

            string name = KSPUtil.SanitizeString(node.Arg("name", "SubterraTest"), '_', true);
            if (name.Contains("..") || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                throw new ArgumentException("save name must not contain path separators or '..'");
            }

            string saveDir = Path.Combine(KSPUtil.ApplicationRootPath, "saves", name);
            if (Directory.Exists(saveDir))
            {
                Directory.Delete(saveDir, true);
            }

            MainMenu menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
            string flag = menu != null && !string.IsNullOrEmpty(menu.DefaultFlagURL) ? menu.DefaultFlagURL : DefaultFlag;
            GameParameters parameters = GameParameters.GetDefaultParameters(mode, GameParameters.Preset.Normal);
            int loads = ctx.SceneLoads;
            HighLogic.CurrentGame = GamePersistence.CreateNewGame(name, mode, parameters, flag, GameScenes.SPACECENTER, EditorFacility.None);
            GameEvents.onGameNewStart.Fire();
            HighLogic.CurrentGame.Start();
            yield return ctx.WaitForScene(GameScenes.SPACECENTER, loads, SceneTimeout);
            ctx.Pass($"{mode} save '{name}' at the Space Center");
        }

        // launch craft = SPH/Crater Crawler site = Runway crew = true — flies Ships/<craft>.craft
        // from the site (default crew when crew = true) and tracks the new vessel once off rails.
        private static IEnumerator Launch(ConfigNode node, TestContext ctx)
        {
            if (HighLogic.CurrentGame == null)
            {
                throw new InvalidOperationException("no game loaded (run newGame first)");
            }

            string craft = node.RequireArg("craft");
            string site = node.Arg("site", "LaunchPad");
            bool crew = node.ArgBool("crew", false);
            string path = KSPUtil.ApplicationRootPath + "Ships/" + craft + ".craft";
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("craft file not found: " + path);
            }

            ConfigNode craftNode = ConfigNode.Load(path);
            VesselCrewManifest manifest = crew
                ? HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craftNode)
                : VesselCrewManifest.FromConfigNode(craftNode);
            string flag = string.IsNullOrEmpty(HighLogic.CurrentGame.flagURL) ? DefaultFlag : HighLogic.CurrentGame.flagURL;
            int flights = ctx.FlightReadyCount;
            FlightDriver.StartWithNewLaunch(path, flag, site, manifest);
            yield return ctx.WaitForFlight(flights, SceneTimeout);

            Vessel vessel = FlightGlobals.ActiveVessel;
            yield return ctx.WaitFor(() => !vessel.packed, UnpackTimeout, "the vessel to go off rails");
            ctx.Track(vessel);
            int crewCount = vessel.GetCrewCount();
            if (crew && crewCount == 0)
            {
                ctx.Fail("crew = true but nobody boarded");
            }

            ctx.Pass($"{vessel.GetDisplayName()} at {site}: {vessel.parts.Count} parts, {crewCount} crew");
        }

        // teleport body = Mun lat = 0 lon = 0 alt = 3 relative = terrain|sea hold = false — moves the
        // active vessel with the cheat menu's SetVesselPosition. KSP adds the terrain height in both
        // modes (sea only clamps it at sea level, for oceans) and uses |alt|.
        private static IEnumerator Teleport(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = RequireActiveVessel();
            string bodyName = node.Arg("body");
            CelestialBody body = bodyName == null ? vessel.mainBody : FlightGlobals.GetBodyByName(bodyName);
            if (body == null)
            {
                throw new ArgumentException($"unknown body '{bodyName}'");
            }

            double lat = node.ArgDouble("lat", 0);
            double lon = node.ArgDouble("lon", 0);
            double alt = node.ArgDouble("alt", 3);
            string relative = node.Arg("relative", "terrain");
            if (relative != "terrain" && relative != "sea")
            {
                throw new ArgumentException("relative must be terrain or sea");
            }

            bool hold = node.ArgBool("hold", false);
            FlightGlobals.fetch.SetVesselPosition(
                FlightGlobals.GetBodyIndex(body), lat, lon, alt, Vector3.zero, asl: relative == "sea", easeToSurface: false);

            // SetVesselPosition packs every vessel and unpacks the active one on the next frame.
            yield return null;
            yield return null;
            if (hold)
            {
                ctx.Hold(vessel);
            }

            ctx.Pass(Invariant($"{body.bodyName} lat {lat} lon {lon} alt {alt} m over {relative}{(hold ? ", held" : "")}"));
        }

        // release — ends a teleport hold and waits until the vessel is off rails.
        private static IEnumerator Release(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.Release();
            if (vessel == null)
            {
                ctx.Fail("no vessel is held");
                yield break;
            }

            yield return ctx.WaitFor(() => !vessel.packed, UnpackTimeout, "the vessel to go off rails");
            ctx.Pass(vessel.GetDisplayName() + " released");
        }

        // wait seconds = 5 — game time (faster in real time during physics warp).
        private static IEnumerator Wait(ConfigNode node, TestContext ctx)
        {
            yield return new WaitForSeconds((float)node.ArgDouble("seconds", 1));
        }

        // waitUntil what = <assertion> timeout = 30 (+ the assertion's values) — checks every frame;
        // FAIL after timeout real seconds.
        private static IEnumerator WaitUntil(ConfigNode node, TestContext ctx)
        {
            string what = node.RequireArg("what");
            Func<ConfigNode, TestContext, bool> check = StepRegistry.GetAssertion(what);
            double timeout = node.ArgDouble("timeout", 30);
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                ctx.Detail = null;
                float elapsed = Time.realtimeSinceStartup - start;
                bool met;
                try
                {
                    met = check(node, ctx);
                }
                catch (Exception e)
                {
                    // Scene changes briefly leave KSP objects null: an assertion that throws is just not met yet.
                    met = false;
                    ctx.Detail = e.GetType().Name + ": " + e.Message;
                }

                if (met)
                {
                    ctx.Pass(Invariant($"{what} after {elapsed:0.0} s") + DetailSuffix(ctx));
                    yield break;
                }

                if (elapsed > timeout)
                {
                    ctx.Fail(Invariant($"{what} still false after {timeout:0} s") + DetailSuffix(ctx));
                    yield break;
                }

                yield return null;
            }
        }

        // eva kerbal = <optional name> — the first crew member next to an airlock (or the named
        // one) goes on EVA; the kerbal becomes the tracked vessel.
        private static IEnumerator Eva(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = RequireActiveVessel();
            string kerbal = node.Arg("kerbal");
            Part from = null;
            ProtoCrewMember who = null;
            foreach (Part part in vessel.parts)
            {
                if (part.airlock == null)
                {
                    continue;
                }

                foreach (ProtoCrewMember crew in part.protoModuleCrew)
                {
                    if (kerbal == null || crew.name == kerbal)
                    {
                        from = part;
                        who = crew;
                        break;
                    }
                }

                if (who != null)
                {
                    break;
                }
            }

            if (who == null)
            {
                throw new InvalidOperationException(kerbal == null ? "no crew member in a part with an airlock" : kerbal + " is not in a part with an airlock");
            }

            if (FlightEVA.fetch.spawnEVA(who, from, from.airlock, true) == null)
            {
                throw new InvalidOperationException($"{who.name} could not leave {from.partInfo.title} (hatch obstructed?)");
            }

            yield return ctx.WaitFor(() => FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.isEVA, 30, "the EVA kerbal to become active");
            ctx.Track(FlightGlobals.ActiveVessel);
            ctx.Pass($"{who.name} on EVA from {from.partInfo.title}");
        }

        // setPosition offsetUp = 5 — moves the active (unpacked) vessel along the local vertical.
        private static IEnumerator SetPosition(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = RequireActiveVessel();
            if (vessel.packed)
            {
                throw new InvalidOperationException("the vessel is packed (held or still loading)");
            }

            double offsetUp = node.ArgDouble("offsetUp", 0);
            Vector3d position = vessel.transform.position;
            Vector3d up = (position - vessel.mainBody.position).normalized;
            vessel.IgnoreGForces(20);
            vessel.SetPosition(position + up * offsetUp);
            yield return null;
            ctx.Pass(Invariant($"moved {offsetUp} m up, altitude {vessel.altitude:F1} m"));
        }

        // snapshot name = a — stores the tracked vessel's latitude, longitude and altitude.
        private static IEnumerator Snapshot(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("name");
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null)
            {
                throw new InvalidOperationException("no tracked vessel");
            }

            ctx.Snapshots[name] = new Vector3d(vessel.latitude, vessel.longitude, vessel.altitude);
            ctx.Pass(Invariant($"{name}: lat {vessel.latitude:F6} lon {vessel.longitude:F6} alt {vessel.altitude:F2} m"));
            yield break;
        }

        // quicksave name = quicksave — KSP's F5 save (QuickSaveLoad.doSave), without F5's
        // clear-to-save check; the check's status is reported.
        private static IEnumerator QuickSave(ConfigNode node, TestContext ctx)
        {
            string name = node.Arg("name", "quicksave");
            QuickSaveLoad quickSaveLoad = RequireQuickSaveLoad();
            ClearToSaveStatus status = FlightGlobals.ClearToSave();
            PrivateMethod(typeof(QuickSaveLoad), "doSave", typeof(string), typeof(string))
                .Invoke(quickSaveLoad, new object[] { name, "#autoLOC_174669" });
            if (!File.Exists(SaveFile(name)))
            {
                throw new IOException("save not written: " + SaveFile(name));
            }

            ctx.Pass($"{name}.sfs (clear to save: {status})");
            yield break;
        }

        // quickload name = quicksave — KSP's F9 load (QuickSaveLoad.quickLoad); done when the
        // flight scene is ready again (the vessel may still be packed).
        private static IEnumerator QuickLoad(ConfigNode node, TestContext ctx)
        {
            string name = node.Arg("name", "quicksave");
            if (!File.Exists(SaveFile(name)))
            {
                throw new FileNotFoundException("no save " + SaveFile(name));
            }

            QuickSaveLoad quickSaveLoad = RequireQuickSaveLoad();
            int flights = ctx.FlightReadyCount;
            PrivateMethod(typeof(QuickSaveLoad), "quickLoad", typeof(string), typeof(string))
                .Invoke(quickSaveLoad, new object[] { name, HighLogic.SaveFolder });
            yield return ctx.WaitForFlight(flights, SceneTimeout);
            ctx.Pass($"{name}.sfs loaded, active vessel {FlightGlobals.ActiveVessel.GetDisplayName()}");
        }

        // warp rate = 1000 seconds = 10 — physics warp if rate is a physics rate (2-4), on-rails
        // warp otherwise, for that many real seconds; then back to 1x.
        private static IEnumerator Warp(ConfigNode node, TestContext ctx)
        {
            float rate = (float)node.ArgDouble("rate", 1);
            double seconds = node.ArgDouble("seconds", 1);
            TimeWarp warp = TimeWarp.fetch;
            if (warp == null)
            {
                throw new InvalidOperationException("no time warp in this scene");
            }

            TimeWarp.Modes mode = TimeWarp.Modes.LOW;
            int index = Array.FindIndex(warp.physicsWarpRates, r => Mathf.Approximately(r, rate));
            if (index <= 0)
            {
                mode = TimeWarp.Modes.HIGH;
                index = Array.FindIndex(warp.warpRates, r => Mathf.Approximately(r, rate));
            }

            if (index <= 0)
            {
                throw new ArgumentException(Invariant(
                    $"rate {rate} is not a physics rate ({string.Join(", ", warp.physicsWarpRates)}) nor a warp rate ({string.Join(", ", warp.warpRates)})"));
            }

            TimeWarp.Modes previousMode = warp.Mode;
            TimeWarp.SetRate(0, true, false);
            warp.Mode = mode;
            TimeWarp.SetRate(index, true, false);
            yield return null;
            float actual = TimeWarp.CurrentRate;
            double startUT = Planetarium.GetUniversalTime();
            if (Mathf.Approximately(actual, rate))
            {
                yield return new WaitForSecondsRealtime((float)seconds);
            }

            float endRate = TimeWarp.CurrentRate;
            double elapsed = Planetarium.GetUniversalTime() - startUT;
            TimeWarp.SetRate(0, true, false);
            warp.Mode = previousMode;
            yield return null;
            if (Mathf.Approximately(actual, rate))
            {
                ctx.Pass(Invariant($"{(mode == TimeWarp.Modes.LOW ? "physics" : "on-rails")} warp x{rate} for {seconds} s: {elapsed:F0} s of game time, x{endRate} at the end"));
            }
            else
            {
                // KSP limits on-rails warp e.g. while moving over the surface; report why.
                Vessel vessel = FlightGlobals.ActiveVessel;
                string state = vessel == null ? "" : Invariant($", clear to save: {FlightGlobals.ClearToSave()}, surface speed {vessel.srfSpeed:F2} m/s");
                ctx.Fail(Invariant($"KSP refused warp x{rate} (rate was x{actual}{state})"));
            }
        }

        // switchTo vessel = <name> — makes it active (KSP reloads the scene if it is not loaded) and tracks it.
        private static IEnumerator SwitchTo(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("vessel");
            RequireActiveVessel();
            Vessel target = null;
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                // Stock craft names are localization keys (#autoLOC_…): match the displayed name too.
                // Prefer a non-debris vessel (some stock craft files are typed Debris).
                if (vessel != null
                    && (string.Equals(vessel.GetDisplayName(), name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(vessel.vesselName, name, StringComparison.OrdinalIgnoreCase))
                    && (target == null || target.vesselType == VesselType.Debris))
                {
                    target = vessel;
                }
            }

            if (target == null)
            {
                throw new ArgumentException($"no vessel named '{name}'");
            }

            uint id = target.persistentId;
            if (FlightGlobals.ActiveVessel != target)
            {
                FlightGlobals.ForceSetActiveVessel(target);
                yield return ctx.WaitFor(
                    () => HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.ActiveVessel != null
                        && FlightGlobals.ActiveVessel.persistentId == id,
                    SceneTimeout,
                    "the switch to " + name);
            }

            Vessel active = FlightGlobals.ActiveVessel;
            ctx.Track(active);
            ctx.Pass($"{active.GetDisplayName()} active, {(active.packed ? "packed" : "unpacked")}");
        }

        // camera distance = 20 — flight camera distance to its target (clamped by KSP).
        private static IEnumerator CameraDistance(ConfigNode node, TestContext ctx)
        {
            FlightCamera camera = FlightCamera.fetch;
            if (camera == null)
            {
                throw new InvalidOperationException("no flight camera in this scene");
            }

            camera.SetDistanceImmediate((float)node.ArgDouble("distance", 20));
            yield return null;
            ctx.Pass(Invariant($"distance {camera.Distance:F1} m"));
        }

        // screenshot name = smoke — the frame as shown, UI included, to <ResultsDir>/<name>.png.
        private static IEnumerator Screenshot(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("name");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"'{name}' is not a valid file name");
            }

            yield return new WaitForEndOfFrame();
            int width = Screen.width;
            int height = Screen.height;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false);
                File.WriteAllBytes(Path.Combine(ctx.ResultsDir, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }

            ctx.Pass($"{name}.png {width}x{height}");
        }

        // scene name = KSC|MainMenu — leaving flight first saves the persistent game, like the pause menu.
        private static IEnumerator Scene(ConfigNode node, TestContext ctx)
        {
            string name = node.RequireArg("name");
            GameScenes target;
            if (string.Equals(name, "KSC", StringComparison.OrdinalIgnoreCase))
            {
                target = GameScenes.SPACECENTER;
            }
            else if (string.Equals(name, "MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                target = GameScenes.MAINMENU;
            }
            else
            {
                throw new ArgumentException("name must be KSC or MainMenu");
            }

            if (HighLogic.LoadedScene == target)
            {
                ctx.Pass("already there");
                yield break;
            }

            if (target == GameScenes.SPACECENTER && HighLogic.CurrentGame == null)
            {
                throw new InvalidOperationException("no game loaded (run newGame first)");
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            }

            int loads = ctx.SceneLoads;
            HighLogic.LoadScene(target);
            yield return ctx.WaitForScene(target, loads, SceneTimeout);
            ctx.Pass(target.ToString());
        }

        // spike name = <spike> (+ the spike's values) — runs a handler registered with RegisterSpike.
        private static IEnumerator Spike(ConfigNode node, TestContext ctx)
        {
            return StepRegistry.GetSpike(node.RequireArg("name"))(node, ctx);
        }

        // assert what = <assertion> (+ its values) — PASS or FAIL with the assertion's detail.
        private static IEnumerator Assert(ConfigNode node, TestContext ctx)
        {
            string what = node.RequireArg("what");
            Func<ConfigNode, TestContext, bool> check = StepRegistry.GetAssertion(what);
            ctx.Detail = null;
            if (check(node, ctx))
            {
                ctx.Pass(what + DetailSuffix(ctx));
            }
            else
            {
                ctx.Fail(what + DetailSuffix(ctx));
            }

            yield break;
        }

        // quit — ends the scenario; the runner writes DONE and quits KSP.
        private static IEnumerator Quit(ConfigNode node, TestContext ctx)
        {
            ctx.QuitRequested = true;
            yield break;
        }

        private static Vessel RequireActiveVessel()
        {
            Vessel vessel = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (vessel == null)
            {
                throw new InvalidOperationException("no active vessel (not in flight)");
            }

            return vessel;
        }

        private static QuickSaveLoad RequireQuickSaveLoad()
        {
            if (!HighLogic.LoadedSceneIsFlight || QuickSaveLoad.fetch == null)
            {
                throw new InvalidOperationException("quicksave/quickload need the flight scene");
            }

            return QuickSaveLoad.fetch;
        }

        private static MethodInfo PrivateMethod(Type type, string name, params Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            if (method == null)
            {
                throw new MissingMethodException(type.Name, name);
            }

            return method;
        }

        private static string SaveFile(string name)
        {
            return Path.Combine(KSPUtil.ApplicationRootPath, "saves", HighLogic.SaveFolder, name + ".sfs");
        }
    }
}
