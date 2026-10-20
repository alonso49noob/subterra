using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using Xunit;
using static Subterra.Core.Tests.Sites;

namespace Subterra.Core.Tests.Tubes
{
    public class NetworkTests
    {
        private const double Lon0 = 10;
        private static readonly FlatSampler Flat = new FlatSampler(R);
        private static readonly TubeNetwork Net = Compile(MakeSite("SerenitySkylight", 0, 120));
        private static readonly TubeNetwork Narrow = Compile(MakeSite("Narrow", 0, 40));
        private static readonly TubeNetwork Polar = Compile(MakeSite("Polar", 80, 120));

        // 3 nodes 1 km apart, skylight (radius 30) at node 0; roof 30 m deep, 40 m high: floor 70 m under the terrain.
        private static SiteSpec MakeSite(string name, double lat, double width)
        {
            double spacing = Deg(1000) / System.Math.Cos(lat * System.Math.PI / 180);
            var site = new SiteSpec { Name = name, IsNamed = true };
            for (int i = 0; i < 3; i++)
            {
                site.Nodes.Add(Node(lat, Lon0 + spacing * i, depth: 30, width: width, height: 40,
                    feature: i == 0 ? NodeFeature.Skylight : NodeFeature.None, skylightRadius: i == 0 ? 15 + width / 8 : 0));
            }

            site.Edges.Add(new EdgeSpec { From = 0, To = 1, Sinuosity = 0, Roughness = 0.05 });
            site.Edges.Add(new EdgeSpec { From = 1, To = 2, Sinuosity = 0, Roughness = 0.05 });
            return site;
        }

        private static TubeNetwork Compile(SiteSpec site) => NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());

        private static double NodeLon(TubeNetwork net, int i, double lat) => Lon0 + Deg(1000) / System.Math.Cos(lat * System.Math.PI / 180) * i;

        private static Vec3 At(TubeNetwork net, double lat, double lon, double radius) => net.ToLocal(BodyFrame.Direction(lat, lon) * radius);

        private static Vec3 MidTube(TubeNetwork net, double lat = 0) => At(net, lat, (NodeLon(net, 1, lat) + NodeLon(net, 2, lat)) / 2, R - 70 + 10);

        private static Vec3 FloorPointAbove(double h) => At(Net, 0, (NodeLon(Net, 1, 0) + NodeLon(Net, 2, 0)) / 2, R - 70 + h);

        private static Vec3 ShaftMid() => At(Net, 0, Lon0, R - 15);

        private static Vec3 RockPoint() => At(Net, 0, NodeLon(Net, 1, 0), R - 120);

        private static Vec3 FloorUnderSkylight() => At(Net, 0, Lon0, R - 68);

        private static Vec3 FloorAt(double meters) => At(Net, 0, Lon0 + Deg(meters), R - 68);

        [Fact]
        public void ShaftCutsSurface()
        {
            var top = Net.ToLocal(BodyFrame.Direction(0, Lon0) * (R + 1));
            Assert.True(Net.Sdf.Distance(top) < 0);
            var off = Net.ToLocal(BodyFrame.Direction(0, Lon0 + Deg(80)) * (R + 1));
            Assert.True(Net.Sdf.Distance(off) > 0);
        }

        [Fact] public void FloorBelowTwoMeters() => Assert.InRange(Net.FloorBelow(FloorPointAbove(2)).Value, 1.5f, 2.5f);

        [Fact] public void NotInsideReturnsNull() => Assert.Null(Net.FloorBelow(RockPoint()));

        [Fact]
        public void Zones()
        {
            Assert.Equal(ZoneKind.SkylightShaft, Net.ZoneAt(ShaftMid()).Kind);
            Assert.Equal(ZoneKind.TubeGallery, Net.ZoneAt(MidTube(Net)).Kind);
            Assert.Equal(ZoneKind.TubeBranch, Narrow.ZoneAt(MidTube(Narrow)).Kind);
            Assert.Equal(ZoneKind.FrozenVault, Polar.ZoneAt(MidTube(Polar, 80)).Kind);
            Assert.Equal("SerenitySkylight", Net.ZoneAt(MidTube(Net)).SiteName);
        }

        [Fact] public void SkyVisibilityUnderSkylight() => Assert.True(Net.SkyVisibility(FloorUnderSkylight()) >= 0.9f);

        [Fact] public void SkyVisibilityFarIsZero() => Assert.Equal(0f, Net.SkyVisibility(FloorAt(2000)));

        [Fact] public void IdFormat() => Assert.Equal("site:SerenitySkylight", Net.Id);

        [Fact]
        public void SkylightGeometry()
        {
            Skylight s = Assert.Single(Net.Skylights);
            Assert.Equal(30, s.Radius);
            Assert.True(Vec3.Distance(At(Net, 0, Lon0, R), s.SurfaceCenter) < 0.01f);
        }

        [Fact]
        public void RequiredSamplesCoverCompile()
        {
            var site = MakeSite("Sampled", 0, 120);
            var ps = new PrecomputedSamples();
            foreach (var (la, lo) in NetworkCompiler.RequiredSamples(site, R))
            {
                ps.Add(la, lo, R, "Lowlands");
            }

            NetworkCompiler.Compile(site, "Mun", R, ps, new List<string>());
        }
    }
}
