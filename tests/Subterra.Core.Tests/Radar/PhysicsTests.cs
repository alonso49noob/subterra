using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class PhysicsTests
    {
        private static readonly LayerSpan[] Rock = { new LayerSpan(0, double.PositiveInfinity, new LayerSpec { Density = 2.6 }) };
        private static readonly LayerSpan[] Ice = { new LayerSpan(0, double.PositiveInfinity, new LayerSpec { Density = 1.0 }) };

        [Fact] public void EcoInRock() => Assert.Equal(375, RadarPhysics.MaxDepth(Rock, 20, 50), 0);

        [Fact] public void EcoInIce() => Assert.Equal(7500, RadarPhysics.MaxDepth(Ice, 20, 50), 0);

        [Fact] public void GprLowRock() => Assert.Equal(175, RadarPhysics.MaxDepth(Rock, 60, 70), 0);

        [Fact] public void GprHighRock() => Assert.Equal(21, RadarPhysics.MaxDepth(Rock, 500, 70), 0);

        [Fact]
        public void LossAcrossTwoLayers()
        {
            // 10 m of ice (0.01 dB/m) over rock (0.2 dB/m) at 60 MHz, depth 20: 2 * (0.1 + 2.0) = 4.2 dB
            var spans = new[]
            {
                new LayerSpan(0, 10, new LayerSpec { Density = 1.0 }),
                new LayerSpan(10, double.PositiveInfinity, new LayerSpec { Density = 2.6 }),
            };
            Assert.Equal(4.2, RadarPhysics.TwoWayLossDb(spans, 20, 60), 9);
        }

        [Fact]
        public void MaxDepthInterpolatesAcrossLayers()
        {
            var spans = new[]
            {
                new LayerSpan(0, 10, new LayerSpec { Density = 1.0 }),
                new LayerSpan(10, double.PositiveInfinity, new LayerSpec { Density = 2.6 }),
            };
            // The loss to 10 m is 0.2 dB; the remaining 0.8 dB at 0.4 dB/m is 2 m more.
            Assert.Equal(12, RadarPhysics.MaxDepth(spans, 60, 1.0), 9);
        }

        [Fact]
        public void UncoveredDepthUsesRockDefault()
        {
            var spans = new[] { new LayerSpan(0, 10, new LayerSpec { Density = 1.0 }) };
            // 10 m of ice: 0.2 dB, then the uncovered 10 m use the rock default (0.2 dB/m, two-way 4 dB).
            Assert.Equal(4.2, RadarPhysics.TwoWayLossDb(spans, 20, 60), 9);
            Assert.Equal(20, RadarPhysics.MaxDepth(spans, 60, 4.2), 9);
        }

        [Fact] public void VerticalRes60() => Assert.Equal(1.02, RadarPhysics.VerticalResolution(60, 6), 2);

        [Fact] public void Fresnel30km() => Assert.Equal(948.36, RadarPhysics.FresnelOrbital(20, 30000), 2);

        [Fact] public void FresnelGround() => Assert.Equal(10.1, RadarPhysics.FresnelGround(60, 6, 100), 1);

        [Fact] public void Cylinder() => Assert.Equal(0.393, GravityPhysics.CylinderMGal(3.0, 250, 20000), 3);

        [Fact] public void Sphere() => Assert.Equal(0.003276, GravityPhysics.SphereMGal(3.0, 250, 20000), 6);

        [Fact] public void ThumperReach() => Assert.Equal(50, SeismicPhysics.Reach(400, 0), 6);

        [Fact] public void SpreadLimits() => Assert.Equal(30, SeismicPhysics.Reach(60, 0), 6);

        [Fact] public void ChargeReach() => Assert.Equal(100, SeismicPhysics.Reach(1000, 8), 6);

        [Fact] public void ChargeCap() => Assert.Equal(1000, SeismicPhysics.Reach(5000, 1e6), 6);

        [Fact] public void Resolution() => Assert.Equal(15, SeismicPhysics.Resolution(30, 200), 6);

        [Fact] public void ResolutionDepthDominates() => Assert.Equal(20, SeismicPhysics.Resolution(30, 400), 6);
    }
}
