using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Assertions on cave chunk streaming.</summary>
    [StepProvider]
    internal static class StreamAssertions
    {
        internal static void Register()
        {
            StepRegistry.RegisterAssertion("chunkCount", ChunkCount);
            StepRegistry.RegisterAssertion("colliderBelow", ColliderBelow);
        }

        // chunkCount min = 1 — visible cave chunks.
        private static bool ChunkCount(ConfigNode node, TestContext ctx)
        {
            ChunkStreamer streamer = ChunkStreamer.Instance;
            int min = (int)node.ArgDouble("min", 1);
            if (streamer == null)
            {
                ctx.Detail = "no streamer";
                return false;
            }

            ctx.Detail = $"{streamer.ActiveChunks} visible, {streamer.CachedChunks} cached ({streamer.CachedBytes / 1024} KiB), {streamer.PendingJobs} meshing";
            return streamer.ActiveChunks >= min;
        }

        // colliderBelow maxDist = 8 — a layer-15 raycast straight down from the tracked vessel hits a cave chunk within maxDist.
        private static bool ColliderBelow(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null)
            {
                ctx.Detail = "no vessel";
                return false;
            }

            float max = (float)node.ArgDouble("maxDist", 8);
            Vector3 position = vessel.GetWorldPos3D();
            Vector3 down = -(position - (Vector3)vessel.mainBody.position).normalized;
            if (!Physics.Raycast(position, down, out RaycastHit hit, 500f, 1 << 15, QueryTriggerInteraction.Ignore))
            {
                ctx.Detail = "nothing below within 500 m";
                return false;
            }

            bool cave = hit.collider.name.StartsWith("SubterraChunk");
            ctx.Detail = Invariant($"hit {hit.collider.name} at {hit.distance:F2} m");
            return cave && hit.distance <= max;
        }
    }
}
