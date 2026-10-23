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
                if (vessel == null || !vessel.mainBody.ocean || vessel.altitude > 50)
                {
                    return true;
                }

                SubterraVesselModule module = SubterraVesselModule.Of(vessel);
                if (module == null || !module.State.Inside)
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
