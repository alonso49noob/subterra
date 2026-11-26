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

        [Fact]
        public void TheRockGoesOverInSteps()
        {
            // Unmoved: basalt, mostly basalt with breccia, mostly breccia with basalt, breccia.
            Assert.Same(Basalt, At(1490));
            LayerSpec upperBand = At(1498.5), lowerBand = At(1501.5);
            Assert.True(upperBand.IsBlend && lowerBand.IsBlend);
            Assert.Same(Basalt, upperBand.BlendUpper);
            Assert.Same(Breccia, upperBand.BlendLower);
            Assert.True(upperBand.BlendShare < 0.5f && lowerBand.BlendShare > 0.5f);
            Assert.Same(Breccia, At(1510));
        }

        [Fact]
        public void TheBoundaryMovesUpAndDownByTheJitter()
        {
            // Pushed 12 m down, a point 8 m above the boundary is in the breccia; pulled up, 8 m below it is basalt.
            Assert.Same(Breccia, At(1492, 1));
            Assert.Same(Basalt, At(1508, -1));
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
