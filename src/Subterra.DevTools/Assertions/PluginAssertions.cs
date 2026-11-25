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
            StepRegistry.RegisterAssertion("mouthRays", MouthRays);
            StepRegistry.RegisterAssertion("capVisible", CapVisible);
        }

        // capVisible expect = true — the far cap of the skylight nearest to the tracked vessel is (not) shown.
        private static bool CapVisible(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            bool expect = node.ArgBool("expect", true);
            bool? shown = vessel == null || SkylightRenderer.Instance == null ? null : SkylightRenderer.Instance.CapVisibleNear(vessel.GetWorldPos3D());
            ctx.Detail = shown == null ? "no skylight tracked" : $"cap {(shown.Value ? "shown" : "hidden")}, {SkylightRenderer.Instance.RimCount} rims built; nearest {SkylightRenderer.Instance.DescribeNearest(vessel.GetWorldPos3D())}";
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
                // missOk = true: nothing within 2 km counts as a pass (an open shaft whose floor has no collider yet).
                ctx.Detail = "no hit within 2 km";
                return node.ArgBool("missOk", false);
            }

            ctx.Detail = System.FormattableString.Invariant($"hit {hit.collider.name} at {hit.distance:F1} m (expected {min}..{max})");
            return hit.distance >= min && hit.distance <= max;
        }

        // mouthRays radius = 4 within = 40 — rays straight down from the tracked vessel and from points around it (every metre
        // out to radius, 8 directions): passes when none hits anything within that many metres (an open shaft mouth
        // under the vessel); the detail lists what each blocked ray hit.
        private static bool MouthRays(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel ?? throw new System.InvalidOperationException("no tracked vessel");
            float radius = (float)node.ArgDouble("radius", 4), within = (float)node.ArgDouble("within", 40);
            UnityEngine.Vector3 up = (vessel.GetWorldPos3D() - vessel.mainBody.position).normalized;
            UnityEngine.Vector3 east = UnityEngine.Vector3.Cross(up, UnityEngine.Vector3.up).sqrMagnitude > 1e-4f ? UnityEngine.Vector3.Cross(up, UnityEngine.Vector3.up).normalized : UnityEngine.Vector3.right;
            UnityEngine.Vector3 north = UnityEngine.Vector3.Cross(east, up);
            var blocked = new System.Collections.Generic.List<string>();
            int rays = 0;
            for (float r = 0; r <= radius + 0.01f; r += 1)
            {
                for (int k = 0; k < (r == 0 ? 1 : 8); k++)
                {
                    float a = k * UnityEngine.Mathf.PI / 4;
                    UnityEngine.Vector3 origin = (UnityEngine.Vector3)vessel.GetWorldPos3D() + (east * UnityEngine.Mathf.Cos(a) + north * UnityEngine.Mathf.Sin(a)) * r;
                    rays++;
                    foreach (UnityEngine.RaycastHit hit in UnityEngine.Physics.RaycastAll(origin, -up, within, (1 << 15) | 1, UnityEngine.QueryTriggerInteraction.Ignore))
                    {
                        if (hit.collider.GetComponentInParent<Part>() != null)
                        {
                            continue;
                        }

                        blocked.Add(System.FormattableString.Invariant($"r {r:F0} dir {k}: {hit.collider.name} at {hit.distance:F1} m"));
                    }
                }
            }

            ctx.Detail = blocked.Count == 0 ? $"{rays} rays, none blocked within {within} m" : $"{blocked.Count} hits of {rays} rays: " + string.Join("; ", blocked);
            return blocked.Count == 0;
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
