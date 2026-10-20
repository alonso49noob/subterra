using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>Assertions on cave props.</summary>
    [StepProvider]
    internal static class ScatterAssertions
    {
        internal static void Register()
        {
            StepRegistry.RegisterAssertion("scatterCount", ScatterCount);
            StepRegistry.RegisterAssertion("scatterColliderHit", ScatterColliderHit);
        }

        // scatterCount min = 1 — props placed on the visible chunks.
        private static bool ScatterCount(ConfigNode node, TestContext ctx)
        {
            int count = 0, chunks = 0;
            if (ChunkStreamer.Instance != null)
            {
                foreach (ChunkObject chunk in ChunkStreamer.Instance.Chunks)
                {
                    if (chunk.Visible)
                    {
                        chunks++;
                        count += chunk.ScatterInstances;
                    }
                }
            }

            ctx.Detail = $"{count} props on {chunks} visible chunks";
            return count >= (int)node.ArgDouble("min", 1);
        }

        // scatterColliderHit — a ray from 5 m above the nearest colliding prop straight down hits a prop collider.
        private static bool ScatterColliderHit(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = ctx.TrackedVessel;
            if (vessel == null || ChunkStreamer.Instance == null)
            {
                ctx.Detail = "no vessel or streamer";
                return false;
            }

            Vector3 here = vessel.GetWorldPos3D();
            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            foreach (ChunkObject chunk in ChunkStreamer.Instance.Chunks)
            {
                foreach (var (data, go) in chunk.Scatters)
                {
                    if (data.Collider == null || !go.activeInHierarchy)
                    {
                        continue;
                    }

                    foreach (var instance in data.Placed)
                    {
                        Vector3 world = go.transform.TransformPoint(instance.Position.ToUnity());
                        float d = (world - here).sqrMagnitude;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = world;
                        }
                    }
                }
            }

            if (bestDistance == float.MaxValue)
            {
                ctx.Detail = "no colliding prop shown";
                return false;
            }

            Vector3 up = (best - (Vector3)vessel.mainBody.position).normalized;
            bool hit = Physics.Raycast(best + up * 5f, -up, out RaycastHit h, 10f, 1 << 15);
            ctx.Detail = hit ? Invariant($"hit {h.collider.name} {h.distance:F2} m below the probe, prop {Mathf.Sqrt(bestDistance):F0} m from the vessel") : "no hit";
            return hit && h.collider.name.StartsWith("SubterraScatter");
        }
    }
}
