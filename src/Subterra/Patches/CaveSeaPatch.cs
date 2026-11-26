using System;
using HarmonyLib;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// KSP's sea does not reach into caves: a part below sea level on a body with an ocean took KSP's own buoyancy and
    /// its "splashed down hard and was destroyed" check, in a dry tunnel dug under Kerbin's sea level (Lucas). In a cave
    /// or tunnel (or just out of its void) <c>PartBuoyancy.FixedUpdate</c> is skipped and the part keeps no water contact;
    /// cave water is Subterra's own (<see cref="SubterraWaterModule"/>).
    /// </summary>
    public static class CaveSeaPatch
    {
        public const string Id = "caveSea";

        /// <summary>Metres under the terrain from which a vessel outside every void counts as buried (not in KSP's sea).</summary>
        private const double BuriedDepth = 3;

        public static void Register()
        {
            PatchManager.Register(Id, harmony => harmony.Patch(
                AccessTools.Method(typeof(PartBuoyancy), "FixedUpdate") ?? throw new MissingMethodException("PartBuoyancy", "FixedUpdate"),
                prefix: new HarmonyMethod(typeof(CaveSeaPatch), nameof(FixedUpdatePrefix))));
        }

        private static bool FixedUpdatePrefix(PartBuoyancy __instance)
        {
            try
            {
                Part part = __instance.GetComponent<Part>();
                Vessel vessel = part != null ? part.vessel : null;
                if (vessel == null)
                {
                    return true;
                }

                // In Subterra's water (a cave's, or an ocean's under the ice): KSP's sea code would clear the part's water
                // contact (no sea there); keep it, so the vessel counts as splashed and a kerbal swims.
                SubterraWaterModule water = SubterraWaterModule.Of(vessel);
                if (water != null && water.InWater(part, out double fraction))
                {
                    part.WaterContact = true;
                    part.submergedPortion = fraction;
                    return false;
                }

                if (!vessel.mainBody.ocean || vessel.altitude > 50)
                {
                    return true;
                }

                // In a void, or buried in the rock (Lucas: a craft set deep under Kerbin with Set Position, its pocket smaller
                // than it, took KSP's sea buoyancy with the point-mass gravity of 500 km down and shot up through the
                // pocket's roof): KSP's sea only lies over the terrain.
                SubterraVesselModule module = SubterraVesselModule.Of(vessel);
                if ((module == null || !module.State.Inside) && Underground.BuriedDepth(vessel) < BuriedDepth)
                {
                    return true;
                }

                part.WaterContact = false;
                part.submergedPortion = 0;
                return false;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
                return true;
            }
        }
    }
}
