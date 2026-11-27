using System;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Environment;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    // Lucas: subtler, interlocked changes of rock between layers (LayerBands with a jitter, and the mixed rock).
    public class LayerTransitionTests
    {
        private static readonly LayerSpec Regolith = new LayerSpec { Name = "Regolith", Top = 0, Bottom = 4, Tint = new[] { 0.5f, 0.5f, 0.5f } };
        private static readonly LayerSpec Basalt = new LayerSpec { Name = "MareBasalt", Top = 4, Bottom = 1500, Tint = new[] { 0.3f, 0.3f, 0.3f } };
        private static readonly LayerSpec Breccia = new LayerSpec { Name = "Megaregolith", Top = 1500, Bottom = 3000, Tint = new[] { 0.6f, 0.55f, 0.5f } };
        private static readonly Strata Column = new Strata(new List<LayerSpec> { Regolith, Basalt, Breccia });

        private static LayerSpec At(double depth, double offset = 0) => LayerBands.Interlocked(depth, offset, d => Column.LayerAt(d, null));

        // Lucas: strata for solid layers (a sharp contact with an edge), a gradient where a viscous layer meets another.
        private static readonly LayerSpec Mantle = new LayerSpec { Name = "Mantle", Top = 0, Bottom = 2000, Tint = new[] { 0.3f, 0.35f, 0.25f } };
        private static readonly LayerSpec OuterCore = new LayerSpec { Name = "OuterCore", Top = 2000, Bottom = 4000, Tint = new[] { 0.7f, 0.4f, 0.2f } };
        private static readonly Strata Deep = new Strata(new List<LayerSpec> { Mantle, OuterCore });

        private static LayerSpec DeepAt(double depth, double offset = 0) => LayerBands.Interlocked(depth, offset, d => Deep.LayerAt(d, null));

        [Fact]
        public void SolidContactsAreSharpWithADarkSeam()
        {
            Assert.Same(Basalt, At(1490));
            Assert.Same(Basalt, At(1498));
            LayerSpec seam = At(1499.6);
            Assert.True(seam.Seam && seam.IsBlend);
            Assert.Same(Basalt, seam.BlendUpper);
            Assert.Same(Breccia, seam.BlendLower);
            Assert.True(seam.Tint[0] < Basalt.Tint[0] && seam.Tint[0] < Breccia.Tint[0], "darker than both");
            Assert.Same(Breccia, At(1501));
        }

        [Fact]
        public void SolidContactsBarelyUndulate()
        {
            // Moved 3 m at most: 2 m from the contact a point can change side, 5 m from it never.
            Assert.Same(Breccia, At(1498, 1));
            Assert.Same(Basalt, At(1502, -1));
            Assert.Same(Basalt, At(1495, 1));
            Assert.Same(Breccia, At(1505, -1));
        }

        [Fact]
        public void ViscousContactsAreAGradient()
        {
            Assert.Same(Mantle, DeepAt(1970));
            LayerSpec upperBand = DeepAt(1990), lowerBand = DeepAt(2010);
            Assert.True(upperBand.IsBlend && lowerBand.IsBlend && !upperBand.Seam);
            Assert.True(upperBand.BlendShare < 0.5f && lowerBand.BlendShare > 0.5f);
            Assert.Same(OuterCore, DeepAt(2030));
            // Tongues up to 12 m: pushed down, 25 m above the contact is already in the gradient.
            Assert.True(DeepAt(1975, 1).IsBlend);
            Assert.Same(Mantle, DeepAt(1975, -1));
        }

        [Fact]
        public void AThinLayerIsNotSwallowed()
        {
            // The 4 m regolith moves its boundary 1 m at most (a quarter of it), its bands are under half a metre.
            Assert.Same(Regolith, At(1.5, 1));
            Assert.Same(Regolith, At(1.5, -1));
        }

        [Fact]
        public void OneMixPerPairAndSide()
        {
            Assert.Same(LayerBands.Blend(Basalt, Breccia, true), LayerBands.Blend(Basalt, Breccia, true));
            Assert.NotSame(LayerBands.Blend(Basalt, Breccia, true), LayerBands.Blend(Basalt, Breccia, false));
        }

        [Fact]
        public void TheJitterIsSmoothAndInRange()
        {
            double last = LayerBands.Jitter(new Vec3d(600000, 0, 0), 5);
            double spread = 0;
            for (int i = 1; i <= 400; i++)
            {
                double j = LayerBands.Jitter(new Vec3d(600000, i * 0.5, 0), 5);
                Assert.InRange(j, -1, 1);
                Assert.True(Math.Abs(j - last) < 0.3, "no jumps between points half a metre apart");
                spread = Math.Max(spread, Math.Abs(j));
                last = j;
            }

            Assert.True(spread > 0.3, "it moves the boundary");
        }

        [Fact]
        public void WithAJitterEveryChunkGetsItsBands()
        {
            var m = new MeshData();
            m.Vertices.Add(new Vec3(0, -100, 0));
            m.Vertices.Add(new Vec3(1, -100, 0));
            m.Vertices.Add(new Vec3(0, -101, 0));
            m.Triangles.AddRange(new[] { 0, 2, 1 });
            m.SkirtTriangleStart = 3;
            var bands = LayerBands.Split(m, p => -p.Y, d => Column.LayerAt(d, null), p => 0);
            Assert.Single(bands);
            Assert.Same(Basalt, bands[0].Layer);
        }

        [Fact]
        public void TheMasksCoverTheirSharesAndNest()
        {
            float[][] masks = RockTextures.BlendMasks(64, 9, 0.3f, 0.7f);
            double small = masks[0].Average(), big = masks[1].Average();
            Assert.InRange(small, 0.22, 0.38);
            Assert.InRange(big, 0.62, 0.78);
            Assert.True(masks[0].Zip(masks[1], (a, b) => a <= b + 1e-4f).All(x => x), "the small share's patches lie inside the big one's");
        }

        [Fact]
        public void TheMixKeepsEachRocksTintAndStaysUnderOne()
        {
            RockTexture a = RockTextures.Generate(RockKind.Basalt, 64, 1), b = RockTextures.Generate(RockKind.Breccia, 64, 1);
            float[] none = new float[64 * 64], all = Enumerable.Repeat(1f, 64 * 64).ToArray();
            RockTexture upper = RockTextures.Blend(a, b, none, Basalt.Tint, Breccia.Tint, 1, 1.8f, out float su);
            RockTexture lower = RockTextures.Blend(a, b, all, Basalt.Tint, Breccia.Tint, 1, 1.8f, out float sl);
            Assert.True(su >= 1 && sl >= 1);
            Assert.All(upper.R.Concat(lower.R), v => Assert.InRange(v, 0f, 1f));
            // Mask 0 is the upper rock at its layer's colour (mean ≈ its tint), mask 1 the lower's.
            Assert.InRange(upper.R.Average() * su, 0.27, 0.33);
            Assert.InRange(lower.R.Average() * sl, 0.54, 0.66);
        }
    }
}
