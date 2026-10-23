using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using Subterra.Core.Meshing;
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

namespace Subterra.Core.Tests.Water
{
    public class OceanTests
    {
        private const double VallRadius = 300000;

        private static OceanShell Vall() => new OceanShell(new OceanSpec { CeilingPole = 1000, CeilingEquator = 10000, Thickness = 30000 }, VallRadius);

        [Fact]
        public void IceIsThinnestAtThePoles()
        {
            OceanShell ocean = Vall();
            Assert.Equal(1000, ocean.CeilingDepth(90), 6);
            Assert.Equal(10000, ocean.CeilingDepth(0), 6);
            Assert.Equal(1000, ocean.CeilingDepth(-90), 6);
            Assert.InRange(ocean.CeilingDepth(45), 5400, 5600);
        }

        [Fact]
        public void UnderTheCeilingDownToTheFloor()
        {
            OceanShell ocean = Vall();
            Vec3d pole = new Vec3d(0, 1, 0);
            Assert.Null(ocean.At(pole * (VallRadius - 900)));
            WaterSample? deep = ocean.At(pole * (VallRadius - 1500));
            Assert.NotNull(deep);
            Assert.Equal(500, deep.Value.Depth, 3);
            Assert.Equal(1030, deep.Value.Density, 6);
            Assert.Null(ocean.At(pole * (VallRadius - 1000 - 30001)));
        }

        [Fact]
        public void OceanNodeParses()
        {
            var spec = Subterra.Core.Config.BodySpecParser.Parse(Subterra.Core.Config.CfgText.Parse(
                "SUBTERRA_BODY\n{\n body = Vall\n OCEAN\n {\n  ceilingPole = 1200\n  ceilingEquator = 9000\n  thickness = 25000\n  colour = 0.1, 0.2, 0.3\n }\n}")[0]).Value;
            Assert.NotNull(spec.Ocean);
            Assert.Equal(1200, spec.Ocean.CeilingPole, 6);
            Assert.Equal(9000, spec.Ocean.CeilingEquator, 6);
            Assert.Equal(25000, spec.Ocean.Thickness, 6);
            Assert.Equal(0.3f, spec.Ocean.Colour[2], 3);
        }
    }
}

namespace Subterra.Core.Tests.Water
{
    public class IceCeilingTests
    {
        [Fact]
        public void CeilingPatchHangsAtTheIceAndLooksDown()
        {
            var ocean = new OceanShell(new OceanSpec { CeilingPole = 1000, CeilingEquator = 10000, Thickness = 30000 }, 300000);
            Vec3d dir = Subterra.Core.Geo.BodyFrame.Direction(88, 0);
            MeshData m = IceCeilingMesher.Build(ocean, dir, 200, 10, 7, out Vec3d centre);
            Assert.Equal(41 * 41, m.Vertices.Count);
            Assert.InRange(centre.Length, 300000 - 1020, 300000 - 1000);
            foreach (Vec3 v in m.Vertices)
            {
                // Within the relief of the mean ceiling (and the sphere's curve over 200 m).
                double r = (centre + Vec3d.FromVec3(v)).Length;
                Assert.InRange(r, centre.Length - IceCeilingMesher.Relief - 5, centre.Length + IceCeilingMesher.Relief + 5);
            }

            Assert.True(Vec3.Dot(m.Normals[m.Normals.Count / 2], dir.ToVec3()) < -0.5f, "faces look down into the water");
        }
    }
}
