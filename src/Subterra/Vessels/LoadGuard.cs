using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Keeps a loaded vessel inside a cave on rails until the cave colliders around it exist: the
    /// active vessel through OrbitPhysicsManager's unpack hold, others by shrinking their unpack range.
    /// Not ready means not ready: no streamer yet, nothing planned for this body yet, or colliders not
    /// baked. An unpacked vessel in a cave with nothing under it is put back on rails.
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
            // A landed craft on rails underground stays put: KSP leaves its orbit driver flying its Kepler orbit when it put it
            // on rails before it counted as landed, and that orbit's gravity is a point mass's (a craft set 100 km down left
            // its pocket at 14 m/s² before it was let go).
            if (vessel.packed && vessel.LandedOrSplashed && vessel.orbitDriver != null
                && vessel.orbitDriver.updateMode == OrbitDriver.UpdateMode.UPDATE && (state.Inside || Underground.BuriedDepth(vessel) > 3))
            {
                vessel.orbitDriver.SetOrbitMode(OrbitDriver.UpdateMode.IDLE);
            }

            if (!state.Inside)
            {
                Release(vessel);
                return;
            }

            // Packed vessels are checked every frame (they are the ones waiting); unpacked ones 5 times a second.
            if (vessel.packed || Active || Time.realtimeSinceStartup >= nextCheck)
            {
                nextCheck = Time.realtimeSinceStartup + 0.2f;
                ChunkStreamer streamer = ChunkStreamer.Instance;
                needed = streamer == null || !streamer.CollidersReady(vessel.mainBody, vessel.GetWorldPos3D(), Radius);
            }

            // A vessel already off rails is only pinned when nothing holds it up: colliders being rebuilt around it (a mole or a
            // laser digging under it, a winch lowering it into new chunks) yanked it onto rails mid-move, and it came off them
            // with a jolt that broke parts. Held by Subterra's winch or mole, or with ground under it, it is left alone.
            // In water too (an ocean under the ice, a flooded cave): it floats, nothing to fall through (Lucas, in Tylo's ocean:
            // "I can't even move"; the guard put the craft back on rails every few seconds as it swam out of its pocket).
            if (needed && !vessel.packed && (HeldBySubterra(vessel) || GroundUnder(vessel) || InWater(vessel)))
            {
                needed = false;
            }

            if (!needed)
            {
                Release(vessel);
                return;
            }

            if (!Active)
            {
                Active = true;
                Log.Info($"Load guard on for {vessel.GetDisplayName()} at UT {Planetarium.GetUniversalTime():F1} (cave colliders not ready, {(vessel.packed ? "packed" : "unpacked: back on rails")})");
            }

            if (vessel == FlightGlobals.ActiveVessel)
            {
                OrbitPhysicsManager.HoldVesselUnpack(2);
                if (!vessel.packed)
                {
                    // Colliders went missing under an unpacked vessel: pin it where it is.
                    vessel.Landed = true;
                    vessel.GoOnRails();
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

        /// <summary>How far down (m) a collider under an unpacked vessel counts as ground holding it.</summary>
        private const float GroundReach = 30f;

        private static bool GroundUnder(Vessel vessel)
        {
            Vector3 down = -(Vector3)(vessel.CoMD - vessel.mainBody.position).normalized;
            return Physics.Raycast(vessel.CoMD, down, GroundReach, 1 << ChunkObject.Layer, QueryTriggerInteraction.Ignore);
        }

        private static bool InWater(Vessel vessel)
        {
            SubterraWaterModule water = SubterraWaterModule.Of(vessel);
            return (water != null && water.AnyWet)
                || Underground.OceanAt(vessel.mainBody, vessel.CoMD) is Core.Water.WaterSample s && s.Under;
        }

        private static bool HeldBySubterra(Vessel vessel)
        {
            foreach (Part p in vessel.parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    if ((m is ModuleSubterraWinch winch && winch.hooked) || (m is ModuleSubterraMole mole && mole.boring))
                    {
                        return true;
                    }
                }
            }

            return false;
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

            Log.Info($"Load guard off for {vessel.GetDisplayName()} at UT {Planetarium.GetUniversalTime():F1}");
        }

        private static IEnumerable<VesselRanges.Situation> Situations(Vessel v)
        {
            VesselRanges r = v.vesselRanges;
            return new[] { r.landed, r.prelaunch, r.flying, r.splashed, r.subOrbital };
        }
    }
}
