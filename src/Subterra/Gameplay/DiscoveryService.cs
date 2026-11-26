using System.Collections.Generic;
using System.Linq;
using FinePrint;
using KSP.Localization;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// First entry into a network: a screen message and (Settings.Waypoints) a waypoint on each of its
    /// skylights; the discovery is saved by <see cref="SubterraScenario"/> and the waypoints are put
    /// back when a game loads.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class DiscoveryService : MonoBehaviour
    {
        public const string WaypointId = WaypointQueue.WaypointId;
        private float nextCheck;
        private bool restored;

        /// <summary>Subterra's waypoints of a network (by name prefix).</summary>
        public static List<Waypoint> WaypointsOf(string networkId)
        {
            WaypointManager manager = WaypointManager.Instance();
            if (manager == null)
            {
                return new List<Waypoint>();
            }

            string prefix = networkId + " ";
            return manager.Waypoints.Where(w => w.name != null && w.name.StartsWith(prefix)).ToList();
        }

        // Waypoints not added yet come back with the discoveries when the next flight scene restores them.
        private void OnDestroy()
        {
            WaypointQueue.Clear();
        }

        private void Update()
        {
            if (!FlightGlobals.ready || SubterraScenario.Instance == null)
            {
                return;
            }

            if (WaypointQueue.Count > 0)
            {
                using (SubterraClock.Measure("DiscoveryService"))
                {
                    WaypointQueue.Pump();
                }
            }

            if (Time.realtimeSinceStartup < nextCheck)
            {
                return;
            }

            using (SubterraClock.Measure("DiscoveryService"))
            {
                nextCheck = Time.realtimeSinceStartup + 0.5f;
                if (!restored)
                {
                    restored = true;
                    RestoreWaypoints();
                }

                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v == null || v.vesselType == VesselType.Debris)
                    {
                        continue;
                    }

                    UndergroundState state = UndergroundTracker.For(v);
                    if (state.Inside)
                    {
                        Discover(v, state);
                    }
                }
            }
        }

        private static void Discover(Vessel vessel, UndergroundState state)
        {
            SubterraScenario.Instance.Visit(state.Net.Id, state.Zone.Kind.ToString());
            DiscoverNetwork(vessel, state.Net);
        }

        /// <summary>Discovers <paramref name="net"/> for <paramref name="vessel"/> (entered, or reached by a drill); no-op if known.</summary>
        internal static void DiscoverNetwork(Vessel vessel, TubeNetwork net)
        {
            // Holes the player dug are not discoveries (no message, no waypoints): their networks are rebuilt as they grow and
            // join each other, and one's skylight waypoint piled up hundreds of times (Lucas, under Minmus).
            if (SubterraScenario.Instance == null || SubterraScenario.Instance.IsDiscovered(net.Id) || ExcavationService.IsDug(net.Id))
            {
                return;
            }

            string name = string.IsNullOrEmpty(net.DisplayName) ? net.Id : Localizer.Format(net.DisplayName);
            var discovery = new SubterraScenario.Discovery { Id = net.Id, Body = vessel.mainBody.bodyName, Name = name };
            foreach (Skylight s in net.Skylights)
            {
                discovery.Waypoints.Add(new SubterraScenario.WaypointInfo { Name = Localizer.Format("#Subterra_Waypoint", net.Id, s.Index + 1), Lat = s.Lat, Lon = s.Lon });
            }

            SubterraScenario.Instance.Discover(discovery);
            // A lake is not a lava tube (Lucas: "Discovered: lava tube network South Polar Lake").
            bool lake = net.Site != null && net.Site.Nodes.Exists(n => n.Feature == Subterra.Core.Model.NodeFeature.Lake);
            Log.Info($"Discovered: {(lake ? "lake" : "cave network")} {net.Id}");
            ScreenMessages.PostScreenMessage(Localizer.Format(lake ? "#Subterra_DiscoveredLake" : "#Subterra_Discovered", name), 6f, ScreenMessageStyle.UPPER_CENTER);
            if (Settings.Current.Waypoints)
            {
                AddWaypoints(discovery);
            }
        }

        private static void RestoreWaypoints()
        {
            if (!Settings.Current.Waypoints)
            {
                return;
            }

            foreach (SubterraScenario.Discovery d in SubterraScenario.Instance.Discovered)
            {
                AddWaypoints(d);
            }
        }

        private static void AddWaypoints(SubterraScenario.Discovery d)
        {
            List<Waypoint> existing = WaypointsOf(d.Id);
            foreach (SubterraScenario.WaypointInfo info in d.Waypoints)
            {
                if (existing.Any(w => w.name == info.Name) || WaypointQueue.IsPending(info.Name))
                {
                    continue;
                }

                WaypointQueue.Enqueue(WaypointQueue.Make(d.Body, info.Lat, info.Lon, info.Name));
            }
        }
    }
}
