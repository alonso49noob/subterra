using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Environment;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Procedural;
using Subterra.Core.Radar;
using Subterra.Core.Tubes;
using Xunit;
using static Subterra.Core.Tests.Sites;

namespace Subterra.Core.Tests.Radar
{
    public class NetworkTargetTests
    {
        private static readonly FlatSampler Flat = new FlatSampler(R);

        private static Strata TwoLayers(double deepDensity = 3.0) => new Strata(new List<LayerSpec>
        {
            new LayerSpec { Name = "Regolith", Top = 0, Bottom = 30, Density = 1.6 },
            new LayerSpec { Name = "Bedrock", Top = 30, Bottom = 5000, Density = deepDensity },
        });

        private static NetworkTargetSource Source(Strata strata = null) =>
            new NetworkTargetSource(new NetworkCatalog(new BodySpec { Body = "Mun" }, R), strata ?? TwoLayers(), Flat);

        private static TubeNetwork Tube(string name = "T", double roofDepth = 50, double height = 20)
        {
            var site = Line(name, 0, 10, 600, 3, tweak: (i, n) => { n.Depth = roofDepth; n.Width = 30; n.Height = height; return n; });
            return NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
        }

        [Fact]
        public void OneTargetPerPrimitive()
        {
            TubeNetwork net = Tube();
            Assert.True(net.Primitives.Count > 1);
            Assert.Equal(net.Primitives.Count, Source().TargetsOf(net).Count);
        }

        [Fact]
        public void IdsAreStable()
        {
            var src = Source();
            var first = src.TargetsOf(Tube()).Select(t => t.Id).ToList();
            var second = src.TargetsOf(Tube()).Select(t => t.Id).ToList();
            Assert.Equal(first, second);
            Assert.Equal(first.Count, first.Distinct().Count());
            Assert.Equal("site:T#0", first[0]);
        }

        [Fact]
        public void DepthIsRoofBelowSurface()
        {
            // Floor at 70 m, 20 m high: the roof is 50 m under the terrain.
            TubeNetwork net = Tube(roofDepth: 50, height: 20);
            foreach (SubsurfaceTarget t in Source().TargetsOf(net))
            {
                Assert.InRange(t.Depth, 49, 51);
                Assert.Equal(20, t.Height, 3);
                Assert.Equal(TargetKind.Void, t.Kind);
                Assert.Equal(net.Id, t.NetworkId);
            }
        }

        [Fact]
        public void PositionAndLatLonAreTheVoidCentre()
        {
            TubeNetwork net = Tube(roofDepth: 50, height: 20);
            SubsurfaceTarget t = Source().TargetsOf(net)[0];
            // Void centre: 70 m under the terrain at the floor, 10 m up from it.
            Assert.InRange(t.Position.Length, R - 61, R - 59);
            Assert.InRange(t.Lat, -0.01, 0.01);
            Assert.InRange(t.Lon, 10, 10 + Deg(600));
        }

        [Fact]
        public void GeodeIsSealed()
        {
            var site = new SiteSpec { Name = "G", IsNamed = true };
            site.Nodes.Add(Node(0, 10, depth: 100, width: 40, height: 30, feature: NodeFeature.Geode));
            TubeNetwork net = NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
            IReadOnlyList<SubsurfaceTarget> targets = Source().TargetsOf(net);
            SubsurfaceTarget geode = Assert.Single(targets);
            Assert.Equal(TargetKind.Sealed, geode.Kind);
            Assert.Equal(80, geode.Width, 3);
            Assert.Equal(30, geode.Height, 3);
            Assert.Equal(80, geode.Length, 3);
            Assert.InRange(geode.Depth, 99, 101);
        }

        [Fact]
        public void ShaftSizeIsDiameterByLength()
        {
            var site = new SiteSpec { Name = "S", IsNamed = true };
            site.Nodes.Add(Node(0, 10, depth: 100, width: 40, height: 30, feature: NodeFeature.Skylight, skylightRadius: 20));
            TubeNetwork net = NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
            SubsurfaceTarget shaft = Source().TargetsOf(net).Single(t => t.Length == 40 && t.Width == 40);
            Assert.True(shaft.Height > 50);
            Assert.Equal(0, shaft.Depth);
        }

        [Fact]
        public void ContrastIsHostDensity()
        {
            // Roof at 50 m is inside the 3.0 g/cm3 layer.
            foreach (SubsurfaceTarget t in Source(TwoLayers(3.0)).TargetsOf(Tube()))
            {
                Assert.Equal(3.0, t.DensityContrast, 6);
                Assert.Equal("Bedrock", t.Host.Name);
            }
        }

        [Fact]
        public void NoHostMeansDefaultDensity()
        {
            var src = Source(new Strata(new List<LayerSpec>()));
            SubsurfaceTarget t = src.TargetsOf(Tube())[0];
            Assert.Null(t.Host);
            Assert.Equal(2.5, t.DensityContrast, 6);
        }

        [Fact]
        public void TargetsNearFiltersByDistance()
        {
            var catalog = new NetworkCatalog(new BodySpec { Body = "Mun" }, R);
            TubeNetwork net = Tube();
            catalog.AddNamed(net);
            var src = new NetworkTargetSource(catalog, TwoLayers(), Flat);
            Vec3d at = BodyFrame.Direction(0, 10) * R;
            var all = src.TargetsOf(net);
            var near = src.TargetsNear(at, 300).ToList();
            Assert.NotEmpty(near);
            Assert.True(near.Count < all.Count);
            Assert.All(near, t => Assert.True(Vec3d.Distance(t.Position, at) <= 300));
            Assert.Equal(all.Count, src.TargetsNear(at, 100000).Count());
        }

        [Fact]
        public void RegistryConcatenatesSources()
        {
            var registry = new TargetRegistry();
            registry.Register(new FakeSource(2));
            registry.Register(new FakeSource(3));
            Assert.Equal(5, registry.Near(Vec3d.Zero, 10).Count());
        }

        [Fact]
        public void RegisterIgnoresNullAndDuplicates()
        {
            var registry = new TargetRegistry();
            var source = new FakeSource(2);
            registry.Register(null);
            registry.Register(source);
            registry.Register(source);
            Assert.Equal(2, registry.Near(Vec3d.Zero, 10).Count());
        }

        [Fact]
        public void RegisterDuringEnumerationKeepsTheOldSnapshot()
        {
            var registry = new TargetRegistry();
            registry.Register(new FakeSource(2));
            using (IEnumerator<SubsurfaceTarget> running = registry.Near(Vec3d.Zero, 10).GetEnumerator())
            {
                Assert.True(running.MoveNext());
                registry.Register(new FakeSource(3));
                int count = 1;
                while (running.MoveNext())
                {
                    count++;
                }

                Assert.Equal(2, count);
            }

            Assert.Equal(5, registry.Near(Vec3d.Zero, 10).Count());

            // A query started before the registration but not yet enumerated also sees the old sources.
            IEnumerable<SubsurfaceTarget> early = registry.Near(Vec3d.Zero, 10);
            registry.Register(new FakeSource(4));
            Assert.Equal(5, early.Count());
        }

        [Fact]
        public void TargetsOfIsCachedAndReadOnly()
        {
            var src = Source();
            TubeNetwork net = Tube();
            IReadOnlyList<SubsurfaceTarget> first = src.TargetsOf(net);
            Assert.Same(first, src.TargetsOf(net));
            Assert.False(first is List<SubsurfaceTarget>);
            Assert.Throws<System.NotSupportedException>(() => ((IList<SubsurfaceTarget>)first).Clear());
        }

        [Fact]
        public void ChamberCentreIsHalfAHeightAboveTheFloor()
        {
            var site = new SiteSpec { Name = "G", IsNamed = true };
            site.Nodes.Add(Node(0, 10, depth: 100, width: 40, height: 30, feature: NodeFeature.Geode));
            TubeNetwork net = NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
            SubsurfaceTarget t = Source().TargetsOf(net).Single();
            // Floor at 130 m, 30 m high: the void centre is 115 m under the terrain.
            Assert.InRange(t.Position.Length, R - 115.5, R - 114.5);
        }

        [Fact]
        public void ShaftCentreIsHalfALengthAboveItsBottom()
        {
            var site = new SiteSpec { Name = "S", IsNamed = true };
            site.Nodes.Add(Node(0, 10, depth: 100, width: 40, height: 30, feature: NodeFeature.Skylight, skylightRadius: 20));
            TubeNetwork net = NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
            int index = Enumerable.Range(0, net.Primitives.Count).Single(i => net.Primitives[i] is Subterra.Core.Sdf.Shaft);
            var shaft = (Subterra.Core.Sdf.Shaft)net.Primitives[index];
            SubsurfaceTarget t = Source().TargetsOf(net)[index];
            // Bottom is 28 m above the floor (130 m down), so 102 m under the terrain; the top is 20 m above it.
            double top = R + NetworkCompiler.ShaftAboveSurface;
            Assert.InRange(t.Position.Length, (R - 102 + top) / 2 - 0.5, (R - 102 + top) / 2 + 0.5);
            Assert.Equal(shaft.Length, t.Height, 3);
        }

        private sealed class FakeSource : ISubsurfaceTargetSource
        {
            private readonly int count;

            public FakeSource(int count) => this.count = count;

            public IEnumerable<SubsurfaceTarget> TargetsNear(Vec3d bodyPos, double radius)
            {
                for (int i = 0; i < count; i++)
                {
                    yield return new SubsurfaceTarget { Id = "fake" + i, PrimitiveIndex = -1 };
                }
            }
        }
    }
}
