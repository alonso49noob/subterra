using HarmonyLib;

namespace Subterra
{
    /// <summary>
    /// Patch <c>warp</c>. A craft floating still in Subterra's water (a cave's lake, an ocean under the ice) may time warp as
    /// fast as a craft splashed down in KSP's own sea (Lucas: "no me deja hacer tanto timewarp en los lagos"): the water's
    /// gentle bobbing counted as moving over the terrain or being under acceleration, and the depth as being under the
    /// warp altitude limit. Splashed and nearly still, it goes on rails where it floats.
    /// </summary>
    public static class WarpPatch
    {
        public const string Id = "warp";

        /// <summary>Fastest (m/s) a floating craft may drift and still warp on rails.</summary>
        public const double MaxDrift = 1.5;

        private static System.Guid floatingId;

        public static void Register()
        {
            PatchManager.Register(Id, harmony =>
            {
                harmony.Patch(AccessTools.Method(typeof(TimeWarp), "getMaxOnRailsRateIdx")
                        ?? throw new System.MissingMethodException("TimeWarp", "getMaxOnRailsRateIdx"),
                    postfix: new HarmonyMethod(typeof(WarpPatch), nameof(MaxRatePostfix)));
            });
        }

        private static void MaxRatePostfix(int tgtRateIdx, ref int __result, ref ClearToSaveStatus reason)
        {
            try
            {
                if (__result >= tgtRateIdx || !Floating(FlightGlobals.ActiveVessel))
                {
                    return;
                }

                __result = tgtRateIdx;
                reason = ClearToSaveStatus.CLEAR;
            }
            catch (System.Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        /// <summary>True for a craft in Subterra's water, splashed and nearly still, with no engine pushing it.</summary>
        public static bool Floating(Vessel v)
        {
            if (v == null || !v.loaded)
            {
                return false;
            }

            // Already on rails from floating (the water module leaves packed craft alone): still floating, while splashed.
            if (v.packed)
            {
                return v.id == floatingId && v.Splashed;
            }

            if (v.srfSpeed > MaxDrift || v.ctrlState.mainThrottle > 0.01f)
            {
                floatingId = System.Guid.Empty;
                return false;
            }

            SubterraWaterModule water = SubterraWaterModule.Of(v);
            if (water == null || !water.AnyWet)
            {
                floatingId = System.Guid.Empty;
                return false;
            }

            // On rails it must stay where it floats (a splashed craft's orbit is not flown).
            if (!v.Splashed)
            {
                v.Splashed = true;
            }

            floatingId = v.id;
            return true;
        }
    }
}
