using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Time of day and part temperature steps/assertions.</summary>
    [StepProvider]
    internal static class TimeSteps
    {
        private static readonly System.Collections.Generic.Dictionary<string, double> TempSnapshots = new System.Collections.Generic.Dictionary<string, double>();

        internal static void Register()
        {
            StepRegistry.Register("setNoon", SetNoon);
            StepRegistry.RegisterAssertion("partTempNear", PartTempNear);
            StepRegistry.RegisterAssertion("partTempSnapshot", PartTempSnapshot);
            StepRegistry.RegisterAssertion("partTempRise", PartTempRise);
        }

        // setNoon body = Mun lon = 0 — moves the clock until the sun is within 2° of the zenith at (0, lon).
        private static IEnumerator SetNoon(ConfigNode node, TestContext ctx)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(node.Arg("body", "Mun")) ?? throw new ArgumentException("unknown body");
            double lon = node.ArgDouble("lon", 0);
            Vessel vessel = FlightGlobals.ActiveVessel;
            double angle = 180;
            for (int attempt = 0; attempt < 6 && angle > 2; attempt++)
            {
                angle = SignedAngle(body, lon, out double signed);
                if (angle <= 2)
                {
                    break;
                }

                // The vertical turns towards the sun at the solar-day rate.
                double solarDay = SolarDay(body);
                double dt = (signed < 0 ? signed + 360 : signed) / 360.0 * solarDay;
                if (vessel != null)
                {
                    OrbitPhysicsManager.HoldVesselUnpack(5);
                    vessel.GoOnRails();
                }

                Planetarium.SetUniversalTime(Planetarium.GetUniversalTime() + dt);
                for (int i = 0; i < 5; i++)
                {
                    yield return new WaitForFixedUpdate();
                }

                angle = SignedAngle(body, lon, out _);
            }

            if (angle > 2)
            {
                ctx.Fail(Invariant($"sun still {angle:F1}° from the zenith"));
            }
            else
            {
                ctx.Pass(Invariant($"UT {Planetarium.GetUniversalTime():F0}, sun {angle:F2}° from the zenith at lon {lon}"));
            }
        }

        // Unsigned angle between the vertical at (0, lon) and the sun; signed = rotation still needed (degrees, about the body's axis).
        private static double SignedAngle(CelestialBody body, double lon, out double signed)
        {
            Vector3d up = (body.GetWorldSurfacePosition(0, lon, 0) - body.position).normalized;
            Vector3d sun = (Planetarium.fetch.Sun.position - body.position).normalized;
            Vector3d axis = body.angularVelocity.normalized;
            Vector3d a = Vector3d.Exclude(axis, up).normalized, b = Vector3d.Exclude(axis, sun).normalized;
            signed = Vector3d.Angle(a, b) * Math.Sign(Vector3d.Dot(Vector3d.Cross(a, b), axis));
            return Vector3d.Angle(up, sun);
        }

        private static double SolarDay(CelestialBody body)
        {
            CelestialBody planet = body;
            while (planet.referenceBody != null && planet.referenceBody != Planetarium.fetch.Sun)
            {
                planet = planet.referenceBody;
            }

            double year = planet.orbit != null ? planet.orbit.period : double.PositiveInfinity;
            double rate = 1 / body.rotationPeriod - 1 / year;
            return 1 / rate;
        }

        private static double MeanSkinTemp(Vessel v) => v.parts.Average(p => p.skinTemperature);

        // partTempNear k = 250.3 tol = 5 — every part's skin and internal temperature within tol of k.
        private static bool PartTempNear(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            if (v == null)
            {
                ctx.Detail = "no vessel";
                return false;
            }

            double k = node.ArgDouble("k", 250), tol = node.ArgDouble("tol", 5);
            double worst = v.parts.Max(p => Math.Max(Math.Abs(p.skinTemperature - k), Math.Abs(p.temperature - k)));
            ctx.Detail = Invariant($"mean skin {MeanSkinTemp(v):F1} K, worst deviation {worst:F1} K from {k} (rock {UndergroundTracker.For(v).RockTemp:F1} K, sun {v.directSunlight})");
            return worst <= tol;
        }

        private static bool PartTempSnapshot(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            if (v == null)
            {
                ctx.Detail = "no vessel";
                return false;
            }

            TempSnapshots[node.RequireArg("name")] = MeanSkinTemp(v);
            ctx.Detail = Invariant($"mean skin {MeanSkinTemp(v):F1} K");
            return true;
        }

        // partTempRise ref = cave min = 20 — the mean skin temperature rose at least min K since the snapshot.
        private static bool PartTempRise(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            if (v == null)
            {
                ctx.Detail = "no vessel";
                return false;
            }

            if (!TempSnapshots.TryGetValue(node.RequireArg("ref"), out double before))
            {
                ctx.Detail = "no snapshot";
                return false;
            }

            double rise = MeanSkinTemp(v) - before;
            ctx.Detail = Invariant($"mean skin {before:F1} → {MeanSkinTemp(v):F1} K (+{rise:F1}), sun {v.directSunlight}");
            return rise >= node.ArgDouble("min", 20);
        }
    }
}
