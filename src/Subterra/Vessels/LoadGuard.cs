using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Keeps a loaded vessel inside a cave on rails until the cave colliders around it exist: the
    /// active vessel through OrbitPhysicsManager's unpack hold, others by shrinking their unpack range.
    /// Never lets a vessel go off rails with no collider below.
    /// </summary>
    public sealed class LoadGuard
    {
        public const float Radius = 30f;
        private Dictionary<VesselRanges.Situation, (float pack, float unpack)> saved;
        private float nextCheck;
        private bool needed;

        public bool Active { get; private set; }

        public void Update(Vessel vessel, UndergroundState state)
        {
            // The collider check is not free: 5 times a second is enough (the hold is renewed every frame).
            if (Time.realtimeSinceStartup >= nextCheck || !state.Inside)
            {
                nextCheck = Time.realtimeSinceStartup + 0.2f;
                needed = state.Inside && ChunkStreamer.Instance != null && !ChunkStreamer.Instance.CollidersReady(vessel.GetWorldPos3D(), Radius);
            }

            if (!needed)
            {
                Release(vessel);
                return;
            }

            if (!Active)
            {
                Active = true;
                Log.Info($"Load guard on for {vessel.GetDisplayName()} (cave colliders not ready)");
            }

            if (vessel == FlightGlobals.ActiveVessel)
            {
                if (vessel.packed)
                {
                    OrbitPhysicsManager.HoldVesselUnpack(2);
                }
            }
            else if (saved == null)
            {
                saved = new Dictionary<VesselRanges.Situation, (float, float)>();
                foreach (VesselRanges.Situation s in Situations(vessel))
                {
                    saved[s] = (s.pack, s.unpack);
                    s.unpack = 1f;
                    s.pack = 1.5f;
                }
            }
        }

        public void Release(Vessel vessel)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            if (saved != null)
            {
                foreach (var pair in saved)
                {
                    pair.Key.pack = pair.Value.pack;
                    pair.Key.unpack = pair.Value.unpack;
                }

                saved = null;
            }

            Log.Info($"Load guard off for {vessel.GetDisplayName()}");
        }

        private static IEnumerable<VesselRanges.Situation> Situations(Vessel v)
        {
            VesselRanges r = v.vesselRanges;
            return new[] { r.landed, r.prelaunch, r.flying, r.splashed, r.subOrbital };
        }
    }
}
