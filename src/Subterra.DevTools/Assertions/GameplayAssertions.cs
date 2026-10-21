using System;
using System.Linq;
using Subterra.Core.Gameplay;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Assertions for light, heat, science, resources and discovery.</summary>
    [StepProvider]
    internal static class GameplayAssertions
    {
        private static Color? ambientAtStart;
        private static float? sunAtStart;

        internal static void Register()
        {
            StepRegistry.RegisterAssertion("ambientBelow", AmbientBelow);
            StepRegistry.RegisterAssertion("ambientRestored", (n, ctx) => Restored(ctx, true));
            StepRegistry.RegisterAssertion("sunIntensityRestored", (n, ctx) => Restored(ctx, false));
            StepRegistry.RegisterAssertion("biomeIs", BiomeIs);
            StepRegistry.RegisterAssertion("biomeDisplayName", BiomeDisplayName);
            StepRegistry.RegisterAssertion("subjectValueRatio", SubjectValueRatio);
            StepRegistry.RegisterAssertion("abundanceRatio", AbundanceRatio);
            StepRegistry.RegisterAssertion("abundanceAtLeast", AbundanceAtLeast);
            StepRegistry.RegisterAssertion("discovered", Discovered);
            StepRegistry.RegisterAssertion("waypointCount", WaypointCount);
            StepRegistry.RegisterAssertion("scansatAnomaly", ScansatAnomaly);
            StepRegistry.RegisterAssertion("lightBaseline", LightBaseline);
        }

        // lightBaseline — remembers the ambient light and sun intensity now (outside caves) for the *Restored checks.
        private static bool LightBaseline(ConfigNode node, TestContext ctx)
        {
            ambientAtStart = RenderSettings.ambientLight;
            Light sun = Sun.Instance != null ? Sun.Instance.sunLight : null;
            sunAtStart = sun != null ? sun.intensity : (float?)null;
            ctx.Detail = Invariant($"ambient {ambientAtStart}, sun {sunAtStart}");
            return true;
        }

        // ambientBelow fraction = 0.05 — ambient light is under that fraction of the level saved when entering the cave.
        private static bool AmbientBelow(ConfigNode node, TestContext ctx)
        {
            double fraction = node.ArgDouble("fraction", 0.05);
            Color? saved = LightController.SavedAmbient;
            if (!saved.HasValue)
            {
                ctx.Detail = "the light controller is not active (camera not in a cave?)";
                return false;
            }

            float now = RenderSettings.ambientLight.grayscale, before = saved.Value.grayscale;
            ctx.Detail = Invariant($"ambient {now:F4} vs {before:F4} outside, sky {UndergroundTracker.Camera.Sky:F2}");
            return now <= before * fraction + 1e-4f;
        }

        private static bool Restored(TestContext ctx, bool ambient)
        {
            if (ambient)
            {
                if (!ambientAtStart.HasValue)
                {
                    ctx.Detail = "no lightBaseline";
                    return false;
                }

                Color now = RenderSettings.ambientLight;
                ctx.Detail = Invariant($"ambient {now} (baseline {ambientAtStart.Value}), controller {(LightController.Active ? "active" : "idle")}");
                return Mathf.Abs(now.grayscale - ambientAtStart.Value.grayscale) < 0.01f;
            }

            Light sun = Sun.Instance != null ? Sun.Instance.sunLight : null;
            if (sun == null || !sunAtStart.HasValue)
            {
                ctx.Detail = "no sun light or no lightBaseline";
                return false;
            }

            ctx.Detail = Invariant($"sun intensity {sun.intensity} (baseline {sunAtStart.Value})");
            return Mathf.Abs(sun.intensity - sunAtStart.Value) < 0.01f;
        }

        // biomeIs id = DevTube|zone — ScienceUtil's biome at the tracked vessel ("zone" = what ZoneAt says).
        private static bool BiomeIs(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            if (v == null)
            {
                ctx.Detail = "no vessel";
                return false;
            }

            string expected = node.RequireArg("id");
            UndergroundState state = UndergroundTracker.For(v);
            if (expected == "zone")
            {
                expected = BiomeIds.For(state.Zone);
            }

            string biome = ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude);
            ctx.Detail = $"biome {biome}, expected {expected} ({state})";
            return biome == expected;
        }

        // biomeDisplayName text = "Dev Tube" — ScienceUtil's display name of the tracked vessel's biome.
        private static bool BiomeDisplayName(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            string biome = v == null ? null : ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude);
            string name = biome == null ? null : ScienceUtil.GetBiomedisplayName(v.mainBody, biome);
            string text = node.RequireArg("text");
            ctx.Detail = $"'{name}' for biome {biome}";
            return name == text;
        }

        // subjectValueRatio ratio = 2 experiment = temperatureScan — subject value of the tracked vessel's biome / the landed value.
        private static bool SubjectValueRatio(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            ScienceExperiment experiment = ResearchAndDevelopment.GetExperiment(node.Arg("experiment", "temperatureScan"));
            if (v == null || experiment == null)
            {
                ctx.Detail = "no vessel or experiment";
                return false;
            }

            string biome = ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude);
            ScienceSubject subject = ResearchAndDevelopment.GetExperimentSubject(experiment, ExperimentSituations.SrfLanded, v.mainBody, biome,
                ScienceUtil.GetBiomedisplayName(v.mainBody, biome));
            double ratio = subject.subjectValue / v.mainBody.scienceValues.LandedDataValue;
            double expected = node.ArgDouble("ratio", 1);
            ctx.Detail = Invariant($"{subject.id}: value {subject.subjectValue} → ratio {ratio:F3} (expected {expected}), cap {subject.scienceCap}");
            return Math.Abs(ratio - expected) < 0.01;
        }

        private static double Abundance(Vessel v, string resource, double altitude) =>
            ResourceMap.Instance.GetAbundance(new AbundanceRequest
            {
                Latitude = v.latitude,
                Longitude = v.longitude,
                Altitude = altitude,
                BodyId = v.mainBody.flightGlobalsIndex,
                ResourceName = resource,
                ResourceType = HarvestTypes.Planetary,
                CheckForLock = false,
                ExcludeVariance = false,
            });

        // abundanceRatio resource = Ore ratio = 2 — abundance at the vessel's altitude / at the surface (altitude 0).
        private static bool AbundanceRatio(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            if (v == null || ResourceMap.Instance == null)
            {
                ctx.Detail = "no vessel or resource map";
                return false;
            }

            string resource = node.Arg("resource", "Ore");
            double inside = Abundance(v, resource, v.altitude), surface = Abundance(v, resource, 0);
            double expected = node.ArgDouble("ratio", 1);
            ctx.Detail = Invariant($"{resource} {inside:F5} inside vs {surface:F5} at the surface");
            return surface > 0 && Math.Abs(inside / surface - expected) < 0.02;
        }

        // abundanceAtLeast resource = Water min = 0.05 — abundance at the vessel.
        private static bool AbundanceAtLeast(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            if (v == null || ResourceMap.Instance == null)
            {
                ctx.Detail = "no vessel or resource map";
                return false;
            }

            string resource = node.Arg("resource", "Water");
            double value = Abundance(v, resource, v.altitude);
            ctx.Detail = Invariant($"{resource} {value:F4}");
            return value >= node.ArgDouble("min", 0) - 1e-6;
        }

        // discovered id = MN-0427|nearest — the network (nearest = the one the vessel is in) is recorded as discovered.
        private static bool Discovered(ConfigNode node, TestContext ctx)
        {
            string id = ResolveId(node, ctx);
            bool found = id != null && SubterraScenario.Instance != null && SubterraScenario.Instance.IsDiscovered(id);
            ctx.Detail = $"{id ?? "no network"}: {(found ? "discovered" : "not discovered")}";
            return found;
        }

        // waypointCount n = 2 id = nearest — Subterra waypoints of the network (n = skylights: number of its skylights).
        private static bool WaypointCount(ConfigNode node, TestContext ctx)
        {
            string id = ResolveId(node, ctx);
            if (id == null)
            {
                ctx.Detail = "no network";
                return false;
            }

            int count = DiscoveryService.WaypointsOf(id).Count;
            string nText = node.Arg("n", "skylights");
            int expected = nText == "skylights" ? CurrentNetwork(ctx)?.Skylights.Count ?? -1 : int.Parse(nText);
            ctx.Detail = $"{id}: {count} waypoints (expected {expected})";
            return count == expected;
        }

        // scansatAnomaly name = DevTube — the body lists a Subterra surface object with that name.
        private static bool ScansatAnomaly(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            string name = node.RequireArg("name");
            PQSSurfaceObject[] objects = v?.mainBody.pqsSurfaceObjects ?? new PQSSurfaceObject[0];
            bool found = objects.Any(o => o is SubterraAnomaly && o.SurfaceObjectName == name);
            ctx.Detail = $"{objects.Length} surface objects, Subterra: {string.Join(", ", objects.OfType<SubterraAnomaly>().Select(o => o.SurfaceObjectName))}";
            return found;
        }

        private static Core.Tubes.TubeNetwork CurrentNetwork(TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            return v == null ? null : UndergroundTracker.For(v).Net;
        }

        private static string ResolveId(ConfigNode node, TestContext ctx)
        {
            string id = node.Arg("id", "nearest");
            if (id != "nearest")
            {
                return id;
            }

            // The network the vessel is in, else the last one seen (after leaving it or reloading).
            Core.Tubes.TubeNetwork current = CurrentNetwork(ctx);
            if (current != null)
            {
                lastNetworkId = current.Id;
            }

            return lastNetworkId;
        }

        private static string lastNetworkId;
    }
}
