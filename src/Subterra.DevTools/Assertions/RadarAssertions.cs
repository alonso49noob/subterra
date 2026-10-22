using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;
using Subterra.Core.Radar;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Assertions for the subsurface radar's coverage and detections.</summary>
    [StepProvider]
    internal static class RadarAssertions
    {
        internal static void Register()
        {
            StepRegistry.RegisterAssertion("coverageAt", CoverageAt);
            StepRegistry.RegisterAssertion("detectionLevel", DetectionLevelIs);
            StepRegistry.RegisterAssertion("radarStatus", RadarStatus);
            StepRegistry.RegisterAssertion("radarInstrument", RadarInstrument);
        }

        // coverageAt body = Mun lat = 0 lon = 0 maxAltitude = 31000 instrument = OrbitalRadar
        // coverageAt at = vessel - the coverage grid has a scan at that point (the active vessel's sub-point with
        // at = vessel; body defaults to the vessel's body then) made from at most maxAltitude metres (default: any).
        private static bool CoverageAt(ConfigNode node, TestContext ctx)
        {
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null)
            {
                ctx.Detail = "no RadarScenario";
                return false;
            }

            string bodyName = node.Arg("body");
            double lat, lon;
            if (node.Arg("at") == "vessel")
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null)
                {
                    ctx.Detail = "no active vessel";
                    return false;
                }

                lat = v.latitude;
                lon = v.longitude;
                bodyName = bodyName ?? v.mainBody.bodyName;
            }
            else
            {
                lat = node.ArgDouble("lat", 0);
                lon = node.ArgDouble("lon", 0);
            }

            if (bodyName == null)
            {
                throw new ArgumentException("missing 'body'");
            }

            InstrumentKind kind = RadarSteps.ParseInstrument(node);
            double max = node.ArgDouble("maxAltitude", double.PositiveInfinity);
            double? best = scenario.Coverage(bodyName, kind).BestAltitude(lat, lon);
            ctx.Detail = Invariant($"{bodyName} {kind} at lat {lat:F3} lon {lon:F3}: ")
                + (best.HasValue ? Invariant($"best altitude {best.Value:F0} m (max {max:F0})") : "not covered");
            return best.HasValue && best.Value <= max;
        }

        private static List<ModuleSubterraRadar> RadarModules(TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new InvalidOperationException("no tracked vessel");
            return vessel.parts.SelectMany(p => p.Modules.OfType<ModuleSubterraRadar>()).ToList();
        }

        // radarStatus status = Scanning count = 2 - every ModuleSubterraRadar on the tracked vessel (at least count of
        // them) shows the localized text of #Subterra_Radar_Status_<status> (Idle, Scanning, NoPower, Landed,
        // OutOfAltitude, NotLanded, TooFast) in its uiStatus. A wait before it lets a physics step refresh the field.
        private static bool RadarStatus(ConfigNode node, TestContext ctx)
        {
            string status = node.RequireArg("status");
            int count = (int)node.ArgDouble("count", 1);
            string expected = Localizer.Format("#Subterra_Radar_Status_" + status);
            List<ModuleSubterraRadar> modules = RadarModules(ctx);
            ctx.Detail = string.Join("; ", modules.Select(m => $"{m.part.name} [{m.mode}] scanning={m.scanning} \"{m.uiStatus}\" altitude {m.vessel.altitude:F0} m"))
                + $" (expected \"{expected}\", at least {count} module(s))";
            return modules.Count >= count && modules.All(m => m.uiStatus == expected);
        }

        // radarInstrument - BuildInstrument() of every radar module equals the Core factory of its mode (the cfg
        // defaults must be the factory values).
        private static bool RadarInstrument(ConfigNode node, TestContext ctx)
        {
            List<ModuleSubterraRadar> modules = RadarModules(ctx);
            var problems = new List<string>();
            foreach (ModuleSubterraRadar m in modules)
            {
                Instrument built = m.BuildInstrument();
                Instrument factory = m.mode == ModuleSubterraRadar.ModeOrbitalRadar ? Instrument.Eco()
                    : m.mode == ModuleSubterraRadar.ModeGravimeter ? Instrument.Plomada()
                    : Instrument.Zahori(m.highBand);
                // KSPFields are floats, the factory values doubles: compare to float precision.
                if (built.Kind != factory.Kind || !Near(built.FrequencyMHz, factory.FrequencyMHz) || !Near(built.DynamicRangeDb, factory.DynamicRangeDb)
                    || !Near(built.SensitivityMGal, factory.SensitivityMGal) || !Near(built.ConeHalfAngleDeg, factory.ConeHalfAngleDeg)
                    || !Near(built.AltitudeMin, factory.AltitudeMin) || !Near(built.AltitudeBest, factory.AltitudeBest) || !Near(built.AltitudeMax, factory.AltitudeMax))
                {
                    problems.Add(Invariant($"{m.part.name}: built {built.Kind} {built.FrequencyMHz} MHz {built.DynamicRangeDb} dB {built.SensitivityMGal} mGal alt {built.AltitudeMin}/{built.AltitudeBest}/{built.AltitudeMax} differs from the factory"));
                }
            }

            ctx.Detail = problems.Count == 0 ? $"{modules.Count} module(s) match their factory instrument" : string.Join("; ", problems);
            return modules.Count > 0 && problems.Count == 0;
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-6 * Math.Max(1, Math.Abs(b));

        // detectionLevel id = MN-test level = Hint - the radar record of the network has exactly that level
        // (None = no record). id = nearest takes the record closest to the active vessel (or to lat/lon when there is
        // no vessel), among the records of body when given.
        private static bool DetectionLevelIs(ConfigNode node, TestContext ctx)
        {
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null)
            {
                ctx.Detail = "no RadarScenario";
                return false;
            }

            string levelText = node.RequireArg("level");
            if (!Enum.TryParse(levelText, true, out DetectionLevel expected) || !Enum.IsDefined(typeof(DetectionLevel), expected))
            {
                throw new ArgumentException("unknown level " + levelText);
            }

            string id = node.Arg("id", "nearest");
            NetworkDetection found;
            if (id == "nearest")
            {
                string bodyName = node.Arg("body");
                Vessel v = FlightGlobals.ActiveVessel;
                double lat = v != null ? v.latitude : node.ArgDouble("lat", 0);
                double lon = v != null ? v.longitude : node.ArgDouble("lon", 0);
                found = scenario.Detections.All
                    .Where(d => bodyName == null || d.Body == bodyName)
                    .OrderBy(d => AngularDistance(lat, lon, d.Lat, d.Lon))
                    .FirstOrDefault();
            }
            else
            {
                found = scenario.Detections.Get(id);
            }

            DetectionLevel actual = found?.Level ?? DetectionLevel.None;
            ctx.Detail = found == null
                ? Invariant($"no record for {id}")
                : Invariant($"{found.NetworkId}: {actual} (expected {expected}), uncertainty {found.Uncertainty:F0} m, depth {found.DepthMin:F0}-{found.DepthMax:F0} m");
            return actual == expected;
        }

        // Great-circle angle in radians between two points given in degrees.
        private static double AngularDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double Rad = Math.PI / 180;
            double a = Math.Sin(lat1 * Rad) * Math.Sin(lat2 * Rad)
                + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Cos((lon1 - lon2) * Rad);
            return Math.Acos(Math.Max(-1, Math.Min(1, a)));
        }
    }
}
