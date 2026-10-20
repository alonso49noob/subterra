using System.Globalization;
using System.Text.RegularExpressions;

namespace Subterra.DevTools
{
    /// <summary>Assertions about the plugin as a whole (loading, patches).</summary>
    [StepProvider]
    internal static class PluginAssertions
    {
        private static readonly Regex LoaderLine = new Regex(@"Loader finished in (\d+) ms");

        internal static void Register()
        {
            StepRegistry.RegisterAssertion("loaderTime", LoaderTime);
            StepRegistry.RegisterAssertion("raycastDown", RaycastDown);
            StepRegistry.RegisterAssertion("capVisible", CapVisible);
        }

        // capVisible expect = true — the far cap of the skylight nearest to the tracked vessel is (not) shown.
        private static bool CapVisible(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            bool expect = node.ArgBool("expect", true);
            bool? shown = vessel == null || SkylightRenderer.Instance == null ? null : SkylightRenderer.Instance.CapVisibleNear(vessel.GetWorldPos3D());
            ctx.Detail = shown == null ? "no skylight tracked" : $"cap {(shown.Value ? "shown" : "hidden")}, {SkylightRenderer.Instance.RimCount} rims built";
            return shown == expect;
        }

        // raycastDown fromUp = 0 expectMin = 115 expectMax = 125 — a layer-15 ray straight down from fromUp m
        // above the tracked vessel first hits something at a distance in [expectMin, expectMax].
        private static bool RaycastDown(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null)
            {
                ctx.Detail = "no vessel";
                return false;
            }

            double min = node.ArgDouble("expectMin", 0), max = node.ArgDouble("expectMax", 1e9);
            UnityEngine.Vector3 up = (vessel.GetWorldPos3D() - vessel.mainBody.position).normalized;
            UnityEngine.Vector3 origin = vessel.GetWorldPos3D() + (Vector3d)(up * (float)node.ArgDouble("fromUp", 0));
            if (!UnityEngine.Physics.Raycast(origin, -up, out UnityEngine.RaycastHit hit, 2000f, 1 << 15, UnityEngine.QueryTriggerInteraction.Ignore))
            {
                ctx.Detail = "no hit within 2 km";
                return false;
            }

            ctx.Detail = System.FormattableString.Invariant($"hit {hit.collider.name} at {hit.distance:F1} m (expected {min}..{max})");
            return hit.distance >= min && hit.distance <= max;
        }

        // loaderTime maxMs = 1000 — the loader's "Loader finished in <ms> ms" line is within the limit.
        private static bool LoaderTime(ConfigNode node, TestContext ctx)
        {
            double max = node.ArgDouble("maxMs", 1000);
            var lines = LogCapture.FindAll(e => e.Message.StartsWith(Log.Prefix) && LoaderLine.IsMatch(e.Message));
            if (lines.Count == 0)
            {
                ctx.Detail = "no 'Loader finished' line";
                return false;
            }

            int ms = int.Parse(LoaderLine.Match(lines[0].Message).Groups[1].Value, CultureInfo.InvariantCulture);
            ctx.Detail = $"loader took {ms} ms (max {max})";
            return ms <= max;
        }
    }
}
