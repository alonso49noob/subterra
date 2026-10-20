using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>
    /// The void of a whole network: smooth union of its primitives (blend k = factor × the narrower
    /// width) plus fBm wall noise of the nearest primitive. Thread-safe.
    /// </summary>
    public sealed class NetworkSdf : ISdf
    {
        /// <summary>Value returned where no primitive is near: solid rock.</summary>
        public const float Far = 1000f;

        private readonly IReadOnlyList<Primitive> prims;
        private readonly SpatialGrid grid;
        private readonly ulong noiseSeed;
        private readonly float smoothFactor;

        public NetworkSdf(IReadOnlyList<Primitive> prims, SpatialGrid grid, ulong noiseSeed, float smoothFactor = 0.15f)
        {
            this.prims = prims;
            this.grid = grid;
            this.noiseSeed = noiseSeed;
            this.smoothFactor = smoothFactor;
        }

        public IReadOnlyList<Primitive> Primitives => prims;

        public SpatialGrid Grid => grid;

        public float Distance(Vec3 p) => Evaluate(p, grid.At(p), null, true, out _);

        public float DistanceNoNoise(Vec3 p) => Evaluate(p, grid.At(p), null, false, out _);

        /// <summary>Index of the primitive with the smallest distance at <paramref name="p"/>, or -1 far from all.</summary>
        public int NearestPrimitive(Vec3 p)
        {
            Evaluate(p, grid.At(p), null, false, out int nearest);
            return nearest;
        }

        /// <summary>A view that only evaluates the primitives touching <paramref name="region"/> (+32 m): for meshing one chunk.</summary>
        public ISdf ForRegion(Aabb region)
        {
            var list = new List<int>();
            grid.Query(region.Expand(32f), list);
            return new RegionSdf(this, list.ToArray());
        }

        internal float Evaluate(Vec3 p, int[] indices, int[] subset, bool noise, out int nearest)
        {
            int[] source = subset ?? indices;
            nearest = -1;
            if (source.Length == 0)
            {
                return Far;
            }

            float result = Far;
            float resultWidth = 0;
            float best = float.MaxValue;
            bool first = true;
            foreach (int i in source)
            {
                Primitive prim = prims[i];
                // Same filter for the cell list and a region subset: both then evaluate the same
                // primitives in the same (ascending) order, so neighbouring chunks agree bit for bit.
                if (!prim.Bounds.Contains(p))
                {
                    continue;
                }

                float d = prim.Distance(p);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }

                if (first)
                {
                    result = d;
                    resultWidth = prim.Width;
                    first = false;
                    continue;
                }

                float k = smoothFactor * Math.Min(resultWidth, prim.Width);
                float before = result;
                result = SdfOps.SmoothMin(result, d, k);
                if (d < before)
                {
                    resultWidth = prim.Width;
                }
            }

            if (first)
            {
                return Far;
            }

            if (noise && nearest >= 0)
            {
                Primitive prim = prims[nearest];
                if (prim.Roughness > 0)
                {
                    result += prim.Roughness * Noise.Fbm(p / prim.NoiseScale, noiseSeed, 3);
                }
            }

            return result;
        }

        private sealed class RegionSdf : ISdf
        {
            private readonly NetworkSdf owner;
            private readonly int[] subset;

            public RegionSdf(NetworkSdf owner, int[] subset)
            {
                this.owner = owner;
                this.subset = subset;
            }

            public float Distance(Vec3 p) => owner.Evaluate(p, null, subset, true, out _);
        }
    }
}
