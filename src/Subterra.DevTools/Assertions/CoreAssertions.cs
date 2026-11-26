using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>The built-in assertions for <c>assert what = …</c> and <c>waitUntil what = …</c>.</summary>
    [StepProvider]
    internal static class CoreAssertions
    {
        internal static void Register()
        {
            StepRegistry.RegisterAssertion("vesselAlive", VesselAlive);
            StepRegistry.RegisterAssertion("airBetween", AirBetween);
            StepRegistry.RegisterAssertion("gravityBetween", GravityBetween);
            StepRegistry.RegisterAssertion("vesselTempBetween", VesselTempBetween);
            StepRegistry.RegisterAssertion("waterPressureBetween", WaterPressureBetween);
            StepRegistry.RegisterAssertion("partPressureBetween", PartPressureBetween);
            StepRegistry.RegisterAssertion("vesselLanded", VesselLanded);
            StepRegistry.RegisterAssertion("logContains", LogContains);
            StepRegistry.RegisterAssertion("noSubterraExceptions", NoSubterraExceptions);
        }

        // airBetween minKPa = 1 maxKPa = 10 — static pressure where the tracked vessel is (and the air temperature, reported).
        private static bool AirBetween(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            double kPa = vessel.staticPressurekPa;
            ctx.Detail = Invariant($"{kPa:F2} kPa, air {vessel.externalTemperature:F0} K, altitude {vessel.altitude:F0} m on {vessel.mainBody.bodyName}");
            return kPa >= node.ArgDouble("minKPa", 0) && kPa <= node.ArgDouble("maxKPa", double.MaxValue);
        }

        // partPressureBetween min = 100 max = 1000 — the highest pressure (kPa) KSP has on any part of the tracked vessel
        // (part.staticPressureAtm: the air, and the water over it under water) is within the range.
        private static bool PartPressureBetween(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            double most = 0;
            bool wet = false;
            foreach (Part p in vessel.parts)
            {
                most = Math.Max(most, p.staticPressureAtm * 101.325);
                wet |= p.WaterContact;
            }

            ctx.Detail = Invariant($"{most:F1} kPa on the most pressed part, air {vessel.staticPressurekPa:F1} kPa, {(wet ? "wet" : "dry")}, altitude {vessel.altitude:F0} m on {vessel.mainBody.bodyName}");
            return most >= node.ArgDouble("min", 0) && most <= node.ArgDouble("max", double.MaxValue);
        }

        // waterPressureBetween min = 1000 max = 50000 — the water's pressure (kPa) on the most pressed wet part of the tracked
        // vessel (what its barometer shows) is within the range.
        private static bool WaterPressureBetween(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            SubterraWaterModule water = SubterraWaterModule.Of(vessel);
            double most = -1;
            foreach (Part p in vessel.parts)
            {
                if (water != null && water.PressureOn(p, out double kPa))
                {
                    most = Math.Max(most, kPa);
                }
            }

            ctx.Detail = most < 0 ? $"no part in Subterra's water (air {vessel.staticPressurekPa:F2} kPa)" : Invariant($"{most:F0} kPa ({most / 101.325:F0} atm), altitude {vessel.altitude:F0} m on {vessel.mainBody.bodyName}");
            return most >= node.ArgDouble("min", 0) && most <= node.ArgDouble("max", double.MaxValue);
        }

        // vesselTempBetween min = 2000 max = 9999 — the hottest part of the tracked vessel (K) is within the range; the air and
        // background temperatures are reported.
        private static bool VesselTempBetween(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            double hottest = 0;
            foreach (Part p in vessel.parts)
            {
                hottest = Math.Max(hottest, p.temperature);
            }

            FlightIntegrator fi = vessel.GetComponent<FlightIntegrator>();
            ctx.Detail = Invariant($"hottest part {hottest:F0} K, air {vessel.externalTemperature:F0} K, background {(fi != null ? fi.backgroundRadiationTemp : double.NaN):F0} K, {vessel.staticPressurekPa:F0} kPa, altitude {vessel.altitude:F0} m, warp {TimeWarp.CurrentRate:F0}x");
            return hottest >= node.ArgDouble("min", 0) && hottest <= node.ArgDouble("max", double.MaxValue);
        }

        // gravityBetween min = 9 max = 9.5 — the gravity KSP integrates for the tracked vessel (m/s²), and the reading of any
        // gravity sensor on it (which must agree), are within the range.
        private static bool GravityBetween(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            double min = node.ArgDouble("min", 0), max = node.ArgDouble("max", double.MaxValue);
            double integrated = vessel.precalc != null ? vessel.precalc.integrationAccel.magnitude : vessel.graviticAcceleration.magnitude;
            double pointMass = FlightGlobals.getGeeForceAtPosition(vessel.CoMD, vessel.mainBody).magnitude;
            bool ok = integrated >= min && integrated <= max;
            string sensors = "";
            foreach (Part p in vessel.parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    if (m is ModuleEnviroSensor s && s.sensorType == ModuleEnviroSensor.SensorType.GRAV)
                    {
                        sensors += $" sensor '{s.readoutInfo}'";
                        string digits = new string(s.readoutInfo.TrimStart().TakeWhile(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
                        if (double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double read))
                        {
                            ok &= read >= min && read <= max;
                        }

                        // Not a number: the sensor only updates while its part's window is open (left out).
                    }
                }
            }

            ctx.Detail = Invariant($"integrated {integrated:F2} m/s² (point mass {pointMass:F2}), altitude {vessel.altitude:F0} m on {vessel.mainBody.bodyName};{sensors}");
            return ok;
        }

        // vesselAlive — the tracked vessel still exists, is not dead and has all the parts it had
        // when it was tracked (launch, eva, switchTo).
        private static bool VesselAlive(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null)
            {
                ctx.Detail = "the tracked vessel is gone";
                return false;
            }

            int parts = TestContext.PartCount(vessel);
            bool dead = vessel.state == Vessel.State.DEAD;
            ctx.Detail = Invariant(
                $"{vessel.GetDisplayName()}: {parts}/{ctx.TrackedPartCount} parts{(dead ? ", DEAD" : "")}, {vessel.situation} at {vessel.mainBody.bodyName}, altitude {vessel.altitude:F1} m");
            // minParts = 20: at least that many parts left (a test that may lose a fragile part or two); default: all.
            return !dead && parts >= node.ArgDouble("minParts", ctx.TrackedPartCount);
        }

        // vesselLanded — the tracked vessel is off rails and touching the ground (a packed vessel's Landed
        // flag is only the pin set by a teleport).
        private static bool VesselLanded(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null)
            {
                ctx.Detail = "the tracked vessel is gone";
                return false;
            }

            ctx.Detail = Invariant($"{vessel.GetDisplayName()}: {vessel.situation}{(vessel.packed ? " (packed)" : "")} at {vessel.mainBody.bodyName}, altitude {vessel.altitude:F1} m, {vessel.heightFromTerrain:F1} m above terrain");
            return !vessel.packed && vessel.Landed;
        }

        // logContains text = "…" — a message logged since the DevTools assembly loaded contains the
        // text (case-sensitive; surrounding quotes are not part of it).
        private static bool LogContains(ConfigNode node, TestContext ctx)
        {
            string text = node.RequireArg("text");
            // The runner's own lines (which quote assertions and their texts) do not count.
            bool found = LogCapture.FindAll(entry => entry.Message.Contains(text) && !entry.Message.StartsWith("[Subterra.DevTools]")).Count > 0;
            ctx.Detail = found ? null : $"no log message contains \"{text}\"";
            return found;
        }

        // noSubterraExceptions — no exception logged whose message or stack trace mentions Subterra.
        private static bool NoSubterraExceptions(ConfigNode node, TestContext ctx)
        {
            List<LogCapture.Entry> hits = LogCapture.FindAll(IsSubterraException);
            if (hits.Count == 0)
            {
                return true;
            }

            string first = hits[0].Message;
            ctx.Detail = $"{hits.Count} found, first: {(first.Length > 200 ? first.Substring(0, 200) + "…" : first)}";
            return false;
        }

        private static bool IsSubterraException(LogCapture.Entry entry)
        {
            bool exception = entry.Type == LogType.Exception
                || ((entry.Type == LogType.Error || entry.Type == LogType.Assert)
                    && entry.Message.IndexOf("Exception", StringComparison.Ordinal) >= 0);
            return exception
                && (entry.Message.IndexOf("Subterra", StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.StackTrace.IndexOf("Subterra", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
