using System.Linq;
using Subterra.Core.Procedural;
using Subterra.Core.Tubes;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Assertions on Subterra's frame time and procedural generation.</summary>
    [StepProvider]
    internal static class BudgetAssertions
    {
        internal static void Register()
        {
            StepRegistry.RegisterAssertion("subterraFrameBudget", FrameBudget);
            StepRegistry.RegisterAssertion("cellReady", CellReady);
            StepRegistry.RegisterAssertion("proceduralNetworks", ProceduralNetworks);
        }

        // subterraFrameBudget avgMs = 2 maxMs = 8 seconds = 5 — Subterra's main-thread ms per frame over the last seconds.
        private static bool FrameBudget(ConfigNode node, TestContext ctx)
        {
            double avgLimit = node.ArgDouble("avgMs", 2), maxLimit = node.ArgDouble("maxMs", 8);
            var (avg, max, frames) = SubterraClock.Stats((float)node.ArgDouble("seconds", 5));
            ctx.Detail = Invariant($"avg {avg:F2} ms, max {max:F2} ms over {frames} frames (limits {avgLimit}/{maxLimit}); worst/total ms: {SubterraClock.Breakdown()}");
            return frames > 0 && avg <= avgLimit && max <= maxLimit;
        }

        // cellReady — the procedural cell under the tracked vessel has been generated.
        private static bool CellReady(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null || !BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                ctx.Detail = "no vessel or no Subterra body";
                return false;
            }

            NetworkCatalog catalog = runtime.Catalog;
            CellId cell = catalog.Grid.CellOf(Frames.ToBody(vessel.mainBody, vessel.GetWorldPos3D()).Normalized);
            ctx.Detail = $"{cell}: {(catalog.IsCellReady(cell) ? "ready" : "not ready")}, {catalog.ReadyCellCount} cells ready";
            return catalog.IsCellReady(cell);
        }

        // proceduralNetworks min = 1 radius = 60000 — procedural networks generated within radius of the vessel.
        private static bool ProceduralNetworks(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null || !BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                ctx.Detail = "no vessel or no Subterra body";
                return false;
            }

            int min = (int)node.ArgDouble("min", 1);
            var position = Frames.ToBody(vessel.mainBody, vessel.GetWorldPos3D());
            TubeNetwork[] near = runtime.Catalog.NetworksNear(position, node.ArgDouble("radius", 60000)).Where(n => !n.IsNamed).ToArray();
            ctx.Detail = $"{near.Length} procedural networks near: {string.Join(", ", near.Take(8).Select(n => n.Id))}";
            return near.Length >= min;
        }
    }
}
