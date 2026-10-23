using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using Subterra.Core.Water;
using Xunit;
using static Subterra.Core.Tests.Sites;

namespace Subterra.Core.Tests.Water
{
    public class WaterTests
    {
        private const double KerbinG = 9.81;

        [Fact]
        public void TenMetresOfWaterAddAboutAnAtmosphere()
        {
            Assert.Equal(101.325 + 98.1, WaterPhysics.PressureKPa(10, WaterPhysics.FreshDensity, KerbinG, 101.325), 3);
            Assert.Equal(50, WaterPhysics.PressureKPa(-3, WaterPhysics.FreshDensity, KerbinG, 50), 6);
        }

        [Fact]
        public void SubmergedFractionRunsAcrossTheHeight()
        {
            Assert.Equal(0, WaterPhysics.SubmergedFraction(-2, 2), 6);
            Assert.Equal(0.5, WaterPhysics.SubmergedFraction(0, 2), 6);
            Assert.Equal(1, WaterPhysics.SubmergedFraction(1, 2), 6);
            Assert.Equal(0.75, WaterPhysics.SubmergedFraction(0.5, 2), 6);
        }

        [Fact]
        public void ACubicMetreOfWaterHoldsUpATonne()
        {
            Assert.Equal(1000 * KerbinG, WaterPhysics.Buoyancy(1, WaterPhysics.FreshDensity, KerbinG), 6);
            Assert.Equal(0.5 * 1000 * 4 * 2, WaterPhysics.Drag(2, 2, WaterPhysics.FreshDensity), 6);
        }

        // One room 100 m wide, 40 m high, floor 70 m down, with water 30 m deep reaching 80 m from its centre.
        private static TubeNetwork Pool()
        {
            var site = new SiteSpec { Name = "Pool", IsNamed = true };
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = 10, Depth = 30, Width = 100, Height = 40, Feature = NodeFeature.Chamber, Water = 30, WaterReach = 80 });
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = 10 + Deg(600), Depth = 30, Width = 30, Height = 20 });
            site.Edges.Add(new EdgeSpec { From = 0, To = 1, Sinuosity = 0 });
            return NetworkCompiler.Compile(site, "Kerbin", R, new FlatSampler(R), new List<string>());
        }

        private static Vec3 At(TubeNetwork net, double metresEast, double radius) => net.ToLocal(BodyFrame.Direction(0, 10 + Deg(metresEast)) * radius);

        [Fact]
        public void UnderTheLevelInsideTheRoom()
        {
            TubeNetwork net = Pool();
            // Floor at R - 70, level at R - 40.
            WaterSample? deep = net.WaterAt(At(net, 0, R - 60));
            Assert.NotNull(deep);
            Assert.InRange(deep.Value.Depth, 19.5, 20.5);
            Assert.True(deep.Value.Under);

            WaterSample? over = net.WaterAt(At(net, 10, R - 35));
            Assert.NotNull(over);
            Assert.False(over.Value.Under);
        }

        [Fact]
        public void NoWaterOutOfReachOrInTheRock()
        {
            TubeNetwork net = Pool();
            Assert.Null(net.WaterAt(At(net, 300, R - 60)));
            Assert.Null(net.WaterAt(At(net, 0, R - 15)));
            Assert.Null(net.WaterAt(At(net, 0, R - 200)));
        }
    }
}
