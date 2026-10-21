using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using Subterra.Core.Tubes;
using Xunit;
using static Subterra.Core.Tests.Sites;

namespace Subterra.Core.Tests.Procedural
{
    public class NetworkCatalogTests
    {
        private static readonly FlatSampler Flat = new FlatSampler(R);

        private static TubeNetwork Net(string name, double lon0)
        {
            var site = Line(name, 0, lon0, 1000, 3, tweak: (i, n) => { n.Depth = 30; n.Height = 40; return n; });
            return NetworkCompiler.Compile(site, "Mun", R, Flat, new List<string>());
        }

        private static BodySpec Spec(bool withField) =>
            new BodySpec { Body = "Mun", Fields = withField ? new List<FieldSpec> { new FieldSpec { Name = "F", Biomes = { "Lowlands" } } } : new List<FieldSpec>() };

        [Fact]
        public void FindContainingNamed()
        {
            var catalog = new NetworkCatalog(Spec(true), R);
            var net = Net("A", 10);
            catalog.AddNamed(net);
            Assert.Same(net, catalog.FindContaining(BodyFrame.Direction(0, 10 + Deg(500)) * (R - 70 + 5)));
            Assert.Null(catalog.FindContaining(BodyFrame.Direction(0, 10 + Deg(500)) * (R + 1)));
        }

        [Fact]
        public void MissingCellsShrinkAfterAddCell()
        {
            var catalog = new NetworkCatalog(Spec(true), R);
            var pos = BodyFrame.Direction(0, 10) * R;
            var missing = catalog.MissingCellsNear(pos, 1000).ToList();
            Assert.NotEmpty(missing);
            catalog.AddCell(missing[0], new List<TubeNetwork>());
            Assert.DoesNotContain(missing[0], catalog.MissingCellsNear(pos, 1000));
            Assert.True(catalog.IsCellReady(missing[0]));
        }

        [Fact]
        public void NetworksNearUsesDistance()
        {
            var catalog = new NetworkCatalog(Spec(true), R);
            var near = Net("Near", 10);
            var far = Net("Far", 40);
            catalog.AddCell(catalog.Grid.CellOf(BodyFrame.Direction(0, 10)), new List<TubeNetwork> { near });
            catalog.AddCell(catalog.Grid.CellOf(BodyFrame.Direction(0, 40)), new List<TubeNetwork> { far });
            var found = catalog.NetworksNear(BodyFrame.Direction(0, 10 - Deg(2000)) * R, 4000).ToList();
            Assert.Contains(near, found);
            Assert.DoesNotContain(far, found);
        }

        [Fact]
        public void ReplaceSwapsNamedCellAndDugNetworks()
        {
            var catalog = new NetworkCatalog(Spec(true), R);
            TubeNetwork named = Net("Named", 10), inCell = Net("InCell", 40), dug = Net("Dug", 70);
            catalog.AddNamed(named);
            catalog.AddCell(catalog.Grid.CellOf(BodyFrame.Direction(0, 40)), new List<TubeNetwork> { inCell });
            catalog.PutDug(dug);
            Assert.Equal(3, catalog.All.Count());

            foreach (TubeNetwork old in new[] { named, inCell, dug })
            {
                TubeNetwork grown = old.WithPrimitives(new List<Subterra.Core.Sdf.Primitive>(), new Subterra.Core.Numerics.Vec3d[0], new double[0]);
                Assert.True(catalog.Replace(grown));
                Assert.Same(grown, catalog.Find(old.Id));
            }

            Assert.Equal(3, catalog.All.Count());
            Assert.False(catalog.Replace(Net("Unknown", 100)));
        }

        [Fact]
        public void DugTunnelsCanJoinAndLeave()
        {
            var catalog = new NetworkCatalog(Spec(true), R);
            TubeNetwork dug = Net("Dug", 70);
            catalog.PutDug(dug);
            catalog.PutDug(dug);
            Assert.Single(catalog.All);
            Assert.True(catalog.RemoveDug(dug.Id));
            Assert.Empty(catalog.All);
            Assert.False(catalog.RemoveDug(dug.Id));
        }

        [Fact]
        public void NoFieldsNoMissingCells() =>
            Assert.Empty(new NetworkCatalog(Spec(false), R).MissingCellsNear(BodyFrame.Direction(0, 10) * R, 60000));
    }
}
