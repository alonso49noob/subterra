using System.Collections.Generic;
using System.Linq;
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
        public void AltitudeNodesKeepTheirLevelWhateverIsAbove()
        {
            var site = new SiteSpec { Name = "Levels", IsNamed = true };
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = Lon0, Depth = 50, Altitude = -300, Width = 60, Height = 30 });
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = Lon0 + Deg(1000), Depth = 50, Altitude = -500, Width = 60, Height = 30 });
            site.Edges.Add(new EdgeSpec { From = 0, To = 1, Sinuosity = 0 });
            TubeNetwork net = Compile(site);
            // Roof 300 m under the datum at the first node (flat terrain at the datum), 500 m at the second.
            Assert.True(net.IsInside(At(net, 0, Lon0, R - 300 - 15)));
            Assert.True(net.IsInside(At(net, 0, Lon0 + Deg(1000), R - 500 - 15)));
            Assert.False(net.IsInside(At(net, 0, Lon0, R - 60)));
        }

        [Fact]
        public void GeodeIsASealedCrystalRoom()
        {
            var site = new SiteSpec { Name = "Geode", IsNamed = true };
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = Lon0, Depth = 200, Width = 40, Height = 30, Feature = NodeFeature.Geode });
            TubeNetwork net = Compile(site);
            Vec3 inside = At(net, 0, Lon0, R - 200 - 30 + 10);
            Assert.True(net.IsInside(inside));
            Assert.Equal(ZoneKind.Geode, net.ZoneAt(inside).Kind);
            Assert.Empty(net.Skylights);
            Assert.True(net.Sdf.Distance(At(net, 0, Lon0, R - 100)) > 0, "rock between the geode and the surface");
        }

        [Fact]
        public void ShaftCutsSurface()
        {
            var top = Net.ToLocal(BodyFrame.Direction(0, Lon0) * (R + 1));
            Assert.True(Net.Sdf.Distance(top) < 0);
            var off = Net.ToLocal(BodyFrame.Direction(0, Lon0 + Deg(80)) * (R + 1));
            Assert.True(Net.Sdf.Distance(off) > 0);
        }

        [Fact]
        public void RampClimbsFromTheTubeToTheGround()
        {
            SiteSpec site = MakeSite("Ramped", 0, 120);
            site.Nodes[0].Ramp = true;
            TubeNetwork net = Compile(site);
            Assert.Equal(2, net.Skylights.Count);
            Skylight mouth = net.Skylights[1];
            Assert.True(mouth.Smooth && !mouth.Natural);

            var (lat, lon) = NetworkCompiler.RampTop(site, site.Nodes[0], R);
            Assert.InRange(System.Math.Abs(lon - Lon0) * 1000 / Deg(1000), 0, 1);
            // The run climbs 72 m at 18°: about 220 m out plus half the tube's width, across the tube (north or south).
            double out_ = System.Math.Abs(lat) * 1000 / Deg(1000);
            Assert.InRange(out_, 250, 300);

            // Open at the top, a passage all the way down, rock beside it.
            // Its floor ends 3 m over the ground, so the passage runs out into the open.
            Assert.True(net.Sdf.Distance(At(net, lat, lon, R + 5)) < 0, "ramp opens at the ground");
            for (double t = 0.15; t < 0.9; t += 0.15)
            {
                double la = lat * t, depth = 70 - 73 * t - 3;
                Assert.True(net.IsInside(At(net, la, Lon0, R - depth)), $"inside the ramp at {t:0.00}");
            }

            Assert.True(net.Sdf.Distance(At(net, lat * 0.5, Lon0 + Deg(60), R - 37)) > 0, "rock beside the ramp");
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
            // The opening reaches past the 30 m shaft: its rim is lobed, out to 1.3 radii.
            Assert.Equal(39, s.Radius, 3);
            Assert.True(s.Smooth && s.Natural);
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

namespace Subterra.Core.Tests.Tubes
{
    public class MeshClipTests
    {
        [Fact]
        public void ShaftTopIsClipped()
        {
            var site = new SiteSpec { Name = "Clip", IsNamed = true };
            site.Nodes.Add(Node(0, 10, depth: 30, width: 120, height: 40, feature: NodeFeature.Skylight, skylightRadius: 30));
            site.Nodes.Add(Node(0, 10 + Deg(500), depth: 30, width: 120, height: 40));
            site.Edges.Add(new EdgeSpec { From = 0, To = 1, Sinuosity = 0, Roughness = 0 });
            var net = NetworkCompiler.Compile(site, "Mun", R, new FlatSampler(R), new List<string>());
            // A chunk around the shaft top (terrain at R).
            var top = net.ToLocal(BodyFrame.Direction(0, 10) * R);
            var origin = top - new Vec3(32, 32, 32);
            var mesh = Subterra.Core.Meshing.SurfaceNets.Build(net.Sdf.ForRegion(new Aabb(origin, origin + new Vec3(64, 64, 64))), origin, 2f, false);
            Assert.Contains(Enumerable.Range(0, mesh.Vertices.Count), i => net.AboveSurface(mesh.Vertices[i]) > 5);
            var clipped = Subterra.Core.Meshing.MeshClip.DropAboveSurface(mesh, net);
            Assert.True(clipped.Triangles.Count < mesh.Triangles.Count);
            for (int i = 0; i < clipped.Triangles.Count; i++)
            {
                Assert.True(net.AboveSurface(clipped.Vertices[clipped.Triangles[i]]) < 2);
            }
        }
    }
}
