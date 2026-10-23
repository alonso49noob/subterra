using System;
using System.Collections.Generic;
using System.Globalization;
using Subterra.Core.Radar;

namespace Subterra
{
    /// <summary>
    /// Saved with the game: the radar coverage grids (one per body and instrument) and what the radar has found in
    /// each cave network. Placed geophones are vessels of their own, which KSP already saves, so nothing about them
    /// is stored here (spec 4.6 changed). A separate module from <see cref="SubterraScenario"/> so that one's
    /// format stays as it was.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class RadarScenario : ScenarioModule
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, CoverageGrid> grids = new Dictionary<string, CoverageGrid>();

        // KSP creates a new module for every scene and calls OnLoad with the saved nodes, so the store lives as long
        // as this instance (decidido por Claude, revisar): code that listens to LevelRaised subscribes per scene.
        public DetectionStore Detections { get; } = new DetectionStore();

        /// <summary>
        /// The waypoints the radar has laid on newly detected networks (main thread only). Saved here, like the
        /// discoveries of <see cref="SubterraScenario"/>, so <c>RadarWaypoints</c> can put them back when a flight
        /// scene starts.
        /// </summary>
        public List<RadarWaypointInfo> Waypoints { get; } = new List<RadarWaypointInfo>();

        public static RadarScenario Instance { get; private set; }

        /// <summary>
        /// The coverage grid of a body for an instrument; created empty if missing. Safe from any thread. A reload
        /// (OnLoad) replaces the grid objects, so consumers must fetch the grid again here and compare its identity,
        /// not only its Version, to notice a reload.
        /// </summary>
        public CoverageGrid Coverage(string body, InstrumentKind kind)
        {
            string key = Key(body, kind);
            lock (gate)
            {
                if (!grids.TryGetValue(key, out CoverageGrid grid))
                {
                    grid = new CoverageGrid();
                    grids[key] = grid;
                }

                return grid;
            }
        }

        /// <summary>
        /// The coverage grid of a body for an instrument if it exists, without creating an empty one (a grid is
        /// 1 MB). Safe from any thread; the same reload caveat as <see cref="Coverage"/>.
        /// </summary>
        public bool TryCoverage(string body, InstrumentKind kind, out CoverageGrid grid)
        {
            lock (gate)
            {
                return grids.TryGetValue(Key(body, kind), out grid);
            }
        }

        public override void OnAwake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>One waypoint of the radar: the network it marks and where it was laid.</summary>
        public sealed class RadarWaypointInfo
        {
            public string NetworkId;
            public string Body;
            public string Name;
            public double Lat;
            public double Lon;
        }

        public override void OnSave(ConfigNode node)
        {
            lock (gate)
            {
                foreach (KeyValuePair<string, CoverageGrid> pair in grids)
                {
                    // Grids that were only asked for (readers create them) hold nothing worth saving.
                    if (!Array.Exists(pair.Value.ToBytes(), b => b != 0))
                    {
                        continue;
                    }

                    int split = pair.Key.LastIndexOf('|');
                    ConfigNode n = node.AddNode("COVERAGE");
                    n.AddValue("body", pair.Key.Substring(0, split));
                    n.AddValue("instrument", pair.Key.Substring(split + 1));
                    n.AddValue("data", pair.Value.Serialize());
                }
            }

            foreach (RadarWaypointInfo w in Waypoints)
            {
                ConfigNode n = node.AddNode("WAYPOINT");
                n.AddValue("network", w.NetworkId);
                n.AddValue("body", w.Body ?? "");
                n.AddValue("name", w.Name);
                n.AddValue("lat", Num(w.Lat));
                n.AddValue("lon", Num(w.Lon));
            }

            foreach (NetworkDetection d in Detections.All)
            {
                ConfigNode n = node.AddNode("DETECTION");
                n.AddValue("network", d.NetworkId);
                n.AddValue("body", d.Body ?? "");
                n.AddValue("level", d.Level.ToString());
                n.AddValue("uncertainty", Num(d.Uncertainty));
                n.AddValue("depthMin", Num(d.DepthMin));
                n.AddValue("depthMax", Num(d.DepthMax));
                n.AddValue("lat", Num(d.Lat));
                n.AddValue("lon", Num(d.Lon));
                n.AddValue("primitives", d.PrimitivesHex);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            // The module always matches its save: forget what was here (grids are replaced by new objects below).
            Detections.Clear();
            lock (gate)
            {
                grids.Clear();
                foreach (ConfigNode n in node.GetNodes("COVERAGE"))
                {
                    string body = n.GetValue("body");
                    if (string.IsNullOrEmpty(body) || !Enum.TryParse(n.GetValue("instrument"), out InstrumentKind kind) || !Enum.IsDefined(typeof(InstrumentKind), kind))
                    {
                        Log.Warn("Radar save: COVERAGE node skipped (missing body or unknown instrument)");
                        continue;
                    }

                    CoverageGrid grid = CoverageGrid.Deserialize(n.GetValue("data"), out bool ok);
                    if (!ok)
                    {
                        Log.Warn($"Radar save: coverage of {body}/{kind} is damaged; starting it empty");
                    }

                    grids[Key(body, kind)] = grid;
                }
            }

            foreach (ConfigNode n in node.GetNodes("DETECTION"))
            {
                LoadDetection(n);
            }

            Waypoints.Clear();
            foreach (ConfigNode n in node.GetNodes("WAYPOINT"))
            {
                string network = n.GetValue("network");
                string name = n.GetValue("name");
                if (string.IsNullOrEmpty(network) || string.IsNullOrEmpty(name) || !TryNum(n, "lat", out double lat) || !TryNum(n, "lon", out double lon))
                {
                    Log.Warn($"Radar save: WAYPOINT node skipped (network '{network}' has missing or invalid values)");
                    continue;
                }

                Waypoints.Add(new RadarWaypointInfo { NetworkId = network, Body = n.GetValue("body"), Name = name, Lat = lat, Lon = lon });
            }
        }

        private void LoadDetection(ConfigNode n)
        {
            string network = n.GetValue("network");
            if (string.IsNullOrEmpty(network)
                || !Enum.TryParse(n.GetValue("level"), out DetectionLevel level) || !Enum.IsDefined(typeof(DetectionLevel), level)
                || level == DetectionLevel.None
                || !TryNum(n, "uncertainty", out double uncertainty) || !TryNum(n, "depthMin", out double depthMin)
                || !TryNum(n, "depthMax", out double depthMax) || !TryNum(n, "lat", out double lat)
                || !TryNum(n, "lon", out double lon))
            {
                Log.Warn($"Radar save: DETECTION node skipped (network '{network}' has missing or invalid values)");
                return;
            }

            var d = new NetworkDetection
            {
                NetworkId = network,
                Body = n.GetValue("body"),
                Level = level,
                Uncertainty = uncertainty,
                DepthMin = depthMin,
                DepthMax = depthMax,
                Lat = lat,
                Lon = lon,
            };
            d.Primitives = NetworkDetection.FromHex(n.GetValue("primitives") ?? "", out bool ok);
            if (!ok)
            {
                Log.Warn($"Radar save: primitives of network '{network}' are damaged; kept without them");
            }

            Detections.Put(d);
        }

        private static string Key(string body, InstrumentKind kind) => body + "|" + kind;

        private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static bool TryNum(ConfigNode n, string key, out double value)
        {
            return double.TryParse(n.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
