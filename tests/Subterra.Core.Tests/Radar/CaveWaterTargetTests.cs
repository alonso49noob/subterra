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
    public class CaveWaterTargetTests
    {
        private static readonly FlatSampler Flat = new FlatSampler(R);

        private static Strata TwoLayers() => new Strata(new List<LayerSpec>
        {
            new LayerSpec { Name = "Regolith", Top = 0, Bottom = 30, Density = 1.6 },
            new LayerSpec { Name = "Bedrock", Top = 30, Bottom = 5000, Density = 3.0 },
        });

        private static CaveWaterTargetSource Source(NetworkCatalog catalog = null) =>
            new CaveWaterTargetSource(catalog ?? new NetworkCatalog(new BodySpec { Body = "Mun" }, R), TwoLayers(), Flat);

        // Three nodes 300 m apart, water 6 m deep in the first and the last.
        private static TubeNetwork Wet(string name = "W")
        {
            var site = Line(name, 0, 10, 300, 3, tweak: (i, n) =>
            {
                n.Depth = 50;
                n.Width = 30;
                n.Height = 20;
                n.Water = i == 1 ? 0 : 6;
                return n;
            });
            return NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
        }

        [Fact]
        public void OneTargetPerWaterLevel()
        {
            TubeNetwork net = Wet();
            Assert.Equal(2, net.Waters.Count);
            IReadOnlyList<SubsurfaceTarget> targets = Source().TargetsOf(net);
            Assert.Equal(2, targets.Count);
            Assert.All(targets, t => Assert.Equal(TargetKind.Water, t.Kind));
        }

        [Fact]
        public void DryNetworkHasNone()
        {
            var site = Line("D", 0, 10, 300, 3, tweak: (i, n) => { n.Depth = 50; return n; });
            TubeNetwork net = NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
            Assert.Empty(Source().TargetsOf(net));
        }

        [Fact]
        public void IdsAreStableAndAttachToTheNetwork()
        {
            var src = Source();
            var first = src.TargetsOf(Wet()).Select(t => t.Id).ToList();
            var second = src.TargetsOf(Wet()).Select(t => t.Id).ToList();
            Assert.Equal(first, second);
            Assert.Equal(new[] { "water:site:W#0", "water:site:W#1" }, first);
            Assert.All(src.TargetsOf(Wet()), t =>
            {
                Assert.Equal("site:W", t.NetworkId);
                Assert.Equal(-1, t.PrimitiveIndex);
            });
        }

        [Fact]
        public void SizeIsDiscByDepthAndDensityIsWater()
        {
            TubeNetwork net = Wet();
            foreach (SubsurfaceTarget t in Source().TargetsOf(net))
            {
                // WaterSurface.Radius is 1.5 times the node width.
                Assert.Equal(90, t.Width, 3);
                Assert.Equal(90, t.Length, 3);
                Assert.Equal(6, t.Height, 3);
                Assert.Equal(1.0, t.DensityContrast, 6);
            }
        }

        [Fact]
        public void DepthIsFromTheTerrainToTheWaterSurface()
        {
            // Roof 50 m under the terrain, 20 m high, water 6 m over the floor: the surface is 50 + 20 - 6 = 64 m down.
            TubeNetwork net = Wet();
            foreach (SubsurfaceTarget t in Source().TargetsOf(net))
            {
                Assert.InRange(t.Depth, 63, 65);
                Assert.Equal("Bedrock", t.Host.Name);
            }
        }

        [Fact]
        public void PositionIsTheCentreOfTheWaterSlab()
        {
            TubeNetwork net = Wet();
            SubsurfaceTarget t = Source().TargetsOf(net)[0];
            // Floor 70 m under the terrain, the slab spans 6 m above it: its centre is 67 m down.
            Assert.InRange(t.Position.Length, R - 68, R - 66);
            Assert.InRange(t.Lat, -0.01, 0.01);
            Assert.InRange(t.Lon, 10 - 0.01, 10 + 0.01);
        }

        [Fact]
        public void TargetsNearFiltersByDistance()
        {
            var catalog = new NetworkCatalog(new BodySpec { Body = "Mun" }, R);
            TubeNetwork net = Wet();
            catalog.AddNamed(net);
            var src = Source(catalog);
            Vec3d at = BodyFrame.Direction(0, 10) * R;
            Assert.Single(src.TargetsNear(at, 200));
            Assert.Equal(2, src.TargetsNear(at, 100000).Count());
            Assert.Empty(src.TargetsNear(at * -1, 1000));
        }

        [Fact]
        public void RegistersNextToNetworkTargets()
        {
            var catalog = new NetworkCatalog(new BodySpec { Body = "Mun" }, R);
            catalog.AddNamed(Wet());
            var registry = new TargetRegistry();
            registry.Register(new NetworkTargetSource(catalog, TwoLayers(), Flat));
            registry.Register(new CaveWaterTargetSource(catalog, TwoLayers(), Flat));
            Vec3d at = BodyFrame.Direction(0, 10) * R;
            Assert.Equal(2, registry.Near(at, 100000).Count(t => t.Kind == TargetKind.Water));
            Assert.True(registry.Near(at, 100000).Count(t => t.Kind == TargetKind.Void) > 2);
        }
    }
}
