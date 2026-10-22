using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Subterra.Core.Radar;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Turns switched-on orbital instruments (Eco, Plomada) into coverage: once a second of real time it walks every
    /// such vessel's ground track from the instrument's <c>lastScanUT</c> up to now and marks the body's
    /// <see cref="CoverageGrid"/> in <see cref="RadarScenario"/>. Loaded vessels are read through their
    /// <see cref="ModuleSubterraRadar"/>; unloaded ones through their <c>ProtoPartModuleSnapshot</c> (the part's cfg
    /// values come from the part prefab), so scanning goes on in the background and through time-warp, like SCANsat.
    ///
    /// <c>lastScanUT</c> is this class's progress marker: it only moves as far as the walk got, so after a long warp
    /// the coverage catches up over several ticks (at most <see cref="MaxStepsPerVessel"/> samples per vessel and
    /// <see cref="TickBudgetMs"/> per tick) instead of costing one long frame.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public sealed class OrbitalScanner : MonoBehaviour
    {
        public const string ModuleName = "ModuleSubterraRadar";

        /// <summary>Most ground-track samples one vessel may take per tick (its instruments share them).</summary>
        public const int MaxStepsPerVessel = 2000;

        private const float TickSeconds = 1f;

        /// <summary>
        /// Main-thread time one tick may spend; the vessel being walked stops where it is and the rest wait for the
        /// next tick (decidido por Claude, revisar: the step cap alone is ~5-10 ms on a slow machine, over the 8 ms
        /// worst-frame budget).
        /// </summary>
        private const double TickBudgetMs = 4;

        /// <summary>Samples between two looks at the clock inside a walk.</summary>
        private const int ClockStride = 32;

        private readonly List<Instrument> instruments = new List<Instrument>();
        private readonly Dictionary<AvailablePart, List<ModuleSubterraRadar>> prefabModules = new Dictionary<AvailablePart, List<ModuleSubterraRadar>>();
        private readonly HashSet<Guid> failed = new HashSet<Guid>();
        private readonly Stopwatch watch = new Stopwatch();
        private float nextTick;
        private int firstVessel;

        /// <summary>
        /// Raised on the main thread at most once per vessel per tick when that vessel's walk improved a coverage
        /// grid: the body's name and the last sub-point (degrees) it marked. Listeners read the grid for the rest.
        /// </summary>
        public static event Action<string, double, double> CoverageChanged;

        /// <summary>
        /// Latitude, longitude (degrees) and altitude above the datum (m) of the point under an orbit at UT
        /// <paramref name="ut"/>. <c>Orbit.getPositionAtUT</c> gives where the vessel is in space at that time but in
        /// the frame of the body as it is turned NOW, so the longitude is turned back by the angle the body rotates
        /// between now and <paramref name="ut"/>: KSP sets <c>rotationAngle = initialRotation + 360 * UT /
        /// rotationPeriod</c> (CelestialBody.CBUpdate) and longitudes are measured in the body's turning frame, so a
        /// fixed direction in space sits rotationAngle(ut) - rotationAngle(now) degrees further west at ut. Using the
        /// body's current <c>rotationAngle</c> (not the clock) keeps it right during high warp, when the clock can run
        /// a frame ahead of the body's orientation.
        /// </summary>
        public static void SubPoint(Orbit orbit, double ut, out double lat, out double lon, out double altitude)
        {
            CelestialBody body = orbit.referenceBody;
            Vector3d radial = orbit.getRelativePositionAtUT(ut).xzy;
            altitude = radial.magnitude - body.Radius;
            lat = body.GetLatitude(radial, true);
            lon = NormalizeLongitude(body.GetLongitude(radial, true) - RotationSince(body, ut));
        }

        /// <summary>Degrees the body turns from its current orientation to the one it has at UT ut (negative in the past).</summary>
        public static double RotationSince(CelestialBody body, double ut)
        {
            if (!body.rotates || body.rotationPeriod == 0)
            {
                return 0;
            }

            return ((body.initialRotation + 360.0 * ut / body.rotationPeriod) - body.rotationAngle) % 360.0;
        }

        /// <summary>Half the swath width in metres: 2F for the radar sounder (total 4F), the altitude for the gravimeter.</summary>
        public static double HalfWidth(InstrumentKind kind, double frequencyMHz, double altitude)
        {
            return kind == InstrumentKind.Gravimeter ? altitude : 2 * RadarPhysics.FresnelOrbital(frequencyMHz, altitude);
        }

        private static bool ActiveScene()
        {
            GameScenes scene = HighLogic.LoadedScene;
            return scene == GameScenes.FLIGHT || scene == GameScenes.SPACECENTER || scene == GameScenes.TRACKSTATION;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < nextTick || !ActiveScene())
            {
                return;
            }

            nextTick = Time.realtimeSinceStartup + TickSeconds;
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null || FlightGlobals.fetch == null || FlightGlobals.Vessels == null || HighLogic.LoadedSceneIsFlight && !FlightGlobals.ready)
            {
                return;
            }

            using (SubterraClock.Measure("OrbitalScanner"))
            {
                Tick(scenario);
            }
        }

        private void Tick(RadarScenario scenario)
        {
            watch.Reset();
            watch.Start();
            double now = Planetarium.GetUniversalTime();
            List<Vessel> vessels = FlightGlobals.Vessels;
            int count = vessels.Count;
            if (count == 0)
            {
                return;
            }

            // Round robin: when the time budget runs out, the vessels not reached go first next tick.
            firstVessel %= count;
            for (int k = 0; k < count; k++)
            {
                if (watch.Elapsed.TotalMilliseconds > TickBudgetMs)
                {
                    firstVessel = (firstVessel + k) % count;
                    return;
                }

                Vessel v = vessels[(firstVessel + k) % count];
                if (v == null || v.vesselType == VesselType.Flag || v.vesselType == VesselType.SpaceObject)
                {
                    continue;
                }

                try
                {
                    ScanVessel(v, now, scenario);
                }
                catch (Exception e)
                {
                    // One report per vessel: a broken vessel must not flood the log every second.
                    if (failed.Add(v.id))
                    {
                        Log.Error($"Orbital scan of {v.GetDisplayName()} failed: {e}");
                    }
                }
            }
        }

        private void ScanVessel(Vessel v, double now, RadarScenario scenario)
        {
            instruments.Clear();
            Collect(v);
            if (instruments.Count == 0)
            {
                return;
            }

            CelestialBody body = v.mainBody;
            string bodyName = body != null ? body.bodyName : "";
            Orbit orbit = v.orbit;
            Vessel.Situations situation = v.situation;
            bool canScan = body != null && orbit != null && BodyRegistry.Instance.TryGet(body, out _)
                && situation != Vessel.Situations.LANDED && situation != Vessel.Situations.SPLASHED && situation != Vessel.Situations.PRELAUNCH;

            CoverageGrid echo = null, gravity = null;
            int echoVersion = 0, gravityVersion = 0;
            bool marked = false;
            double markLat = 0, markLon = 0;
            int stepsEach = Math.Max(1, MaxStepsPerVessel / instruments.Count);

            foreach (Instrument instrument in instruments)
            {
                double from = instrument.LastScanUT;
                if (from <= 0 || from > now || instrument.LastScanBody != bodyName)
                {
                    // Never walked, a revert moved the clock back, or the vessel changed body since the marker (its
                    // track before was around the other body; the current orbit cannot rebuild it): start from now.
                    // Orbit.StartUT cannot tell a patch change: the patched-conics solver rewrites it with the
                    // current UT every frame for the active vessel.
                    from = now;
                }

                // A loaded instrument that is off, unpowered or blocked marks nothing, and its marker still moves
                // to now, so a later resume does not back-fill the gap (decidido por Claude, revisar). The same for a
                // vessel on the ground or around a body Subterra does not handle (Jool): its past was on another
                // orbit, and walking it with the current one would mark the wrong places.
                if (!instrument.Active || !canScan)
                {
                    if (instrument.Snapshot == null || instrument.Active)
                    {
                        instrument.Save(now, bodyName);
                    }

                    continue;
                }

                CoverageGrid grid;
                if (instrument.Kind == InstrumentKind.Gravimeter)
                {
                    if (gravity == null)
                    {
                        gravity = scenario.Coverage(body.bodyName, InstrumentKind.Gravimeter);
                        gravityVersion = gravity.Version;
                    }

                    grid = gravity;
                }
                else
                {
                    if (echo == null)
                    {
                        echo = scenario.Coverage(body.bodyName, InstrumentKind.OrbitalRadar);
                        echoVersion = echo.Version;
                    }

                    grid = echo;
                }

                double reached = Walk(instrument, orbit, body, from, now, stepsEach, grid, ref marked, ref markLat, ref markLon);
                instrument.Save(reached, bodyName);
            }

            if (marked && (echo != null && echo.Version != echoVersion || gravity != null && gravity.Version != gravityVersion))
            {
                CoverageChanged?.Invoke(body.bodyName, markLat, markLon);
            }
        }

        /// <summary>
        /// Walks the ground track from UT from towards now and returns the UT reached (the last sample taken; the
        /// next walk starts by taking it again, to join the track). The time step makes the sub-point advance at most
        /// half the swath (the swath at the lowest altitude the orbit can scan from) between samples: the bound
        /// adds the orbital angular rate at periapsis, the fastest point, and the body's rotation.
        /// </summary>
        private double Walk(Instrument instrument, Orbit orbit, CelestialBody body, double from, double now, int maxSteps, CoverageGrid grid,
            ref bool marked, ref double markLat, ref double markLon)
        {
            double min = instrument.AltitudeMin, max = instrument.AltitudeMax;
            double pe = orbit.PeA;
            if (pe > max || orbit.eccentricity < 1 && orbit.ApA < min)
            {
                return now; // The orbit never enters the window.
            }

            double radius = body.Radius;
            double peR = Math.Max(orbit.PeR, radius);
            double speedAtPe = Math.Sqrt(Math.Max(0, body.gravParameter * (2 / peR - 1 / orbit.semiMajorAxis)));
            double rotationRate = body.rotates && body.rotationPeriod != 0 ? 2 * Math.PI / Math.Abs(body.rotationPeriod) : 0;
            double groundSpeed = radius * (speedAtPe / peR + rotationRate);
            double narrowest = HalfWidth(instrument.Kind, instrument.FrequencyMHz, Math.Max(min, pe));
            double dt = groundSpeed > 0 ? narrowest / groundSpeed : now - from;
            if (!(dt > 0) || double.IsInfinity(dt))
            {
                return now;
            }

            bool havePrevious = false;
            double prevLat = 0, prevLon = 0, prevAlt = 0;
            double t = from;
            for (int steps = 1; ; steps++)
            {
                SubPoint(orbit, t, out double lat, out double lon, out double alt);
                bool inside = alt >= min && alt <= max;
                if (inside)
                {
                    // Between two samples the swath and the resolution are those of the worse of the two.
                    double low = havePrevious ? Math.Min(prevAlt, alt) : alt;
                    double high = havePrevious ? Math.Max(prevAlt, alt) : alt;
                    double half = HalfWidth(instrument.Kind, instrument.FrequencyMHz, low);
                    if (havePrevious)
                    {
                        grid.MarkTrack(prevLat, prevLon, lat, lon, half, radius, high);
                    }
                    else
                    {
                        grid.MarkSwath(lat, lon, half, radius, high);
                    }

                    marked = true;
                    markLat = lat;
                    markLon = lon;
                }

                havePrevious = inside;
                prevLat = lat;
                prevLon = lon;
                prevAlt = alt;
                if (t >= now || steps >= maxSteps || steps % ClockStride == 0 && watch.Elapsed.TotalMilliseconds > TickBudgetMs)
                {
                    return t;
                }

                t = Math.Min(now, t + dt);
            }
        }

        // Fills `instruments` with the vessel's orbital radar instruments.
        private void Collect(Vessel v)
        {
            if (v.loaded)
            {
                List<Part> parts = v.parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    PartModuleList modules = parts[i].Modules;
                    for (int j = 0; j < modules.Count; j++)
                    {
                        if (modules[j] is ModuleSubterraRadar radar && radar.IsOrbitalMode)
                        {
                            instruments.Add(Instrument.Live(radar));
                        }
                    }
                }

                return;
            }

            ProtoVessel proto = v.protoVessel;
            if (proto == null)
            {
                return;
            }

            List<ProtoPartSnapshot> snapshots = proto.protoPartSnapshots;
            for (int i = 0; i < snapshots.Count; i++)
            {
                ProtoPartSnapshot pps = snapshots[i];
                int nth = 0;
                for (int j = 0; j < pps.modules.Count; j++)
                {
                    ProtoPartModuleSnapshot snapshot = pps.modules[j];
                    if (snapshot.moduleName != ModuleName)
                    {
                        continue;
                    }

                    // The n-th radar snapshot of a part belongs to the n-th radar module of its prefab.
                    List<ModuleSubterraRadar> prefab = PrefabModules(pps.partInfo);
                    ModuleSubterraRadar config = nth < prefab.Count ? prefab[nth] : null;
                    nth++;
                    if (config != null && config.IsOrbitalMode)
                    {
                        instruments.Add(Instrument.Unloaded(snapshot, config));
                    }
                }
            }
        }

        private List<ModuleSubterraRadar> PrefabModules(AvailablePart info)
        {
            if (info == null || info.partPrefab == null)
            {
                return EmptyModules;
            }

            if (!prefabModules.TryGetValue(info, out List<ModuleSubterraRadar> list))
            {
                list = new List<ModuleSubterraRadar>();
                foreach (PartModule m in info.partPrefab.Modules)
                {
                    if (m is ModuleSubterraRadar radar)
                    {
                        list.Add(radar);
                    }
                }

                prefabModules[info] = list;
            }

            return list;
        }

        private static readonly List<ModuleSubterraRadar> EmptyModules = new List<ModuleSubterraRadar>();

        private static double NormalizeLongitude(double lon)
        {
            lon %= 360.0;
            if (lon >= 180)
            {
                lon -= 360;
            }
            else if (lon < -180)
            {
                lon += 360;
            }

            return lon;
        }

        /// <summary>One orbital instrument, loaded (module) or not (snapshot + prefab config).</summary>
        private sealed class Instrument
        {
            public ModuleSubterraRadar Module;
            public ProtoPartModuleSnapshot Snapshot;
            public InstrumentKind Kind;
            public double FrequencyMHz;
            public double AltitudeMin;
            public double AltitudeMax;
            public double LastScanUT;

            /// <summary>The body the marker belongs to (empty in saves from before it existed).</summary>
            public string LastScanBody;

            /// <summary>
            /// Whether it scans now. Loaded: the module's live state (on, powered, in its situation). Unloaded: the
            /// saved scanning flag only; no electricity check in the background (decidido por Claude, revisar).
            /// </summary>
            public bool Active;

            public static Instrument Live(ModuleSubterraRadar m)
            {
                return new Instrument
                {
                    Module = m,
                    Kind = m.mode == ModuleSubterraRadar.ModeGravimeter ? InstrumentKind.Gravimeter : InstrumentKind.OrbitalRadar,
                    FrequencyMHz = m.frequencyMHz,
                    AltitudeMin = m.altitudeMin,
                    AltitudeMax = m.altitudeMax,
                    LastScanUT = m.lastScanUT,
                    LastScanBody = m.lastScanBody ?? "",
                    Active = m.scanning && m.IsScanningNow,
                };
            }

            public static Instrument Unloaded(ProtoPartModuleSnapshot s, ModuleSubterraRadar config)
            {
                ConfigNode values = s.moduleValues;
                bool.TryParse(values?.GetValue("scanning"), out bool scanning);
                double.TryParse(values?.GetValue("lastScanUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out double last);
                return new Instrument
                {
                    Snapshot = s,
                    Kind = config.mode == ModuleSubterraRadar.ModeGravimeter ? InstrumentKind.Gravimeter : InstrumentKind.OrbitalRadar,
                    FrequencyMHz = config.frequencyMHz,
                    AltitudeMin = config.altitudeMin,
                    AltitudeMax = config.altitudeMax,
                    LastScanUT = last,
                    LastScanBody = values?.GetValue("lastScanBody") ?? "",
                    Active = scanning,
                };
            }

            /// <summary>Stores the new marker where KSP saves it (the module field, or the snapshot's values).</summary>
            public void Save(double ut, string body)
            {
                if (Module != null)
                {
                    Module.lastScanUT = ut;
                    Module.lastScanBody = body;
                }
                else if (Snapshot?.moduleValues != null)
                {
                    Snapshot.moduleValues.SetValue("lastScanUT", ut.ToString("R", CultureInfo.InvariantCulture), true);
                    if (LastScanBody != body)
                    {
                        Snapshot.moduleValues.SetValue("lastScanBody", body, true);
                    }
                }

                LastScanUT = ut;
                LastScanBody = body;
            }
        }
    }
}
