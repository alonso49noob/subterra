using System.Collections.Generic;
using FinePrint;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Waypoints waiting to be added to the game, one per frame: adding a network's dozen at once costs a visible
    /// frame. Shared by <see cref="DiscoveryService"/> (skylights of a discovered network) and the radar's
    /// <c>RadarWaypoints</c>. Names are the identity: a waypoint whose name is already waiting is dropped. Main thread
    /// only. <see cref="Pump"/> never adds a waypoint in the frame it was queued in, so a frame that queues
    /// one does not also pay for adding it. Waypoints not added yet are lost when the flight scene ends (<see cref="Clear"/>); the callers restore
    /// theirs from the save when the next flight scene starts.
    /// </summary>
    public static class WaypointQueue
    {
        public const string WaypointId = "custom";

        private static readonly Queue<Waypoint> Pending = new Queue<Waypoint>();
        private static readonly Queue<int> PendingFrames = new Queue<int>();
        private static readonly HashSet<string> PendingNames = new HashSet<string>();

        /// <summary>How many waypoints are waiting.</summary>
        public static int Count => Pending.Count;

        /// <summary>A navigable custom waypoint on the surface of a body, as Subterra lays them.</summary>
        public static Waypoint Make(string body, double lat, double lon, string name)
        {
            return new Waypoint
            {
                celestialName = body,
                latitude = lat,
                longitude = lon,
                altitude = 0,
                name = name,
                id = WaypointId,
                isOnSurface = true,
                isNavigatable = true,
                isCustom = true,
            };
        }

        /// <summary>Queues a waypoint; false (and nothing queued) if one with the same name is already waiting.</summary>
        public static bool Enqueue(Waypoint w)
        {
            if (w == null || !PendingNames.Add(w.name))
            {
                return false;
            }

            Pending.Enqueue(w);
            PendingFrames.Enqueue(Time.frameCount);
            return true;
        }

        /// <summary>Whether a waypoint with this name is waiting in the queue.</summary>
        public static bool IsPending(string name) => PendingNames.Contains(name);

        /// <summary>Adds the oldest waiting waypoint to the game (call once per frame).</summary>
        public static void Pump()
        {
            if (Pending.Count == 0 || PendingFrames.Peek() >= Time.frameCount)
            {
                return;
            }

            PendingFrames.Dequeue();
            Waypoint waypoint = Pending.Dequeue();
            PendingNames.Remove(waypoint.name);
            WaypointManager.AddWaypoint(waypoint);
        }

        public static void Clear()
        {
            Pending.Clear();
            PendingFrames.Clear();
            PendingNames.Clear();
        }
    }
}
