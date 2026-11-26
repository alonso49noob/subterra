using System;
using System.Linq;
using Subterra.Core.Meshing;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class RockTexturesTests
    {
        private const int Size = 128;

        public static TheoryData<RockKind> Kinds()
        {
            var data = new TheoryData<RockKind>();
            foreach (RockKind kind in Enum.GetValues(typeof(RockKind)))
            {
                data.Add(kind);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Kinds))]
        public void EveryKindIsInRangeAndDeterministic(RockKind kind)
        {
            RockTexture a = RockTextures.Generate(kind, Size, 42), b = RockTextures.Generate(kind, Size, 42);
            Assert.Equal(a.R, b.R);
            Assert.Equal(a.Height, b.Height);
            foreach (float[] channel in new[] { a.R, a.G, a.B, a.Height })
            {
                Assert.All(channel, v => Assert.InRange(v, 0f, 1f));
            }
        }

        [Theory]
        [MemberData(nameof(Kinds))]
        public void EveryKindTilesSeamlessly(RockKind kind)
        {
            RockTexture t = RockTextures.Generate(kind, Size, 7);
            // Across both wraps the step is no bigger than between ordinary neighbours (a seam would be much bigger).
            double edge = 0, inner = 0;
            for (int i = 0; i < Size; i++)
            {
                edge += Step(t, Size - 1, i, 0, i) + Step(t, i, Size - 1, i, 0);
                inner += Step(t, Size / 2 - 1, i, Size / 2, i) + Step(t, i, Size / 2 - 1, i, Size / 2);
            }

            Assert.True(edge <= inner * 2.5 + 0.05, $"{kind}: edge {edge:F3} vs inner {inner:F3}");
        }

        [Theory]
        [MemberData(nameof(Kinds))]
        public void EveryKindHasAPattern(RockKind kind)
        {
            // Not a flat colour: the brightness varies across the tile.
            RockTexture t = RockTextures.Generate(kind, Size, 3);
            float[] l = Enumerable.Range(0, Size * Size).Select(i => (t.R[i] + t.G[i] + t.B[i]) / 3).ToArray();
            float mean = l.Average();
            double spread = Math.Sqrt(l.Select(v => (v - mean) * (v - mean)).Average());
            Assert.True(spread > 0.02, $"{kind}: spread {spread:F3}");
        }

        [Fact]
        public void KindsLookDifferent()
        {
            // Mean colours: basalt darker than anorthosite, ice bluer than sandstone, sandstone redder than ice.
            Mean(RockKind.Basalt, out float basalt, out _, out _);
            Mean(RockKind.Anorthosite, out float anorthosite, out _, out _);
            Mean(RockKind.Ice, out float iceR, out _, out float iceB);
            Mean(RockKind.Sandstone, out float sandR, out _, out float sandB);
            Assert.True(basalt < anorthosite);
            Assert.True(iceB - iceR > sandB - sandR);
        }

        [Theory]
        [InlineData("Regolith", RockKind.Regolith)]
        [InlineData("IceRegolith", RockKind.Ice)]
        [InlineData("Megaregolith", RockKind.Breccia)]
        [InlineData("AnorthositeBreccia", RockKind.Breccia)]
        [InlineData("IcyBreccia", RockKind.Breccia)]
        [InlineData("IcyCrust", RockKind.Ice)]
        [InlineData("MareBasalt", RockKind.Basalt)]
        [InlineData("VolcanicBasalt", RockKind.Basalt)]
        [InlineData("MagmaChamberRoof", RockKind.Basalt)]
        [InlineData("Limestone", RockKind.Limestone)]
        [InlineData("Sandstone", RockKind.Sandstone)]
        [InlineData("Granite", RockKind.Granite)]
        [InlineData("DeepCrust", RockKind.Granite)]
        [InlineData("LowerCrust", RockKind.Gabbro)]
        [InlineData("MiddleCrust", RockKind.Gneiss)]
        [InlineData("LithosphericMantle", RockKind.Olivine)]
        [InlineData("Asthenosphere", RockKind.Asthenosphere)]
        [InlineData("OuterCore", RockKind.MoltenMetal)]
        [InlineData("InnerCore", RockKind.Metal)]
        [InlineData("IronCore", RockKind.Metal)]
        [InlineData("HighPressureIce", RockKind.Ice)]
        [InlineData("Evaporites", RockKind.Salt)]
        [InlineData("PolarIce", RockKind.Ice)]
        [InlineData("UpperMantle", RockKind.Mantle)]
        [InlineData("RockyCore", RockKind.Core)]
        [InlineData("LayeredSediments", RockKind.Sediment)]
        [InlineData("Soil", RockKind.Sediment)]
        [InlineData("Something", RockKind.Generic)]
        [InlineData(null, RockKind.Generic)]
        public void KindIsGuessedFromTheLayerName(string name, RockKind expected)
        {
            Assert.Equal(expected, RockTextures.KindFor(name));
        }

        [Fact]
        public void AnExplicitTextureWins()
        {
            Assert.Equal(RockKind.Metal, RockTextures.KindFor("Mantle", "metal"));
            Assert.Equal(RockKind.Mantle, RockTextures.KindFor("Mantle", "no such kind"));
        }

        [Fact]
        public void NoIridescenceLeavesTheRockAsItIs()
        {
            RockTexture rock = RockTextures.Generate(RockKind.Mantle, 64, 2);
            RockTexture same = RockTextures.Iridescent(rock, 0, 5);
            Assert.Equal(rock.R, same.R);
            Assert.Equal(rock.B, same.B);
        }

        [Fact]
        public void IridescentRockShowsManyHues()
        {
            // The dull green mantle becomes banded with every hue: its colours spread far wider.
            RockTexture rock = RockTextures.Generate(RockKind.Mantle, 64, 2), film = RockTextures.Iridescent(rock, 1, 5);
            Assert.True(HueSpread(film) > HueSpread(rock) * 2, $"{HueSpread(film):F3} vs {HueSpread(rock):F3}");
            Assert.All(film.R, v => Assert.InRange(v, 0f, 1f));
        }

        [Fact]
        public void IridescenceGrowsWithHeatAndDepth()
        {
            Assert.InRange(RockTextures.Iridescence(290, 50, 600000), 0, 0.01f);
            Assert.True(RockTextures.Iridescence(900, 26862, 600000) > 0.2f);
            Assert.True(RockTextures.Iridescence(1100, 35000, 600000) > RockTextures.Iridescence(900, 26862, 600000));
            Assert.Equal(1, RockTextures.Iridescence(5000, 590000, 600000));
            // A cold small moon still shimmers deep down.
            Assert.True(RockTextures.Iridescence(300, 150000, 200000) > 0.7f);
        }

        // Spread of the colour away from grey: mean distance of each pixel's (r, g, b) from its own grey.
        private static double HueSpread(RockTexture t)
        {
            double sum = 0;
            for (int i = 0; i < t.R.Length; i++)
            {
                float l = (t.R[i] + t.G[i] + t.B[i]) / 3;
                sum += Math.Abs(t.R[i] - l) + Math.Abs(t.G[i] - l) + Math.Abs(t.B[i] - l);
            }

            return sum / t.R.Length;
        }

        private static double Step(RockTexture t, int x0, int y0, int x1, int y1)
        {
            int a = y0 * t.Size + x0, b = y1 * t.Size + x1;
            return Math.Abs(t.R[a] - t.R[b]) + Math.Abs(t.Height[a] - t.Height[b]);
        }

        private static void Mean(RockKind kind, out float r, out float g, out float b)
        {
            RockTexture t = RockTextures.Generate(kind, 64, 1);
            r = t.R.Average();
            g = t.G.Average();
            b = t.B.Average();
        }
    }
}
