using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Subterra.DevTools
{
    /// <summary>State shared by the steps of one scenario run, plus helpers for waiting on KSP.</summary>
    public sealed class TestContext
    {
        private readonly List<string> messages = new List<string>();
        private uint trackedVesselId;
        private uint heldVesselId;

        internal TestContext(string scenarioName, string resultsDir, MonoBehaviour runner)
        {
            ScenarioName = scenarioName;
            ResultsDir = resultsDir;
            Runner = runner;
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneLoaded);
            GameEvents.onFlightReady.Add(OnFlightReady);
        }

        public string ScenarioName { get; }

        /// <summary>Folder of the results file; screenshots and other artifacts are written here too.</summary>
        public string ResultsDir { get; }

        /// <summary>The runner, which survives scene changes; use it to start helper coroutines.</summary>
        public MonoBehaviour Runner { get; }

        /// <summary>Positions saved by the snapshot step: x = latitude, y = longitude, z = altitude (m).</summary>
        public Dictionary<string, Vector3d> Snapshots { get; } = new Dictionary<string, Vector3d>();

        /// <summary>Why the last assertion passed or failed; the assert and waitUntil steps report it.</summary>
        public string Detail { get; set; }

        /// <summary>Set by the quit step: the runner stops after the current step.</summary>
        public bool QuitRequested { get; set; }

        /// <summary>The scenario ends with the game left running for a player (step handOver): no quit.</summary>
        public bool HandedOver { get; set; }

        /// <summary>Count of scene loads seen (onLevelWasLoadedGUIReady) and the last scene loaded.</summary>
        public int SceneLoads { get; private set; }

        public GameScenes LastLoadedScene { get; private set; }

        /// <summary>Count of onFlightReady events: increases on every flight scene load (launch, quickload, switch).</summary>
        public int FlightReadyCount { get; private set; }

        /// <summary>Part count of the tracked vessel when it was tracked.</summary>
        public int TrackedPartCount { get; private set; }

        internal bool StepFailed { get; private set; }

        internal string StepMessage => string.Join("; ", messages);

        /// <summary>
        /// The vessel steps and assertions act on: the one set by <see cref="Track"/> (launch, eva,
        /// switchTo), found again by persistentId after scene reloads; the active vessel if none was set.
        /// </summary>
        public Vessel TrackedVessel
        {
            get
            {
                if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null)
                {
                    return null;
                }

                return trackedVesselId == 0 ? FlightGlobals.ActiveVessel : FindVessel(trackedVesselId);
            }
        }

        /// <summary>Adds a message to the current step's result line.</summary>
        public void Pass(string msg)
        {
            AddMessage(msg);
        }

        /// <summary>Marks the current step as FAIL and adds a message to its result line.</summary>
        public void Fail(string msg)
        {
            StepFailed = true;
            AddMessage(msg);
        }

        public void Track(Vessel vessel)
        {
            trackedVesselId = vessel.persistentId;
            TrackedPartCount = PartCount(vessel);
        }

        /// <summary>Keeps <paramref name="vessel"/> packed and pinned at its current position until <see cref="Release"/>.</summary>
        public void Hold(Vessel vessel)
        {
            heldVesselId = vessel.persistentId;
            ApplyHold(vessel);
        }

        /// <summary>Ends <see cref="Hold"/> without touching the vessel (a step about to place it somewhere else).</summary>
        public void Forget() => heldVesselId = 0;

        /// <summary>Ends <see cref="Hold"/> and lets the vessel go off rails; returns it (null if nothing was held).</summary>
        public Vessel Release()
        {
            Vessel vessel = heldVesselId == 0 ? null : FindVessel(heldVesselId);
            heldVesselId = 0;
            if (vessel != null)
            {
                // The orbit stayed frozen while held: restart it at rest relative to the surface
                // (as SetVesselPosition does), or the vessel would leave the rails with the speed
                // of a fall that never happened.
                RestOrbitHere(vessel);
                vessel.Landed = false;
                OrbitPhysicsManager.HoldVesselUnpack(1);
                vessel.GoOffRails();
            }

            return vessel;
        }

        /// <summary>
        /// Restarts the vessel's orbit at its latitude, longitude and altitude, at rest relative to the surface.
        /// A packed vessel moved by hand keeps its old orbit, and KSP puts it back there when it leaves the rails.
        /// </summary>
        internal static void RestOrbitHere(Vessel vessel, CelestialBody body = null)
        {
            body = body ?? vessel.mainBody;
            Vector3d position = body.GetWorldSurfacePosition(vessel.latitude, vessel.longitude, vessel.altitude) - body.position;
            Vector3d velocity = Vector3d.Cross(body.angularVelocity, position);
            vessel.orbit.UpdateFromStateVectors(position.xzy, velocity.xzy, body, Planetarium.GetUniversalTime());
            vessel.orbitDriver.updateFromParameters();
        }

        /// <summary>Yields every frame until <paramref name="condition"/> is true; throws TimeoutException after the timeout (real time).</summary>
        public IEnumerator WaitFor(Func<bool> condition, double timeoutSeconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + (float)timeoutSeconds;
            string lastError = null;
            while (true)
            {
                bool met = false;
                try
                {
                    met = condition();
                    lastError = null;
                }
                catch (Exception e)
                {
                    // Scene transitions briefly leave KSP singletons null; keep waiting.
                    lastError = e.GetType().Name + ": " + e.Message;
                }

                if (met)
                {
                    yield break;
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    string error = lastError == null ? "" : " (last error: " + lastError + ")";
                    throw new TimeoutException($"timed out after {timeoutSeconds:0} s waiting for {what}{error}");
                }

                yield return null;
            }
        }

        /// <summary>Waits for a load of <paramref name="scene"/> that happens after <see cref="SceneLoads"/> was <paramref name="loadsBefore"/>.</summary>
        public IEnumerator WaitForScene(GameScenes scene, int loadsBefore, double timeoutSeconds)
        {
            return WaitFor(() => SceneLoads > loadsBefore && LastLoadedScene == scene, timeoutSeconds, "scene " + scene);
        }

        /// <summary>Waits for a flight scene load newer than <paramref name="flightReadyBefore"/> with an active vessel.</summary>
        public IEnumerator WaitForFlight(int flightReadyBefore, double timeoutSeconds)
        {
            return WaitFor(
                () => FlightReadyCount > flightReadyBefore && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.ActiveVessel != null,
                timeoutSeconds,
                "flight scene");
        }

        public static Vessel FindVessel(uint persistentId)
        {
            if (FlightGlobals.fetch == null)
            {
                return null;
            }

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel != null && vessel.persistentId == persistentId)
                {
                    return vessel;
                }
            }

            return null;
        }

        public static int PartCount(Vessel vessel)
        {
            if (vessel.loaded)
            {
                return vessel.parts.Count;
            }

            return vessel.protoVessel == null ? 0 : vessel.protoVessel.protoPartSnapshots.Count;
        }

        internal void BeginStep()
        {
            StepFailed = false;
            messages.Clear();
            Detail = null;
        }

        /// <summary>Called every frame by the runner: re-applies the hold after KSP unpacks the vessel (or reloads it).</summary>
        internal void MaintainHold()
        {
            if (heldVesselId == 0 || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready)
            {
                return;
            }

            Vessel vessel = FindVessel(heldVesselId);
            if (vessel == null || !vessel.loaded)
            {
                return;
            }

            try
            {
                ApplyHold(vessel);
            }
            catch
            {
                heldVesselId = 0;
                throw;
            }
        }

        internal void Dispose()
        {
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneLoaded);
            GameEvents.onFlightReady.Remove(OnFlightReady);
        }

        // A packed vessel flagged Landed has an idle orbit driver: KSP pins it at its latitude,
        // longitude and altitude instead of propagating its orbit (which would let it fall).
        private static void ApplyHold(Vessel vessel)
        {
            OrbitPhysicsManager.HoldVesselUnpack(60);
            vessel.Landed = true;
            if (!vessel.packed)
            {
                vessel.GoOnRails();
            }
            else if (vessel.orbitDriver.updateMode != OrbitDriver.UpdateMode.IDLE)
            {
                vessel.orbitDriver.SetOrbitMode(OrbitDriver.UpdateMode.IDLE);
            }
        }

        private void AddMessage(string msg)
        {
            if (!string.IsNullOrEmpty(msg))
            {
                messages.Add(msg);
            }
        }

        private void OnSceneLoaded(GameScenes scene)
        {
            SceneLoads++;
            LastLoadedScene = scene;
        }

        private void OnFlightReady()
        {
            FlightReadyCount++;
        }
    }
}
