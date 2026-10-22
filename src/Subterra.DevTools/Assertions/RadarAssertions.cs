using System;
using System.Collections.Generic;
using System.Globalization;
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
            StepRegistry.RegisterAssertion("radarLastScan", RadarLastScan);
        }

        internal static Vessel FindVessel(string name)
        {
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v != null && (string.Equals(v.vesselName, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(v.GetDisplayName(), name, StringComparison.OrdinalIgnoreCase)))
                {
                    return v;
                }
            }

            return null;
        }

        // radarLastScan vessel = Radar A maxLag = 300 - the named vessel (default: the active one) has at least one
        // radar instrument switched on, and every switched-on one has lastScanUT at most maxLag seconds of game time
        // behind now. Loaded vessels are read from their modules, unloaded ones from the saved module values (what a
        // save writes), so it shows the background scan kept up and stored its progress.
        private static bool RadarLastScan(ConfigNode node, TestContext ctx)
        {
            string name = node.Arg("vessel");
            Vessel v = name == null ? FlightGlobals.ActiveVessel : FindVessel(name);
            if (v == null)
            {
                ctx.Detail = "no vessel " + (name ?? "active");
                return false;
            }

            double maxLag = node.ArgDouble("maxLag", 300);
            double now = Planetarium.GetUniversalTime();
            var lags = new List<double>();
            if (v.loaded)
            {
                foreach (ModuleSubterraRadar m in v.parts.SelectMany(p => p.Modules.OfType<ModuleSubterraRadar>()).Where(m => m.scanning))
                {
                    lags.Add(now - m.lastScanUT);
                }
            }
            else
            {
                foreach (ProtoPartModuleSnapshot s in v.protoVessel.protoPartSnapshots.SelectMany(p => p.modules).Where(m => m.moduleName == OrbitalScanner.ModuleName))
                {
                    bool.TryParse(s.moduleValues.GetValue("scanning"), out bool scanning);
                    if (scanning)
                    {
                        double.TryParse(s.moduleValues.GetValue("lastScanUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out double last);
                        lags.Add(now - last);
                    }
                }
            }

            ctx.Detail = Invariant($"{v.GetDisplayName()} ({(v.loaded ? "loaded" : "unloaded")}, {v.mainBody.bodyName}, {v.situation}): {lags.Count} instrument(s) on, lag ")
                + string.Join(", ", lags.Select(l => Invariant($"{l:F1} s"))) + Invariant($" (max {maxLag:F0}) at UT {now:F0}");
            return lags.Count > 0 && lags.All(l => l >= 0 && l <= maxLag);
        }

        // coverageAt body = Mun lat = 0 lon = 0 maxAltitude = 31000 instrument = OrbitalRadar
        // coverageAt at = vessel vessel = Radar A / coverageAt at = a - the coverage grid has a scan at that point
        // made from at most maxAltitude metres (default: any). at = vessel takes the sub-point of the active vessel,
        // or of the named one (loaded or not: from its orbit now); at = <name> a point recorded by subPoint. body
        // defaults to that vessel's body (the active vessel's for a recorded point).
        private static bool CoverageAt(ConfigNode node, TestContext ctx)
        {
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null)
            {
                ctx.Detail = "no RadarScenario";
                return false;
            }

            string bodyName = node.Arg("body");
            string at = node.Arg("at");
            double lat, lon;
            if (at == "vessel")
            {
                string name = node.Arg("vessel");
                Vessel v = name == null ? FlightGlobals.ActiveVessel : FindVessel(name);
                if (v == null)
                {
                    ctx.Detail = name == null ? "no active vessel" : "no vessel named " + name;
                    return false;
                }

                if (v == FlightGlobals.ActiveVessel)
                {
                    lat = v.latitude;
                    lon = v.longitude;
                }
                else
                {
                    // KSP's own maths at the current UT (no body-rotation correction is needed at "now").
                    Vector3d radial = v.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime()).xzy;
                    lat = v.mainBody.GetLatitude(radial, true);
                    lon = v.mainBody.GetLongitude(radial, true);
                }

                bodyName = bodyName ?? v.mainBody.bodyName;
            }
            else if (at != null)
            {
                if (!ctx.Snapshots.TryGetValue(RadarSteps.PointPrefix + at, out Vector3d point))
                {
                    throw new ArgumentException("no point " + at + " (subPoint first)");
                }

                lat = point.x;
                lon = point.y;
                bodyName = bodyName ?? FlightGlobals.ActiveVessel?.mainBody.bodyName;
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
