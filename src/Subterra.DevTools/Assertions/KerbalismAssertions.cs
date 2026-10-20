using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Kerbalism radiation checks (by reflection: only meaningful with the kerbalism profile).</summary>
    [StepProvider]
    internal static class KerbalismAssertions
    {
        private static readonly Dictionary<string, double> Samples = new Dictionary<string, double>();

        internal static void Register()
        {
            StepRegistry.RegisterAssertion("radiationSample", RadiationSample);
            StepRegistry.RegisterAssertion("radiationRatio", RadiationRatio);
        }

        private static double? Radiation(Vessel v)
        {
            MethodInfo method = AccessTools.Method(AccessTools.TypeByName("KERBALISM.API"), "Radiation", new[] { typeof(Vessel) });
            return method == null ? (double?)null : (double)method.Invoke(null, new object[] { v });
        }

        // radiationSample name = surf — stores Kerbalism's environment radiation at the tracked vessel.
        private static bool RadiationSample(ConfigNode node, TestContext ctx)
        {
            double? r = ctx.TrackedVessel == null ? null : Radiation(ctx.TrackedVessel);
            if (r == null)
            {
                ctx.Detail = "Kerbalism not loaded";
                return false;
            }

            Samples[node.RequireArg("name")] = r.Value;
            ctx.Detail = Invariant($"{r.Value * 3600:E3} rad/h");
            return true;
        }

        // radiationRatio ref = surf max = 0.1 — radiation now / at the sample is at most max.
        private static bool RadiationRatio(ConfigNode node, TestContext ctx)
        {
            double? r = ctx.TrackedVessel == null ? null : Radiation(ctx.TrackedVessel);
            if (r == null || !Samples.TryGetValue(node.RequireArg("ref"), out double before) || before <= 0)
            {
                ctx.Detail = "no Kerbalism or no sample";
                return false;
            }

            double ratio = r.Value / before;
            ctx.Detail = Invariant($"{r.Value * 3600:E3} vs {before * 3600:E3} rad/h: ratio {ratio:F4}, {UndergroundTracker.For(ctx.TrackedVessel)}");
            return ratio <= node.ArgDouble("max", 0.1);
        }
    }
}
