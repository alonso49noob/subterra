using System;
using Subterra.Core.Meshing;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class TextureGeneratorTests
    {
        private const int Size = 64;

        [Fact]
        public void HeightIsNormalizedAndDeterministic()
        {
            float[] a = TextureGenerator.Height(Size, 5), b = TextureGenerator.Height(Size, 5);
            Assert.Equal(a, b);
            Assert.All(a, v => Assert.InRange(v, 0f, 1f));
        }

        [Fact]
        public void TilesSeamlessly()
        {
            float[] h = TextureGenerator.Height(Size, 9);
            double edge = 0, inner = 0;
            for (int y = 0; y < Size; y++)
            {
                edge += Math.Abs(h[y * Size + Size - 1] - h[y * Size]);
                inner += Math.Abs(h[y * Size + Size / 2] - h[y * Size + Size / 2 - 1]);
            }

            // Across the wrap the step is no bigger than between ordinary neighbours (a seam would be).
            Assert.True(edge <= inner * 2.5 + 1e-3, $"edge {edge:F3} vs inner {inner:F3}");
        }

        [Fact]
        public void NormalsAreUnitAndFaceUp()
        {
            var n = TextureGenerator.Normals(TextureGenerator.Height(Size, 3), Size);
            Assert.All(n, v => { Assert.InRange(v.Length, 0.999f, 1.001f); Assert.True(v.Z > 0); });
        }
    }
}
