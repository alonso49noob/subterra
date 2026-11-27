using System;
using HarmonyLib;
using KSP.Localization;
using Subterra.Core.Environment;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Patch <c>interiorGravity</c>. Inside a body gravity falls linearly to zero at the centre (a uniform body: only the
    /// mass deeper than you pulls), instead of KSP's point mass growing as 1/r². Lucas saw a GRAVMAX read 11 m/s² 35 km down
    /// on Kerbin: <c>FlightGlobals.getGeeForceAtPosition</c> is so small the JIT inlines it into its callers, so patching it
    /// changed nothing. The gravity <c>VesselPrecalculate.CalculateGravity</c> works out is corrected
    /// right after it, and so is the GRAVMAX's reading.
    /// </summary>
    public static class InteriorGravityPatch
    {
        public const string Id = "interiorGravity";

        public static void Register()
        {
            PatchManager.Register(Id, harmony =>
            {
                harmony.Patch(AccessTools.Method(typeof(VesselPrecalculate), nameof(VesselPrecalculate.CalculateGravity))
                        ?? throw new MissingMethodException("VesselPrecalculate", "CalculateGravity"),
                    prefix: new HarmonyMethod(typeof(InteriorGravityPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(InteriorGravityPatch), nameof(Postfix)));
                harmony.Patch(AccessTools.Method(typeof(Vessel), nameof(Vessel.GoOnRails))
                        ?? throw new MissingMethodException("Vessel", "GoOnRails"),
                    prefix: new HarmonyMethod(typeof(InteriorGravityPatch), nameof(GoOnRailsPrefix)));
                harmony.Patch(AccessTools.Method(typeof(ModuleEnviroSensor), "FixedUpdate")
                        ?? throw new MissingMethodException("ModuleEnviroSensor", "FixedUpdate"),
                    postfix: new HarmonyMethod(typeof(InteriorGravityPatch), nameof(SensorPostfix)));
            });
        }

        /// <summary>Gravity at <paramref name="world"/> inside <paramref name="body"/> over KSP's point-mass value there.</summary>
        public static double Factor(CelestialBody body, Vector3d world)
        {
            if (body == null || body.Radius <= 0)
            {
                return 1;
            }

            double s = (world - body.position).magnitude / body.Radius;
            return s >= 1 ? 1 : s * s * s;
        }

        // After KSP worked out the gravity to integrate (point mass, times its multipliers), the part of it the mass farther
        // out than the vessel would not pull is taken off: from the true gravity, from what the parts are pushed by and from
        // what the vessel reports. (Scaling vessel.gravityMultiplier instead was undone by KSP's easing after a Set Position:
        // it sets the multiplier back to 1 inside this very call.) The tiny change of the rotating frame's correction with
        // the step is left out.
        // KSP's orbit drift compensation moves a craft that is not landed along its Kepler orbit, whose gravity is a point
        // mass's: underground it pulled a craft set 500 km down out of its pocket at hundreds of m/s² (Lucas: it ignored its
        // pocket and kept going down). Off for craft underground.
        private static void Prefix(Vessel ___vessel, out bool __state)
        {
            __state = VesselPrecalculate.allowDriftCompensation;
            try
            {
                if (___vessel != null && UndergroundMap.IsUnderground(___vessel))
                {
                    VesselPrecalculate.allowDriftCompensation = false;
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        // Put on rails, a craft that is not landed flies its Kepler orbit (a point mass's gravity): one underground is pinned
        // where it is instead (landed), as KSP does with craft on the ground.
        private static void GoOnRailsPrefix(Vessel __instance)
        {
            try
            {
                if (__instance != null && !__instance.packed && !__instance.LandedOrSplashed && UndergroundMap.IsUnderground(__instance))
                {
                    __instance.Landed = true;
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        private static void Postfix(VesselPrecalculate __instance, Vessel ___vessel, bool __state)
        {
            VesselPrecalculate.allowDriftCompensation = __state;
            try
            {
                if (___vessel == null)
                {
                    return;
                }

                double s = Factor(___vessel.mainBody, ___vessel.CoMD);
                if (s >= 1)
                {
                    return;
                }

                Vector3d pointMass = __instance.gAccelTrue;
                __instance.gAccelTrue = pointMass * s;
                ___vessel.gravityTrue = __instance.gAccelTrue;

                // Packed, or KSP correcting orbit drift (it zeroes the integration and works from the orbit): nothing more.
                if (___vessel.packed || __instance.integrationAccel.sqrMagnitude == 0)
                {
                    return;
                }

                Vector3d excess = pointMass * ((1 - s) * PhysicsGlobals.GraviticForceMultiplier * ___vessel.gravityMultiplier);
                __instance.integrationAccel -= excess;
                __instance.gAccel -= excess;
                ___vessel.gravityForPos = __instance.integrationAccel;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        // The GRAVMAX shows KSP's point-mass gravity at the part: underground, the gravity there instead. The barometer shows
        // KSP's air pressure: in Subterra's water (a flooded cave, an ocean under the ice), the water's pressure instead
        // (Lucas: in Vall's ocean it read "IN VACUUM!", Vall having no air).
        private static void SensorPostfix(ModuleEnviroSensor __instance)
        {
            try
            {
                if (__instance.vessel == null)
                {
                    return;
                }

                if (__instance.sensorType == ModuleEnviroSensor.SensorType.PRES)
                {
                    WaterReading(__instance);
                    return;
                }

                if (__instance.sensorType != ModuleEnviroSensor.SensorType.GRAV)
                {
                    return;
                }

                Vector3d at = __instance.transform.position;
                double factor = Factor(__instance.vessel.mainBody, at);
                if (factor >= 1)
                {
                    return;
                }

                double pointMass = FlightGlobals.getGeeForceAtPosition(at, __instance.vessel.mainBody).magnitude;
                string shown = Localizer.Format("#autoLOC_237120", pointMass.ToString("00.00"));
                if (__instance.readoutInfo == shown)
                {
                    __instance.readoutInfo = Localizer.Format("#autoLOC_237120", (pointMass * factor).ToString("00.00"));
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        private static void WaterReading(ModuleEnviroSensor sensor)
        {
            // Only while KSP itself updates the reading (the sensor on, its part's window open).
            if (!sensor.sensorActive || UIPartActionController.Instance == null || !UIPartActionController.Instance.ItemListContains(sensor.part, false))
            {
                return;
            }

            // The three states (Lucas: the PresMat should count gas, liquid and solid, and read the rock's pressure on airless
            // bodies too, the Mun's core for one): the air, the water over the sensor's own part (Subterra's or KSP's sea;
            // an ocean's ice ceiling with it), and a share of the weight of the rock over it.
            SubterraWaterModule water = SubterraWaterModule.Of(sensor.vessel);
            Part part = sensor.part;
            CelestialBody body = sensor.vessel.mainBody;
            Vector3d at = part.transform.position;
            double gas = Math.Max(0, sensor.vessel.staticPressurekPa), fluid;
            bool inOcean = Underground.OceanAt(body, at) is Core.Water.WaterSample sea && sea.Under;
            if (water != null && water.PressureOn(part, out double wet))
            {
                fluid = wet;
            }
            else if (CaveWaterKPa(body, at, gas) is double cave)
            {
                // A cave's lake round a physicsless part (the PresMat is one): the water module only weighs parts with physics
                // (Lucas: the PresMat did not count the lakes' pressure).
                fluid = cave;
            }
            else if (part.WaterContact && part.submergedPortion > 0)
            {
                fluid = part.staticPressureAtm * 101.325;
            }
            else if (inOcean && BodyRegistry.Instance.TryGet(body, out BodyRuntime rt) && rt.Ocean != null)
            {
                // In an ocean under the ice the water module had no reading for (Lucas, Minmus: "sigo en el vacío").
                Core.Water.WaterSample s = Underground.OceanAt(body, at).Value;
                double g = FlightGlobals.getGeeForceAtPosition(at, body).magnitude * Factor(body, at);
                Core.Geo.BodyFrame.ToLatLon(Frames.ToBody(body, at), out double lat, out _);
                fluid = Core.Water.WaterPhysics.PressureKPa(s.Depth, s.Density, g, gas + rt.Ocean.OverburdenKPa(lat, g));
            }
            else
            {
                fluid = Math.Max(gas, part.staticPressureAtm * 101.325);
            }

            // The rock: its weight from the surface down to the sensor. In an ocean, the ice or rock of its ceiling (Lucas: the
            // barometer counted one state only there: the water's reading held the ceiling's whole weight, real size, and the
            // solid read 0): taken out of the liquid and shown as solid, at the rock's scale.
            double rock;
            if (inOcean && BodyRegistry.Instance.TryGet(body, out BodyRuntime ort) && ort.Ocean != null)
            {
                double g = FlightGlobals.getGeeForceAtPosition(at, body).magnitude * Factor(body, at);
                Core.Geo.BodyFrame.ToLatLon(Frames.ToBody(body, at), out double olat, out _);
                double ceiling = ort.Ocean.OverburdenKPa(olat, g);
                fluid = gas + Math.Max(0, fluid - gas - ceiling);
                rock = ceiling * Settings.Current.RockPressureScale;
            }
            else
            {
                rock = RockKPa(body, at);
            }
            double total = fluid + rock;
            if (total <= 1e-6)
            {
                return;
            }

            string unit = Localizer.Format("#autoLOC_7001410");
            sensor.readoutInfo = rock > 0.005 || fluid - gas > 0.005
                ? Localizer.Format("#Subterra_Pres_Reading", Kpa(total), unit, Kpa(gas), Kpa(Math.Max(0, fluid - gas)), Kpa(rock))
                : Kpa(total) + " " + unit;
        }

        /// <summary>The pressure (kPa) at a world point under a cave's water (the air on its surface plus the water over it), or null.</summary>
        public static double? CaveWaterKPa(CelestialBody body, Vector3d world, double airKPa)
        {
            if (body == null || !Underground.Locate(body, world, out Core.Tubes.TubeNetwork net, out Core.Numerics.Vec3 local) || net.Waters.Count == 0)
            {
                return null;
            }

            Core.Water.WaterSample? sample = net.WaterAt(local, margin: 1f, above: 0f);
            if (sample == null || !sample.Value.Under || sample.Value.Depth <= 0)
            {
                return null;
            }

            double g = FlightGlobals.getGeeForceAtPosition(world, body).magnitude * Factor(body, world);
            return Core.Water.WaterPhysics.PressureKPa(sample.Value.Depth, sample.Value.Density, g, airKPa);
        }

        private static string Kpa(double kPa) =>
            kPa < 10 ? kPa.ToString("0.00##") : kPa < 1000 ? kPa.ToString("0.0") : kPa.ToString("#,0");

        /// <summary>The PresMat's share of the weight of the rock over a world point (kPa); 0 above the ground.</summary>
        public static double RockKPa(CelestialBody body, Vector3d world)
        {
            if (body == null || Settings.Current.RockPressureScale <= 0 || !BodyRegistry.Instance.TryGet(body, out BodyRuntime rt))
            {
                return 0;
            }

            double lat = body.GetLatitude(world), lon = body.GetLongitude(world);
            double depth = KspSampler.SurfaceRadius(body, lat, lon) - (world - body.position).magnitude;
            if (depth <= 0.5)
            {
                return 0;
            }

            var spans = rt.Strata.Spans(KspSampler.Biome(body, lat, lon));
            return Core.Environment.Lithostatic.PressureKPa(spans, depth, body.GeeASL * PhysicsGlobals.GravitationalAcceleration, body.Radius)
                * Settings.Current.RockPressureScale;
        }

        /// <summary>Gravity (m/s²) at a depth below the datum of <paramref name="body"/> (for tests and tools).</summary>
        public static double GravityAtDepth(CelestialBody body, double depth) =>
            body.gMagnitudeAtCenter / (body.Radius * body.Radius) * AirColumn.GravityFactor(depth, body.Radius);
    }
}
