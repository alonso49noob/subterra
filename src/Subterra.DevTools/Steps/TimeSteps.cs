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
            StepRegistry.RegisterAssertion("partTempAway", PartTempAway);
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

        // What the FlightIntegrator is doing with the vessel's heat, for the failure details.
        private static string ThermalInfo(Vessel v)
        {
            FlightIntegrator fi = v.GetComponent<FlightIntegrator>();
            string integrator = fi == null
                ? "no FlightIntegrator"
                : Invariant($"analytic {fi.isAnalytical}, solar {fi.solarFlux:F0} W/m², bg {fi.backgroundRadiationTemp:F1}/{fi.backgroundRadiationTempExposed:F1} K, body flux {fi.bodyEmissiveFlux:F0}+{fi.bodyAlbedoFlux:F0}");
            return Invariant(
                $"UT {Planetarium.GetUniversalTime():F0}, packed {v.packed}, {integrator}; skin {v.parts.Min(p => p.skinTemperature):F1}..{v.parts.Max(p => p.skinTemperature):F1} K, internal {v.parts.Min(p => p.temperature):F1}..{v.parts.Max(p => p.temperature):F1} K, mean skin mass {v.parts.Average(p => p.skinThermalMass):F1} kJ/K, radiative area {v.parts.Sum(p => p.radiativeArea):F2} m², exposed {v.parts.Sum(p => p.exposedArea):F2} m², drag cube area {v.parts.Sum(p => p.DragCubes.Area):F2} ({v.parts.Count(p => p.DragCubes.Area <= 0)} at zero) m²");
        }

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
            ctx.Detail = Invariant($"mean skin {MeanSkinTemp(v):F1} K, worst deviation {worst:F1} K from {k} (rock {UndergroundTracker.For(v).RockTemp:F1} K, sun {v.directSunlight}); {ThermalInfo(v)}");
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
            ctx.Detail = Invariant($"mean skin {MeanSkinTemp(v):F1} K; {ThermalInfo(v)}");
            return true;
        }

        // partTempAway k = 250.3 min = 20 — the mean skin temperature is at least min K away from k (outside the
        // cave KSP's own thermal model applies again).
        private static bool PartTempAway(ConfigNode node, TestContext ctx)
        {
            Vessel v = ctx.TrackedVessel;
            if (v == null)
            {
                ctx.Detail = "no vessel";
                return false;
            }

            double k = node.ArgDouble("k", 250), mean = MeanSkinTemp(v);
            ctx.Detail = Invariant($"mean skin {mean:F1} K vs {k} (sun {v.directSunlight}); {ThermalInfo(v)}");
            return Math.Abs(mean - k) >= node.ArgDouble("min", 20);
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
            ctx.Detail = Invariant($"mean skin {before:F1} → {MeanSkinTemp(v):F1} K (+{rise:F1}), sun {v.directSunlight}; {ThermalInfo(v)}");
            return rise >= node.ArgDouble("min", 20);
        }
    }
}
