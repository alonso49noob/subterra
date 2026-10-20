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
        private readonly int[][] noiseTables;
        private readonly float smoothFactor;

        [ThreadStatic]
        private static List<int> QueryBuffer;

        public NetworkSdf(IReadOnlyList<Primitive> prims, SpatialGrid grid, ulong noiseSeed, float smoothFactor = 0.15f)
        {
            this.prims = prims;
            this.grid = grid;
            this.noiseSeed = noiseSeed;
            noiseTables = Noise.Tables(noiseSeed);
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

        /// <summary>
        /// Whether the cave surface may cross <paramref name="box"/>: the primitives whose bounds touch the box,
        /// evaluated at its centre without bounds clipping, come within half its diagonal plus twice
        /// their roughness (and a little margin). Conservative: a false "maybe" only costs an empty chunk.
        /// </summary>
        public bool MayHaveSurface(Aabb box)
        {
            List<int> list = QueryBuffer ?? (QueryBuffer = new List<int>());
            list.Clear();
            grid.Query(box, list);
            if (list.Count == 0)
            {
                return false;
            }

            Vec3 centre = box.Center;
            float reach = box.HalfDiagonal;
            float d = float.MaxValue;
            float roughness = 0;
            foreach (int i in list)
            {
                d = Math.Min(d, prims[i].Distance(centre));
                roughness = Math.Max(roughness, prims[i].Roughness);
            }

            return Math.Abs(d) <= reach + 2 * roughness + 2f;
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
            int resultGroup = -1;
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
                    resultGroup = prim.Group;
                    first = false;
                    continue;
                }

                bool sameGroup = prim.Group >= 0 && prim.Group == resultGroup;
                float k = sameGroup ? 0f : smoothFactor * Math.Min(resultWidth, prim.Width);
                float before = result;
                result = SdfOps.SmoothMin(result, d, k);
                if (d < before)
                {
                    resultWidth = prim.Width;
                    resultGroup = prim.Group;
                }
            }

            if (first)
            {
                return Far;
            }

            if (noise && nearest >= 0)
            {
                Primitive prim = prims[nearest];
                // Far from the surface the noise cannot change the sign: skip it (deterministic, so
                // neighbouring chunks still agree).
                if (prim.Roughness > 0 && Math.Abs(result) < prim.Roughness + 4f)
                {
                    result += prim.Roughness * prim.NoiseMask(p) * Noise.Fbm(p / prim.NoiseScale, noiseTables, 3);
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
