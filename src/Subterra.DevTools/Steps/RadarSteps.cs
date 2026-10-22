using System;
using System.Collections;
using Subterra.Core.Radar;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Test-only steps for the subsurface radar.</summary>
    [StepProvider]
    internal static class RadarSteps
    {
        internal static void Register()
        {
            StepRegistry.Register("radarMark", RadarMark);
        }

        // radarMark body = Mun lat = 0 lon = 0 altitude = 30000 halfWidth = 2000 instrument = OrbitalRadar hint = MN-test#0
        // - marks the coverage grid as if a scan had passed there at that altitude and, if hint is given
        // (<networkId>#<primitiveIndex>), applies a Hint detection of a synthetic target of that network at lat/lon
        // (depth 50 m, uncertainty 1000 m).
        private static IEnumerator RadarMark(ConfigNode node, TestContext ctx)
        {
            RadarScenario scenario = RadarScenario.Instance ?? throw new InvalidOperationException("no RadarScenario (wrong scene?)");
            string bodyName = node.RequireArg("body");
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName) ?? throw new ArgumentException("unknown body " + bodyName);
            double lat = node.ArgDouble("lat", 0);
            double lon = node.ArgDouble("lon", 0);
            double altitude = node.ArgDouble("altitude", 30000);
            double halfWidth = node.ArgDouble("halfWidth", 2000);
            InstrumentKind kind = ParseInstrument(node);

            scenario.Coverage(bodyName, kind).MarkSwath(lat, lon, halfWidth, body.Radius, altitude);
            string detail = Invariant($"{bodyName} {kind} lat {lat:F3} lon {lon:F3} altitude {altitude:F0} m halfWidth {halfWidth:F0} m");

            string hint = node.Arg("hint");
            if (!string.IsNullOrEmpty(hint))
            {
                int hash = hint.LastIndexOf('#');
                if (hash <= 0 || !int.TryParse(hint.Substring(hash + 1), out int index) || index < 0)
                {
                    throw new ArgumentException("hint must be <networkId>#<primitiveIndex>: " + hint);
                }

                var target = new SubsurfaceTarget
                {
                    Id = hint,
                    Kind = TargetKind.Void,
                    NetworkId = hint.Substring(0, hash),
                    PrimitiveIndex = index,
                    Lat = lat,
                    Lon = lon,
                    Depth = 50,
                };
                scenario.Detections.Apply(bodyName, target, new Detection(DetectionLevel.Hint, 1000, 50, 50));
                detail += ", hint " + hint;
            }

            ctx.Pass(detail);
            yield break;
        }

        internal static InstrumentKind ParseInstrument(ConfigNode node)
        {
            string text = node.Arg("instrument", "OrbitalRadar");
            if (!Enum.TryParse(text, true, out InstrumentKind kind) || !Enum.IsDefined(typeof(InstrumentKind), kind))
            {
                throw new ArgumentException("unknown instrument " + text);
            }

            return kind;
        }
    }
}
