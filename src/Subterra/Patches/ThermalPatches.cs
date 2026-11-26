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
                harmony.Patch(AccessTools.Method(typeof(FlightIntegrator), "CalculateAnalyticTemperature"),
                    postfix: new HarmonyMethod(typeof(ThermalPatches), nameof(AnalyticPostfix)));
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
                // Buried in the rock outside any void (a vessel set deep with the cheat menu, its pocket smaller than it): no
                // sun, no light or heat from the surface.
                if (RockTempAround(___vessel).HasValue)
                {
                    __instance.bodyAlbedoFlux = 0;
                    __instance.bodyEmissiveFlux = 0;
                    ___vessel.directSunlight = false;
                    __instance.solarFlux = 0;
                    ___vessel.solarFlux = 0;
                }

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
                double? rock = state.Inside ? state.RockTemp : RockTempAround(___vessel);
                if (rock.HasValue)
                {
                    __result = rock.Value;
                }
            }
            catch (System.Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        // In time warp KSP sets a vessel's temperature to its equilibrium, never above its weakest part's limit (so nothing
        // explodes while warping), even with the "ignore max temperature" cheat. Underground with that cheat on, the
        // equilibrium is let reach the rock's temperature (Lucas, 500 km under Kerbin: it never passed 1000 K in warp).
        private static void AnalyticPostfix(Vessel ___vessel, ref double __result)
        {
            try
            {
                if (!CheatOptions.IgnoreMaxTemperature || ___vessel == null)
                {
                    return;
                }

                UndergroundState state = State(___vessel);
                double? rock = state.Inside ? state.RockTemp : RockTempAround(___vessel);
                if (!rock.HasValue || rock.Value <= __result)
                {
                    return;
                }

                // Only where KSP clipped it at the weakest part (10 K under its limit): below that the equilibrium already
                // counts every flux, a cooler's too.
                double weakest = double.MaxValue;
                foreach (Part p in ___vessel.parts)
                {
                    weakest = System.Math.Min(weakest, p.maxTemp);
                }

                if (__result >= weakest - 11)
                {
                    __result = rock.Value;
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

        /// <summary>Metres under the terrain from which a vessel outside every void counts as buried in the rock.</summary>
        private const double BuriedDepth = 3;

        // The rock's temperature round a vessel under the terrain but in no void (Lucas: 500 km under Kerbin his thermometer
        // read 205 K; the vessel was not inside its small pocket, so it got space's background and the air of 500 km up).
        // Looked up once a second per vessel (terrain height and biome are not free).
        private static readonly System.Collections.Generic.Dictionary<System.Guid, (float time, double? temp)> Buried =
            new System.Collections.Generic.Dictionary<System.Guid, (float, double?)>();

        private static double? RockTempAround(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null)
            {
                return null;
            }

            float now = Time.realtimeSinceStartup;
            if (Buried.TryGetValue(vessel.id, out var cached) && now - cached.time < 1f)
            {
                return cached.temp;
            }

            if (Buried.Count > 256)
            {
                Buried.Clear();
            }

            double? temp = null;
            CelestialBody body = vessel.mainBody;
            if (BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                Vector3d world = vessel.CoMD;
                double lat = body.GetLatitude(world), lon = body.GetLongitude(world);
                double depth = KspSampler.SurfaceRadius(body, lat, lon) - (world - body.position).magnitude;
                if (depth > BuriedDepth)
                {
                    temp = runtime.Geotherm.Temperature(depth, KspSampler.Biome(body, lat, lon));
                }
            }

            Buried[vessel.id] = (now, temp);
            return temp;
        }
    }
}
