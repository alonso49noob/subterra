using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>
    /// The void of a whole network: smooth union of its primitives (blend k = factor × the narrower
    /// width) plus fBm wall noise of the nearest primitive. Thread-safe.
    /// </summary>
    public sealed class NetworkSdf : IBoundedSdf
    {
        /// <summary>
        /// Value returned where no primitive's bounds reach: solid rock. Outside every bounds box the
        /// void is at least <see cref="Primitive.BoundsMargin"/> away, so this is a true lower bound
        /// (vertex interpolation at coarse LODs stays right) and never a placeholder.
        /// </summary>
        public const float Far = Primitive.BoundsMargin;

        /// <summary>Margin around a chunk's region within which <see cref="ForRegion"/> keeps primitives.</summary>
        private const float RegionMargin = 32f;

        private readonly IReadOnlyList<Primitive> prims;
        private readonly SpatialGrid grid;
        private readonly ulong noiseSeed;
        private readonly int[][] noiseTables;
        private readonly float smoothFactor;
        private readonly float maxWidth;

        [ThreadStatic]
        private static List<int> QueryBuffer;

        public NetworkSdf(IReadOnlyList<Primitive> prims, SpatialGrid grid, ulong noiseSeed, float smoothFactor = 0.15f)
        {
            this.prims = prims;
            this.grid = grid;
            this.noiseSeed = noiseSeed;
            noiseTables = Noise.Tables(noiseSeed);
            this.smoothFactor = smoothFactor;
            foreach (Primitive prim in prims)
            {
                maxWidth = Math.Max(maxWidth, prim.Width);
            }
        }

        public IReadOnlyList<Primitive> Primitives => prims;

        public SpatialGrid Grid => grid;

        public float Distance(Vec3 p) => Evaluate(p, grid.At(p), true, false, out _, out _);

        public float DistanceNoNoise(Vec3 p) => Evaluate(p, grid.At(p), false, false, out _, out _);

        /// <summary>Index of the primitive with the smallest distance at <paramref name="p"/>, or -1 far from all.</summary>
        public int NearestPrimitive(Vec3 p)
        {
            Evaluate(p, grid.At(p), false, false, out int nearest, out _);
            return nearest;
        }

        /// <summary>A view that only evaluates the primitives touching <paramref name="region"/> (+32 m): for meshing one chunk.</summary>
        public ISdf ForRegion(Aabb region)
        {
            var list = new List<int>();
            Aabb kept = region.Expand(RegionMargin);
            grid.Query(kept, list);
            return new RegionSdf(this, list.ToArray(), kept);
        }

        // The primitives missing from p's grid cell list do not overlap that cell.
        float IBoundedSdf.SignBound(Vec3 p) => SignBound(p, grid.At(p), InsideDistance(grid.CellBox(p), p));

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

        // See IBoundedSdf. `unlisted` is a lower bound of the distance from p to the bounds of every
        // primitive missing from `source`.
        private float SignBound(Vec3 p, int[] source, float unlisted)
        {
            float value = Evaluate(p, source, true, true, out int nearest, out float skipped);
            float reach = Math.Max(0f, Math.Min(skipped, unlisted + Primitive.BoundsMargin - BlendReach(maxWidth)));
            if (nearest < 0)
            {
                // Nothing evaluated: rock, and no void closer than the skipped primitives allow.
                return reach;
            }

            return value >= 0 ? Math.Min(value, reach) : value;
        }

        // How much the smooth union with a primitive of this width can lower the field (k / 4).
        private float BlendReach(float width) => 0.25f * smoothFactor * width;

        // Distance from p to the outside of box (0 when p is outside it).
        private static float InsideDistance(Aabb box, Vec3 p)
        {
            if (!box.Contains(p))
            {
                return 0f;
            }

            float d = Math.Min(Math.Min(p.X - box.Min.X, box.Max.X - p.X), Math.Min(p.Y - box.Min.Y, box.Max.Y - p.Y));
            return Math.Min(d, Math.Min(p.Z - box.Min.Z, box.Max.Z - p.Z));
        }

        // With `bound`, `skipped` is a lower bound of how far the primitives skipped by their bounds
        // stay from turning the field negative (their bounds distance plus the margin, minus the blend);
        // otherwise +infinity. It never changes the returned value.
        private float Evaluate(Vec3 p, int[] source, bool noise, bool bound, out int nearest, out float skipped)
        {
            nearest = -1;
            skipped = float.PositiveInfinity;
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
                    if (bound)
                    {
                        skipped = Math.Min(skipped, prim.Bounds.DistanceTo(p) + Primitive.BoundsMargin - BlendReach(prim.Width));
                    }

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

        private sealed class RegionSdf : IBoundedSdf
        {
            private readonly NetworkSdf owner;
            private readonly int[] subset;
            private readonly Aabb kept;

            public RegionSdf(NetworkSdf owner, int[] subset, Aabb kept)
            {
                this.owner = owner;
                this.subset = subset;
                this.kept = kept;
            }

            public float Distance(Vec3 p) => owner.Evaluate(p, subset, true, false, out _, out _);

            // The primitives left out of the subset do not touch the kept box.
            public float SignBound(Vec3 p) => owner.SignBound(p, subset, InsideDistance(kept, p));
        }
    }
}
