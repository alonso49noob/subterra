using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// The rock layers a chunk's walls cross: each triangle takes the layer at its own depth, so a shaft kilometres deep
    /// changes rock where the layer changes, not at a chunk's edge (a chunk used to take the layer at its centre).
    /// <para>
    /// Lucas: the change of rock should be subtler and interlocked, not a flat cut. The boundary is moved up and down by a
    /// noise of the place (the same in every chunk): tongues of each layer reach into the other. And for a few metres either
    /// side of it the walls take two mixes of both rocks (mostly the upper with some of the lower, then the reverse), so the
    /// rock goes over in steps, A, A with B, B with A, B. Both are scaled down in thin layers, which they must not swallow.
    /// </para>
    /// </summary>
    public static class LayerBands
    {
        /// <summary>Most the boundary moves up or down (m), and as a share of the layer's thickness.</summary>
        public const double MaxJitter = 12, JitterShare = 0.25;

        /// <summary>Height (m) of each of the two mixed bands either side of a boundary, and as a share of the layer's thickness.</summary>
        public const double MaxBand = 3, BandShare = 0.12;

        /// <summary>Wavelength (m) of the boundary's tongues.</summary>
        public const double JitterWavelength = 28;

        /// <summary>Share of the other rock in the band next to a boundary.</summary>
        public const float MinorShare = 0.3f;

        private static readonly ConcurrentDictionary<(LayerSpec, LayerSpec, bool), LayerSpec> Blends =
            new ConcurrentDictionary<(LayerSpec, LayerSpec, bool), LayerSpec>();

        /// <summary>
        /// The mix of <paramref name="upper"/> and <paramref name="lower"/> on the side of the boundary where
        /// <paramref name="mostlyLower"/> says (one object per pair and side, for every chunk and thread).
        /// </summary>
        public static LayerSpec Blend(LayerSpec upper, LayerSpec lower, bool mostlyLower) =>
            Blends.GetOrAdd((upper, lower, mostlyLower), k =>
            {
                LayerSpec main = k.Item3 ? k.Item2 : k.Item1;
                float share = k.Item3 ? 1 - MinorShare : MinorShare;
                float[] tu = k.Item1.Tint ?? new[] { 0.5f, 0.5f, 0.5f }, tl = k.Item2.Tint ?? new[] { 0.5f, 0.5f, 0.5f };
                return new LayerSpec
                {
                    Name = k.Item1.Name + ">" + k.Item2.Name + (k.Item3 ? "@lower" : "@upper"),
                    DisplayName = main.DisplayName,
                    Top = k.Item1.Bottom,
                    Bottom = k.Item1.Bottom,
                    Hardness = main.Hardness,
                    Density = main.Density,
                    Gradient = main.Gradient,
                    Tint = new[] { tu[0] + (tl[0] - tu[0]) * share, tu[1] + (tl[1] - tu[1]) * share, tu[2] + (tl[2] - tu[2]) * share },
                    Texture = main.Texture,
                    BlendUpper = k.Item1,
                    BlendLower = k.Item2,
                    BlendShare = share,
                };
            });

        /// <summary>The boundary's offset at a point (body frame, m), -1 to 1: smooth tongues a few tens of metres across.</summary>
        public static double Jitter(Vec3d bodyPos, ulong seed)
        {
            var p = new Vec3((float)(bodyPos.X / JitterWavelength), (float)(bodyPos.Y / JitterWavelength), (float)(bodyPos.Z / JitterWavelength));
            double n = Noise.Fbm(p, seed, 3) * 2.2;
            return n < -1 ? -1 : n > 1 ? 1 : n;
        }

        /// <summary>
        /// The triangles of <paramref name="mesh"/> grouped by layer (indices into its triangle list, three per triangle),
        /// in order of first appearance. Without <paramref name="jitter"/>: null when the whole mesh lies in one layer (the
        /// usual case: one material); with it, always the groups (even one), as a chunk's own layer may not be its centre's.
        /// </summary>
        /// <param name="depthOf">Depth (m, down from the surface) of a point of the mesh.</param>
        /// <param name="layerAt">The layer at a depth (null where there is none).</param>
        /// <param name="jitter">The boundary's offset at a point of the mesh, -1 to 1 (null: flat boundaries, no mixed bands).</param>
        public static List<(LayerSpec Layer, int[] Triangles)> Split(MeshData mesh, Func<Vec3, double> depthOf, Func<double, LayerSpec> layerAt,
            Func<Vec3, double> jitter = null)
        {
            List<int> tris = mesh.Triangles;
            int count = tris.Count / 3;
            if (count == 0)
            {
                return null;
            }

            var layerOf = new LayerSpec[count];
            LayerSpec first = null;
            bool several = false;
            for (int t = 0; t < count; t++)
            {
                Vec3 a = mesh.Vertices[tris[t * 3]], b = mesh.Vertices[tris[t * 3 + 1]], c = mesh.Vertices[tris[t * 3 + 2]];
                var centre = new Vec3((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3);
                double depth = Math.Max(0, depthOf(centre));
                LayerSpec layer = jitter == null ? layerAt(depth) : Interlocked(depth, jitter(centre), layerAt);
                layerOf[t] = layer;
                if (t == 0)
                {
                    first = layer;
                }
                else if (!ReferenceEquals(layer, first))
                {
                    several = true;
                }
            }

            if (!several && jitter == null)
            {
                return null;
            }

            var order = new List<LayerSpec>();
            var lists = new List<List<int>>();
            for (int t = 0; t < count; t++)
            {
                int k = IndexOf(order, layerOf[t]);
                if (k < 0)
                {
                    k = order.Count;
                    order.Add(layerOf[t]);
                    lists.Add(new List<int>());
                }

                lists[k].Add(tris[t * 3]);
                lists[k].Add(tris[t * 3 + 1]);
                lists[k].Add(tris[t * 3 + 2]);
            }

            var result = new List<(LayerSpec, int[])>(order.Count);
            for (int k = 0; k < order.Count; k++)
            {
                result.Add((order[k], lists[k].ToArray()));
            }

            return result;
        }

        /// <summary>
        /// The layer (or mix) at <paramref name="depth"/> with the boundary moved by <paramref name="offset"/> (-1 to 1, times
        /// the jitter allowed there), and within a band of a boundary the mix of the two layers on that side.
        /// </summary>
        public static LayerSpec Interlocked(double depth, double offset, Func<double, LayerSpec> layerAt)
        {
            LayerSpec here = layerAt(depth);
            if (here == null)
            {
                return null;
            }

            double d = Math.Max(0, depth + offset * Math.Min(MaxJitter, JitterShare * Thickness(here)));
            LayerSpec layer = layerAt(d);
            if (layer == null)
            {
                return here;
            }

            double band = Math.Min(MaxBand, BandShare * Thickness(layer));
            if (band <= 0.05)
            {
                return layer;
            }

            LayerSpec below = layerAt(d + band);
            if (below != null && !ReferenceEquals(below, layer))
            {
                return Blend(layer, below, false);
            }

            LayerSpec above = d - band > 0 ? layerAt(d - band) : null;
            if (above != null && !ReferenceEquals(above, layer))
            {
                return Blend(above, layer, true);
            }

            return layer;
        }

        private static double Thickness(LayerSpec layer) => Math.Max(0, layer.Bottom - layer.Top);

        private static int IndexOf(List<LayerSpec> order, LayerSpec layer)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (ReferenceEquals(order[i], layer))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
