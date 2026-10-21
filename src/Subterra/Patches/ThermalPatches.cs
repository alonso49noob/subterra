using HarmonyLib;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Patch <c>thermal</c>. Inside a cave: no sunlight (unless the sun shines down a skylight onto the
    /// vessel) and no light or heat from the body's surface, and the background radiation temperature
    /// is the rock's instead of deep space's: parts neither freeze nor bake.
    /// </summary>
    public static class ThermalPatches
    {
        public const string Id = "thermal";

        public static void Register()
        {
            PatchManager.Register(Id, harmony =>
            {
                harmony.Patch(AccessTools.Method(typeof(FlightIntegrator), "CalculateSunBodyFlux"),
                    postfix: new HarmonyMethod(typeof(ThermalPatches), nameof(SunBodyFluxPostfix)));
                harmony.Patch(AccessTools.Method(typeof(FlightIntegrator), "CalculateBackgroundRadiationTemperature"),
                    postfix: new HarmonyMethod(typeof(ThermalPatches), nameof(BackgroundPostfix)));
            });
        }

        private static void SunBodyFluxPostfix(FlightIntegrator __instance, Vessel ___vessel)
        {
            try
            {
                SunBodyFlux(__instance, ___vessel);
            }
            catch (System.Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        private static void SunBodyFlux(FlightIntegrator __instance, Vessel ___vessel)
        {
            UndergroundState state = State(___vessel);
            if (!state.Inside)
            {
                return;
            }

            __instance.bodyAlbedoFlux = 0;
            __instance.bodyEmissiveFlux = 0;
            if (!SunReaches(__instance, ___vessel, state))
            {
                ___vessel.directSunlight = false;
                __instance.solarFlux = 0;
                ___vessel.solarFlux = 0;
            }
        }

        private static void BackgroundPostfix(Vessel ___vessel, ref double __result)
        {
            try
            {
                UndergroundState state = State(___vessel);
                if (state.Inside && state.RockTemp.HasValue)
                {
                    __result = state.RockTemp.Value;
                }
            }
            catch (System.Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        // The sun moves slowly: the ray through the network (up to 3 km of SDF steps) is cast at most
        // every SunRayInterval seconds per vessel, not in every physics frame.
        private const float SunRayInterval = 0.5f;
        private static readonly System.Collections.Generic.Dictionary<System.Guid, (float time, bool reaches)> SunRays =
            new System.Collections.Generic.Dictionary<System.Guid, (float, bool)>();

        private static bool SunReaches(FlightIntegrator fi, Vessel vessel, UndergroundState state)
        {
            float now = Time.realtimeSinceStartup;
            if (SunRays.TryGetValue(vessel.id, out var cached) && now - cached.time < SunRayInterval)
            {
                return cached.reaches;
            }

            if (SunRays.Count > 256)
            {
                SunRays.Clear();
            }

            Vector3 sun = Quaternion.Inverse(vessel.mainBody.pqsController.transform.rotation) * fi.sunVector;
            bool reaches = state.Net.RayExitsToSky(state.Local, sun.ToCore(), 3000f);
            SunRays[vessel.id] = (now, reaches);
            return reaches;
        }

        private static UndergroundState State(Vessel vessel) =>
            vessel == null ? UndergroundState.Outside : UndergroundTracker.For(vessel);
    }
}
