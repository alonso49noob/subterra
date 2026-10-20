using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Numerics
{
    public class VecTests
    {
        [Fact] public void CrossXY() => Assert.Equal(new Vec3(0, 0, 1), Vec3.Cross(new Vec3(1, 0, 0), new Vec3(0, 1, 0)));

        [Fact] public void NormalizedHasUnitLength() => Assert.Equal(1f, new Vec3(3, 4, 12).Normalized.Length, 5);

        [Fact] public void Vec3dRoundTrip() => Assert.Equal(new Vec3(1.5f, -2f, 3f), Vec3d.FromVec3(new Vec3(1.5f, -2f, 3f)).ToVec3());

        [Fact]
        public void AabbBasics()
        {
            var a = new Aabb(new Vec3(0, 0, 0), new Vec3(10, 10, 10));
            var b = new Aabb(new Vec3(5, 5, 5), new Vec3(20, 20, 20));
            var c = new Aabb(new Vec3(11, 0, 0), new Vec3(12, 1, 1));
            Assert.True(a.Intersects(b));
            Assert.False(a.Intersects(c));
            Assert.True(a.Contains(new Vec3(10, 0, 5)));
            Assert.False(a.Contains(new Vec3(10.1f, 0, 5)));
            Assert.Equal(new Vec3(5, 5, 5), a.Center);
            Assert.Equal(new Vec3(-1, -1, -1), a.Expand(1).Min);
            Assert.Equal(new Vec3(20, 20, 20), Aabb.Union(a, b).Max);
            Assert.Equal((float)System.Math.Sqrt(75), a.HalfDiagonal, 4);
            Assert.Equal(1f, a.DistanceTo(new Vec3(11, 5, 5)), 5);
            Assert.Equal(0f, a.DistanceTo(new Vec3(5, 5, 5)));
        }
    }

    public class HashTests
    {
        [Fact] public void HashEmptyIsFnvOffset() => Assert.Equal(14695981039346656037UL, Hash.Of(""));

        [Fact] public void HashDiffers() => Assert.NotEqual(Hash.Of("Mun"), Hash.Of("Minmus"));

        [Fact] public void TwoValueCombineMatchesParams() => Assert.Equal(Hash.Combine(new ulong[] { 7, 9 }), Hash.Combine(7UL, 9UL));

        [Fact] public void CombineIsOrderSensitive() => Assert.NotEqual(Hash.Combine(1, 2), Hash.Combine(2, 1));
    }

    public class RngTests
    {
        [Fact]
        public void RngSameSeedSameSequence()
        {
            var a = new Rng(42);
            var b = new Rng(42);
            for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void RngRangeBounds()
        {
            var r = new Rng(1);
            for (int i = 0; i < 10000; i++)
            {
                var v = r.Range(-3.0, 5.0);
                Assert.True(v >= -3.0 && v < 5.0, $"{v} outside [-3, 5)");
            }
        }

        [Fact]
        public void IntRangeCoversAllValues()
        {
            var r = new Rng(3);
            var seen = new bool[5];
            for (int i = 0; i < 1000; i++) seen[r.Range(0, 5)] = true;
            Assert.All(seen, Assert.True);
        }
    }

    public class NoiseTests
    {
        [Fact]
        public void FbmBoundedAndDeterministic()
        {
            var r = new Rng(7);
            for (int i = 0; i < 10000; i++)
            {
                var p = new Vec3((float)r.Range(-1e4, 1e4), (float)r.Range(-1e4, 1e4), (float)r.Range(-1e4, 1e4));
                var v = Noise.Fbm(p, 99, 4);
                Assert.InRange(v, -1f, 1f);
                Assert.Equal(v, Noise.Fbm(p, 99, 4));
            }
        }

        [Fact] public void NoiseSeedMatters() => Assert.NotEqual(Noise.Perlin3(new Vec3(1.3f, 2.7f, 0.4f), 1), Noise.Perlin3(new Vec3(1.3f, 2.7f, 0.4f), 2));

        [Fact] public void PerlinZeroAtLatticePoints() => Assert.Equal(0f, Noise.Perlin3(new Vec3(3, -7, 12), 5), 6);
    }
}
