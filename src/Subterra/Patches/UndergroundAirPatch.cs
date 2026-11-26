using System;
using System.Collections.Generic;
using HarmonyLib;
using Subterra.Core.Environment;

namespace Subterra
{
    /// <summary>
    /// Patch <c>undergroundAir</c>. Below a body's datum KSP reads its atmosphere curves at a negative altitude: the stock
    /// curves clamp (sea-level air all the way to the core) and Kopernicus' wrap round (Lucas's barometer read 0.17 kPa 35 km
    /// down on Kerbin, the pressure 35 km up). Below the datum the pressure now grows going down and the air takes the
    /// rock's temperature (<see cref="AirColumn"/>), and KSP's overpressure check leaves parts underground alone (Lucas: no
    /// pressure limit, it held testing and players back; <c>waterCrush = true</c> brings it back).
    /// </summary>
    public static class UndergroundAirPatch
    {
        public const string Id = "undergroundAir";

        private static readonly Dictionary<string, (AirColumn Column, double Pressure)> Columns = new Dictionary<string, (AirColumn, double)>();
        private static Part.voidPartDelegate stockPressureCheck;
        private static bool building;

        public static void Register()
        {
            PatchManager.Register(Id, harmony =>
            {
                harmony.Patch(AccessTools.Method(typeof(CelestialBody), nameof(CelestialBody.GetPressure), new[] { typeof(double) })
                        ?? throw new MissingMethodException("CelestialBody", "GetPressure"),
                    prefix: new HarmonyMethod(typeof(UndergroundAirPatch), nameof(PressurePrefix)));
                harmony.Patch(AccessTools.Method(typeof(CelestialBody), nameof(CelestialBody.GetTemperature), new[] { typeof(double) })
                        ?? throw new MissingMethodException("CelestialBody", "GetTemperature"),
                    prefix: new HarmonyMethod(typeof(UndergroundAirPatch), nameof(TemperaturePrefix)));
                harmony.Patch(AccessTools.Method(typeof(FlightIntegrator), "DragCubeSetupAndPartAeroStats")
                        ?? throw new MissingMethodException("FlightIntegrator", "DragCubeSetupAndPartAeroStats"),
                    postfix: new HarmonyMethod(typeof(UndergroundAirPatch), nameof(PartPressurePostfix)));
                if (stockPressureCheck == null)
                {
                    stockPressureCheck = Part.CheckPartPressure;
                    Part.CheckPartPressure = CheckPartPressure;
                }
            });
        }

        /// <summary>The air column of <paramref name="body"/> and its datum pressure (kPa); null without an atmosphere.</summary>
        public static AirColumn ColumnOf(CelestialBody body, out double datumPressure)
        {
            datumPressure = 0;
            if (body == null || !body.atmosphere || building)
            {
                return null;
            }

            if (!Columns.TryGetValue(body.bodyName, out var entry))
            {
                building = true;
                try
                {
                    // The datum's pressure and scale height from KSP's own curve (just above the datum, where it is right).
                    double p0 = body.GetPressure(0), p1 = body.GetPressure(100);
                    double scaleHeight = p0 > 0 && p1 > 0 && p1 < p0 ? 100 / Math.Log(p0 / p1) : 7000;
                    double t0 = body.GetTemperature(0);
                    Func<double, double?> rock = null;
                    if (BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
                    {
                        rock = depth => runtime.Geotherm.Temperature(depth, "");
                    }

                    entry = (new AirColumn(body.Radius, scaleHeight, t0, rock), p0);
                    Columns[body.bodyName] = entry;
                    Log.Info($"Underground air on {body.bodyName}: {p0:F1} kPa and {t0:F0} K at the datum, scale height {scaleHeight:F0} m; " +
                        $"10 km down {p0 * entry.Column.PressureFactor(10000):F0} kPa {entry.Column.Temperature(10000):F0} K, at the centre {p0 * entry.Column.PressureFactor(body.Radius):F0} kPa");
                }
                finally
                {
                    building = false;
                }
            }

            datumPressure = entry.Pressure;
            return entry.Column;
        }

        private static bool PressurePrefix(CelestialBody __instance, double altitude, ref double __result)
        {
            if (altitude >= 0 || !Settings.Current.UndergroundAir)
            {
                return true;
            }

            try
            {
                AirColumn column = ColumnOf(__instance, out double p0);
                if (column == null)
                {
                    return true;
                }

                __result = p0 * column.PressureFactor(-altitude);
                return false;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
                return true;
            }
        }

        private static bool TemperaturePrefix(CelestialBody __instance, double altitude, ref double __result)
        {
            if (altitude >= 0 || !Settings.Current.UndergroundAir)
            {
                return true;
            }

            try
            {
                AirColumn column = ColumnOf(__instance, out _);
                if (column == null)
                {
                    return true;
                }

                __result = column.Temperature(-altitude);
                return false;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
                return true;
            }
        }

        // The pressure on each part (Lucas: hydrostatic pressure the barometers read). KSP sets it to the air at the part plus,
        // below sea level on a body with a sea, the sea's water over it: also in a dry cave dug under Kerbin's sea level,
        // and nothing for Subterra's water. Now: in Subterra's water, its pressure (the air on it, the ice or rock over an
        // ocean, the water above); underground and dry, the air alone; elsewhere KSP's own (its sea included).
        private static void PartPressurePostfix(Vessel v)
        {
            try
            {
                if (v == null || v.mainBody == null || v.parts == null)
                {
                    return;
                }

                SubterraWaterModule water = SubterraWaterModule.Of(v);
                bool underground = UndergroundTracker.For(v).Inside || Underground.BuriedDepth(v) > 0;
                if (!underground && (water == null || !water.AnyWet))
                {
                    return;
                }

                CelestialBody body = v.mainBody;
                foreach (Part part in v.parts)
                {
                    if (water != null && water.PressureOn(part, out double kPa))
                    {
                        part.staticPressureAtm = kPa * KPaToAtm;
                    }
                    else if (underground)
                    {
                        part.staticPressureAtm = body.GetPressure(FlightGlobals.getAltitudeAtPos((Vector3d)part.partTransform.position, body)) * KPaToAtm;
                    }
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        private const double KPaToAtm = 0.009869232667160128;

        // KSP's overpressure check, skipped for parts of vessels in caves, shafts or pockets, in the oceans under ice, or under
        // the terrain at all (a craft just set 500 km down was crushed by 72 MPa of air in its first frames, before its
        // underground state was worked out: that is updated five times a second).
        private static void CheckPartPressure(Part p)
        {
            try
            {
                if (!Settings.Current.WaterCrush && p != null && p.vessel != null
                    && (UndergroundTracker.For(p.vessel).Inside || Underground.OceanAt(p.vessel.mainBody, p.transform.position) != null
                        || Underground.BuriedDepth(p.vessel) > 0))
                {
                    return;
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }

            stockPressureCheck?.Invoke(p);
        }
    }
}
