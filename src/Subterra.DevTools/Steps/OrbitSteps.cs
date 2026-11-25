using System;
using System.Collections;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Steps that put the active vessel in orbit (radar tasks 14, 15 and 17 use them).</summary>
    [StepProvider]
    internal static class OrbitSteps
    {
        private const double SettleTimeout = 60;

        internal static void Register()
        {
            StepRegistry.Register("setOrbit", SetOrbit);
        }

        // setOrbit body = Mun alt = 30000 inc = 85 lan = 0 - puts the active vessel in a circular orbit of that
        // body (alt in metres above the mean radius, angles in degrees) with FlightGlobals.SetShipOrbit and waits
        // until the vessel is orbiting, unpacked and at that altitude. With pe = 12000 ap = 400000 instead of alt the
        // orbit is elliptical (periapsis and apoapsis altitudes) and the vessel starts at its periapsis.
        private static IEnumerator SetOrbit(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            string bodyName = node.RequireArg("body");
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName) ?? throw new ArgumentException($"unknown body '{bodyName}'");
            double alt = node.ArgDouble("alt", 100000);
            double inc = node.ArgDouble("inc", 0);
            double lan = node.ArgDouble("lan", 0);
            bool elliptical = node.HasValue("pe") || node.HasValue("ap");
            double pe = node.ArgDouble("pe", alt), ap = node.ArgDouble("ap", pe);
            if (alt <= 0 || pe <= 0 || ap < pe)
            {
                throw new ArgumentException("alt and pe must be positive and ap >= pe");
            }

            double rPe = body.Radius + pe, rAp = body.Radius + ap;
            double sma = elliptical ? (rPe + rAp) / 2 : body.Radius + alt;
            double ecc = elliptical ? (rAp - rPe) / (rAp + rPe) : 0;

            // A teleport hold would keep re-landing the vessel.
            if (ctx.Release() != null)
            {
                yield return ctx.WaitFor(() => !vessel.packed, 60, "the released vessel to go off rails");
            }

            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.GetBodyIndex(body), ecc, sma, inc, lan, 0.0, 0.0, Planetarium.GetUniversalTime());
            yield return null;
            yield return null;
            // SetShipOrbit packs the vessel and unpacks it again; wait for that and for the orbit to settle.
            yield return ctx.WaitFor(
                () => FlightGlobals.ActiveVessel == vessel && !vessel.packed && vessel.situation == Vessel.Situations.ORBITING
                    && vessel.mainBody == body
                    && (elliptical
                        ? Math.Abs(vessel.orbit.PeA - pe) < Math.Max(1000, pe * 0.05) && Math.Abs(vessel.orbit.ApA - ap) < Math.Max(1000, ap * 0.05)
                        : Math.Abs(vessel.altitude - alt) < Math.Max(1000, alt * 0.05)),
                SettleTimeout,
                "the vessel to settle in orbit");
            yield return new WaitForSeconds(1f);
            ctx.Pass(Invariant(
                $"{body.bodyName} orbit: alt {vessel.altitude:F0} m, Pe {vessel.orbit.PeA:F0} m, Ap {vessel.orbit.ApA:F0} m, inc {vessel.orbit.inclination:F1} deg, ecc {vessel.orbit.eccentricity:F4}, period {vessel.orbit.period:F0} s, {vessel.situation}"));
        }
    }
}
