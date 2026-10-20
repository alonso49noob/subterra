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
        public const string WaypointId = "custom";
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

            string prefix = networkId + " Skylight ";
            return manager.Waypoints.Where(w => w.name != null && w.name.StartsWith(prefix)).ToList();
        }

        private void Update()
        {
            if (!FlightGlobals.ready || SubterraScenario.Instance == null || Time.realtimeSinceStartup < nextCheck)
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
            TubeNetwork net = state.Net;
            SubterraScenario.Instance.Visit(net.Id, state.Zone.Kind.ToString());
            if (SubterraScenario.Instance.IsDiscovered(net.Id))
            {
                return;
            }

            string name = string.IsNullOrEmpty(net.DisplayName) ? net.Id : Localizer.Format(net.DisplayName);
            var discovery = new SubterraScenario.Discovery { Id = net.Id, Body = vessel.mainBody.bodyName, Name = name };
            foreach (Skylight s in net.Skylights)
            {
                discovery.Waypoints.Add(new SubterraScenario.WaypointInfo { Name = $"{net.Id} Skylight {s.Index + 1}", Lat = s.Lat, Lon = s.Lon });
            }

            SubterraScenario.Instance.Discover(discovery);
            Log.Info($"Discovered: lava tube network {net.Id}");
            ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Discovered", name), 6f, ScreenMessageStyle.UPPER_CENTER);
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
                if (existing.Any(w => w.name == info.Name))
                {
                    continue;
                }

                var waypoint = new Waypoint
                {
                    celestialName = d.Body,
                    latitude = info.Lat,
                    longitude = info.Lon,
                    altitude = 0,
                    name = info.Name,
                    id = WaypointId,
                    isOnSurface = true,
                    isNavigatable = true,
                    isCustom = true,
                };
                WaypointManager.AddWaypoint(waypoint);
            }
        }
    }
}
