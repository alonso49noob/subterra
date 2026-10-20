using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using Xunit;
using static Subterra.Core.Tests.Sites;

namespace Subterra.Core.Tests.Procedural
{
    public class FieldGeneratorTests
    {
        private static readonly CellGrid Grid = new CellGrid(R);
        private static readonly CellId Cell = Grid.CellOf(BodyFrame.Direction(2, 20));

        // Ground falling 1 m every 100 m towards the east; Highlands north of latitude 4.
        private static double H(double lat, double lon) => R - lon * BodyFrame.Deg2Rad * R * Math.Cos(lat * BodyFrame.Deg2Rad) / 100;

        private static string Biome(double lat, double lon) => lat > 4 ? "Highlands" : "Lowlands";

        private static readonly FlatSampler Terrain = new FlatSampler(H, Biome);

        private static List<SiteSpec> Gen(double density = 40) =>
            FieldGenerator.Generate(new FieldSpec { Name = "MunMaria", Biomes = { "Lowlands" }, NetworksPer10k = density }, "Mun", "MN", Grid, Cell, Terrain, Terrain);

        private static double H(NodeSpec n) => H(n.Lat, n.Lon);

        private static string Ser(List<SiteSpec> sites)
        {
            var sb = new StringBuilder();
            foreach (var s in sites)
            {
                sb.Append(s.Name).Append(':');
                foreach (var n in s.Nodes)
                {
                    sb.Append($"{n.Lat:R},{n.Lon:R},{n.Width:R},{n.Depth:R},{n.Feature};");
                }

                foreach (var e in s.Edges)
                {
                    sb.Append($"{e.From}-{e.To};");
                }
            }

            return sb.ToString();
        }

        [Fact]
        public void GeneratesSomething() => Assert.NotEmpty(Gen());

        [Fact] public void Deterministic() => Assert.Equal(Ser(Gen()), Ser(Gen()));

        [Fact]
        public void TrunksGoDownhill()
        {
            foreach (var s in Gen())
            {
                // The trunk is the first chain of nodes (edges i-1 → i).
                int last = 0;
                while (last + 1 < s.Nodes.Count && s.Edges.Any(e => e.From == last && e.To == last + 1))
                {
                    last++;
                }

                Assert.True(H(s.Nodes[last]) < H(s.Nodes[0]), $"{s.Name}: {H(s.Nodes[last])} !< {H(s.Nodes[0])}");
            }
        }

        [Fact]
        public void RespectsBiomes()
        {
            var nodes = Gen().SelectMany(s => s.Nodes).ToList();
            Assert.True(nodes.Count(n => Biome(n.Lat, n.Lon) == "Lowlands") >= 0.95 * nodes.Count);
        }

        [Fact]
        public void WidthsInRangeAndMin20()
        {
            foreach (var n in Gen().SelectMany(s => s.Nodes))
            {
                Assert.InRange(n.Width, 20, 350 * 1.15);
            }
        }

        [Fact]
        public void SkylightSpacing()
        {
            int gaps = 0;
            foreach (var s in Gen())
            {
                double along = 0;
                double? lastSkylight = null;
                for (int i = 0; i < s.Nodes.Count && (i == 0 || s.Edges.Any(e => e.From == i - 1 && e.To == i)); i++)
                {
                    if (i > 0)
                    {
                        along += BodyFrame.SurfaceDistance(BodyFrame.Direction(s.Nodes[i - 1].Lat, s.Nodes[i - 1].Lon), BodyFrame.Direction(s.Nodes[i].Lat, s.Nodes[i].Lon), R);
                    }

                    if (s.Nodes[i].Feature == NodeFeature.Skylight)
                    {
                        if (lastSkylight.HasValue)
                        {
                            Assert.InRange(along - lastSkylight.Value, 2000 - 500, 6000 + 500);
                            gaps++;
                        }

                        lastSkylight = along;
                    }
                }
            }

            Assert.True(gaps > 0);
        }

        [Fact] public void IdFormat() { foreach (var s in Gen()) Assert.Matches(@"^MN-\d{4}$", s.Name); }

        [Fact] public void ZeroDensityNoNetworks() => Assert.Empty(Gen(density: 0));
    }
}
