using System;
using System.Linq;
using FinePrint;
using KSP.Localization;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Radar;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// A waypoint for each network the radar takes to the Detected level (once per network): at its skylight nearest to
    /// the centroid of what was found or, if the network has no skylight, at the centroid itself. Like
    /// <see cref="DiscoveryService"/> it respects <see cref="Settings.Waypoints"/>, adds them through the shared
    /// <see cref="WaypointQueue"/> and puts them back when a flight scene starts (they are saved in
    /// <see cref="RadarScenario.Waypoints"/>).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class RadarWaypoints : MonoBehaviour
    {
        // The store is recreated with every scene's RadarScenario: follow the current one.
        private DetectionStore subscribed;
        private bool restored;

        private void Update()
        {
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null || !FlightGlobals.ready)
            {
                return;
            }

            if (!ReferenceEquals(subscribed, scenario.Detections))
            {
                Unsubscribe();
                subscribed = scenario.Detections;
                subscribed.LevelRaised += OnLevelRaised;
                restored = false;
            }

            if (!restored)
            {
                restored = true;
                Restore(scenario);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (subscribed != null)
            {
                subscribed.LevelRaised -= OnLevelRaised;
                subscribed = null;
            }
        }

        private static void OnLevelRaised(NetworkDetection d, DetectionLevel level)
        {
            RadarScenario scenario = RadarScenario.Instance;
            if (level != DetectionLevel.Detected || scenario == null || string.IsNullOrEmpty(d.NetworkId)
                || scenario.Waypoints.Any(w => w.NetworkId == d.NetworkId))
            {
                return;
            }

            try
            {
                using (SubterraClock.Measure("RadarWaypoints"))
                {
                    Lay(scenario, d);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Radar waypoint for {d.NetworkId} failed: {e}");
            }
        }

        private static void Lay(RadarScenario scenario, NetworkDetection d)
        {
            double lat = d.Lat, lon = d.Lon;
            if (d.Body != null && BodyRegistry.Instance.Bodies.TryGetValue(d.Body, out BodyRuntime runtime))
            {
                TubeNetwork net = runtime.Catalog.Find(d.NetworkId);
                if (net != null && net.Skylights.Count > 0)
                {
                    Vec3d centroid = BodyFrame.Direction(d.Lat, d.Lon);
                    Skylight nearest = net.Skylights.OrderBy(s => BodyFrame.SurfaceDistance(BodyFrame.Direction(s.Lat, s.Lon), centroid, 1.0)).First();
                    lat = nearest.Lat;
                    lon = nearest.Lon;
                }
            }

            double depth = (d.DepthMin + d.DepthMax) / 2;
            // The network id keeps the name unique: two caves at the same depth are two waypoints (decidido por Claude, revisar).
            string name = Localizer.Format("#Subterra_Radar_Waypoint", Math.Round(depth).ToString("F0")) + " (" + d.NetworkId + ")";
            var info = new RadarScenario.RadarWaypointInfo { NetworkId = d.NetworkId, Body = d.Body, Name = name, Lat = lat, Lon = lon };
            scenario.Waypoints.Add(info);
            Log.Info($"Radar: waypoint \"{name}\" for {d.NetworkId} on {d.Body} at {lat:F4}, {lon:F4}");
            if (Settings.Current.Waypoints)
            {
                Enqueue(info);
            }
        }

        private static void Restore(RadarScenario scenario)
        {
            if (!Settings.Current.Waypoints)
            {
                return;
            }

            foreach (RadarScenario.RadarWaypointInfo info in scenario.Waypoints)
            {
                Enqueue(info);
            }
        }

        // Skips a waypoint that is already in the game or waiting in the queue.
        private static void Enqueue(RadarScenario.RadarWaypointInfo info)
        {
            if (Exists(info.Name) || WaypointQueue.IsPending(info.Name))
            {
                return;
            }

            WaypointQueue.Enqueue(WaypointQueue.Make(info.Body, info.Lat, info.Lon, info.Name));
        }

        /// <summary>Whether the game has a waypoint with this name.</summary>
        public static bool Exists(string name)
        {
            WaypointManager manager = WaypointManager.Instance();
            return manager != null && manager.Waypoints.Any(w => w.name == name);
        }
    }
}
