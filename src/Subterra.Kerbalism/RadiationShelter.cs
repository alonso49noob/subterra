using System;
using HarmonyLib;
using UnityEngine;

[assembly: KSPAssemblyDependency("Kerbalism", 3, 0)]
[assembly: KSPAssemblyDependency("Subterra", 0, 1)]

namespace Subterra.Kerbalism
{
    /// <summary>
    /// Optional: loaded by KSP only when Kerbalism is installed. Rock overhead shields the environment's
    /// radiation: Kerbalism's radiation at a vessel in a cave is multiplied by max(sky visibility,
    /// exp(−depth / 3 m)). Kerbalism has no API for this, so its KERBALISM.Radiation.Compute is patched
    /// (found by name: no build-time reference to Kerbalism, which targets .NET 4.8). Patch id
    /// <c>kerbalismRadiation</c> (can be turned off in Settings.cfg).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class RadiationShelter : MonoBehaviour
    {
        public const string Id = "kerbalismRadiation";

        /// <summary>The factor applied to a vessel's environment radiation.</summary>
        public static double Factor(UndergroundState state) =>
            state.Inside ? Math.Max(state.Sky, Math.Exp(-state.Depth / 3.0)) : 1.0;

        private void Start()
        {
            PatchManager.ApplyNow(Id, harmony =>
            {
                Type radiation = AccessTools.TypeByName("KERBALISM.Radiation") ?? throw new MissingMemberException("KERBALISM.Radiation");
                Type outBool = typeof(bool).MakeByRefType();
                var parameters = new[] { typeof(Vessel), typeof(Vector3d), typeof(double), typeof(double), outBool, outBool, outBool, outBool, outBool, typeof(double).MakeByRefType() };
                harmony.Patch(AccessTools.Method(radiation, "Compute", parameters) ?? throw new MissingMethodException("KERBALISM.Radiation", "Compute"),
                    postfix: new HarmonyMethod(typeof(RadiationShelter), nameof(ComputePostfix)));
                Log.Info("Kerbalism radiation shelter on");
            });
            Destroy(gameObject);
        }

        private static void ComputePostfix(Vessel v, ref double __result, ref double shieldedRadiation)
        {
            try
            {
                if (v == null || v.mainBody == null)
                {
                    return;
                }

                double factor = Factor(UndergroundTracker.For(v));
                if (factor < 1.0)
                {
                    __result *= factor;
                    shieldedRadiation *= factor;
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }
    }
}
