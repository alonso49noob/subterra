using System.Collections;
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
            StepRegistry.Register("resetClock", ResetClock);
            StepRegistry.RegisterAssertion("cellReady", CellReady);
            StepRegistry.RegisterAssertion("proceduralNetworks", ProceduralNetworks);
        }

        // subterraFrameBudget avgMs = 2 maxMs = 8 seconds = 5 maxGcFrames = 5 — Subterra's main-thread ms per frame
        // over the last seconds. The max leaves out frames where a garbage collection ran inside Subterra's work
        // (the collection stops the whole game and is charged to whoever runs); those are counted instead.
        private static bool FrameBudget(ConfigNode node, TestContext ctx)
        {
            double avgLimit = node.ArgDouble("avgMs", 2), maxLimit = node.ArgDouble("maxMs", 8);
            int gcLimit = (int)node.ArgDouble("maxGcFrames", 5);
            var (avg, max, frames, maxWithoutGc, gcFrames) = SubterraClock.DetailedStats((float)node.ArgDouble("seconds", 5));
            ctx.Detail = Invariant($"avg {avg:F2} ms, max {maxWithoutGc:F2} ms without GC ({max:F2} ms with), {gcFrames} GC frames, over {frames} frames (limits {avgLimit}/{maxLimit}/{gcLimit}); worst/total ms: {SubterraClock.Breakdown()}");
            return frames > 0 && avg <= avgLimit && maxWithoutGc <= maxLimit && gcFrames <= gcLimit;
        }

        // resetClock — forget each component's worst frame, so the next budget check names what ran since.
        private static IEnumerator ResetClock(ConfigNode node, TestContext ctx)
        {
            SubterraClock.ResetComponents();
            ctx.Pass("component times reset");
            yield break;
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
