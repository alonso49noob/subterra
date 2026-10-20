using System.Collections.Generic;
using System.Globalization;

namespace Subterra
{
    /// <summary>
    /// Saved with the game: discovered networks (with the waypoints made for their skylights, so they
    /// can be recreated before their cell is generated again) and visited zones.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class SubterraScenario : ScenarioModule
    {
        private readonly Dictionary<string, Discovery> discovered = new Dictionary<string, Discovery>();
        private readonly HashSet<string> visited = new HashSet<string>();

        public static SubterraScenario Instance { get; private set; }

        public IEnumerable<Discovery> Discovered => discovered.Values;

        public bool IsDiscovered(string id) => discovered.ContainsKey(id);

        public bool Discover(Discovery d)
        {
            if (discovered.ContainsKey(d.Id))
            {
                return false;
            }

            discovered[d.Id] = d;
            return true;
        }

        /// <summary>Marks a zone of a network visited; true the first time.</summary>
        public bool Visit(string id, string zone) => visited.Add(id + "|" + zone);

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

        public override void OnSave(ConfigNode node)
        {
            foreach (Discovery d in discovered.Values)
            {
                ConfigNode n = node.AddNode("DISCOVERED");
                n.AddValue("id", d.Id);
                n.AddValue("body", d.Body);
                n.AddValue("name", d.Name);
                foreach (WaypointInfo w in d.Waypoints)
                {
                    ConfigNode wn = n.AddNode("WAYPOINT");
                    wn.AddValue("name", w.Name);
                    wn.AddValue("lat", w.Lat.ToString("R", CultureInfo.InvariantCulture));
                    wn.AddValue("lon", w.Lon.ToString("R", CultureInfo.InvariantCulture));
                }
            }

            foreach (string v in visited)
            {
                string[] parts = v.Split('|');
                ConfigNode n = node.AddNode("VISITED");
                n.AddValue("id", parts[0]);
                n.AddValue("zone", parts.Length > 1 ? parts[1] : "");
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            discovered.Clear();
            visited.Clear();
            foreach (ConfigNode n in node.GetNodes("DISCOVERED"))
            {
                var d = new Discovery { Id = n.GetValue("id"), Body = n.GetValue("body"), Name = n.GetValue("name") };
                if (string.IsNullOrEmpty(d.Id))
                {
                    continue;
                }

                foreach (ConfigNode wn in n.GetNodes("WAYPOINT"))
                {
                    if (double.TryParse(wn.GetValue("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                        && double.TryParse(wn.GetValue("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                    {
                        d.Waypoints.Add(new WaypointInfo { Name = wn.GetValue("name"), Lat = lat, Lon = lon });
                    }
                }

                discovered[d.Id] = d;
            }

            foreach (ConfigNode n in node.GetNodes("VISITED"))
            {
                visited.Add(n.GetValue("id") + "|" + n.GetValue("zone"));
            }
        }

        public sealed class Discovery
        {
            public string Id;
            public string Body;
            public string Name;
            public List<WaypointInfo> Waypoints = new List<WaypointInfo>();
        }

        public struct WaypointInfo
        {
            public string Name;
            public double Lat;
            public double Lon;
        }
    }
}
