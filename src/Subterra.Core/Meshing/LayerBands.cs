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
    /// <para>
    /// Then (Lucas: a gradient, or strata with edges on the solid blocks): that wide, tongued gradient is kept where a viscous
    /// layer (asthenosphere, mantle, core) meets another; between two solid layers the contact is sharp, barely undulating,
    /// with a thin dark seam along it. [Claude, revisar]
    /// </para>
    /// </summary>
    public static class LayerBands
    {
        /// <summary>Most the boundary moves up or down (m), and as a share of the layer's thickness.</summary>
        public const double MaxJitter = 12, JitterShare = 0.25;

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

            // Lucas: the contacts still looked odd; two ways: a gradient, or strata (solid blocks with edges, as in layered
            // rock), except for the viscous layers (asthenosphere, mantle, core). Solid on both sides: a sharp contact, barely
            // undulating, with a thin dark seam along it. A viscous layer on either side: a wide gradient.
            bool viscous = ViscousContact(depth, here, layerAt);
            double reach = viscous ? Math.Min(MaxJitter, JitterShare * Thickness(here)) : Math.Min(StrataJitter, StrataShare * Thickness(here));
            double d = Math.Max(0, depth + offset * reach);
            LayerSpec layer = layerAt(d);
            if (layer == null)
            {
                return here;
            }

            if (!viscous)
            {
                double seam = Math.Min(SeamHeight, 0.05 * Thickness(layer));
                LayerSpec under = seam > 0.05 ? layerAt(d + seam) : null;
                return under != null && !ReferenceEquals(under, layer) && !IsViscous(under) ? Seam(layer, under) : layer;
            }

            double band = Math.Min(GradientBand, GradientShare * Thickness(layer));
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

        /// <summary>Most a solid contact undulates (m), and as a share of the layer's thickness.</summary>
        public const double StrataJitter = 3, StrataShare = 0.06;

        /// <summary>Height (m) of the dark seam along a solid contact.</summary>
        public const double SeamHeight = 0.8;

        /// <summary>Height (m) of each of the two mixed bands of a viscous contact (a gradient), and as a share of the thickness.</summary>
        public const double GradientBand = 18, GradientShare = 0.2;

        /// <summary>Layers that flow (Lucas: the asthenosphere, the mantle, the core): their contacts are gradients.</summary>
        public static bool IsViscous(LayerSpec layer)
        {
            if (layer == null)
            {
                return false;
            }

            RockKind kind = RockTextures.KindFor(layer.Name, layer.Texture);
            return kind == RockKind.Asthenosphere || kind == RockKind.Mantle || kind == RockKind.MoltenMetal || kind == RockKind.Core
                || kind == RockKind.Metal;
        }

        // Whether the contact nearest to the depth has a viscous layer on either side.
        private static bool ViscousContact(double depth, LayerSpec here, Func<double, LayerSpec> layerAt)
        {
            if (IsViscous(here))
            {
                return true;
            }

            bool nearTop = depth - here.Top < here.Bottom - depth;
            LayerSpec other = nearTop ? (here.Top > 0.05 ? layerAt(here.Top - 0.05) : null) : layerAt(here.Bottom + 0.05);
            return IsViscous(other);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(LayerSpec, LayerSpec), LayerSpec> Seams =
            new System.Collections.Concurrent.ConcurrentDictionary<(LayerSpec, LayerSpec), LayerSpec>();

        /// <summary>The dark seam between two solid layers (one object per pair).</summary>
        public static LayerSpec Seam(LayerSpec upper, LayerSpec lower) =>
            Seams.GetOrAdd((upper, lower), k =>
            {
                float[] tu = k.Item1.Tint ?? new[] { 0.5f, 0.5f, 0.5f }, tl = k.Item2.Tint ?? new[] { 0.5f, 0.5f, 0.5f };
                return new LayerSpec
                {
                    Name = k.Item1.Name + ">" + k.Item2.Name + "@seam",
                    DisplayName = k.Item1.DisplayName,
                    Top = k.Item1.Bottom,
                    Bottom = k.Item1.Bottom,
                    Hardness = k.Item1.Hardness,
                    Density = k.Item1.Density,
                    Gradient = k.Item1.Gradient,
                    Tint = new[] { (tu[0] + tl[0]) * 0.3f, (tu[1] + tl[1]) * 0.3f, (tu[2] + tl[2]) * 0.3f },
                    Texture = k.Item1.Texture,
                    BlendUpper = k.Item1,
                    BlendLower = k.Item2,
                    BlendShare = 0.5f,
                    Seam = true,
                };
            });

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
