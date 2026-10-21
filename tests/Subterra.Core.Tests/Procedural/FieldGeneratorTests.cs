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

        [Fact]
        public void SeveralFieldsShareIdsAndCap()
        {
            var fields = new[]
            {
                new FieldSpec { Name = "MunMaria", Biomes = { "Lowlands" }, NetworksPer10k = 1000 },
                new FieldSpec { Name = "MunMariaWest", Biomes = { "Lowlands" }, NetworksPer10k = 1000, Seed = 7 },
            };
            List<SiteSpec> sites = FieldGenerator.Generate(fields, "Mun", "MN", Grid, Cell, Terrain, Terrain);
            Assert.NotEmpty(sites);
            Assert.True(sites.Count <= FieldGenerator.MaxNetworksPerCell, $"{sites.Count} networks in one cell");
            Assert.Equal(sites.Count, sites.Select(s => s.Name).Distinct().Count());
        }

        [Fact]
        public void SecondFieldGetsWhatTheFirstLeaves()
        {
            var sparse = new FieldSpec { Name = "MunMaria", Biomes = { "Lowlands" }, NetworksPer10k = 40 };
            var dense = new FieldSpec { Name = "MunMariaWest", Biomes = { "Lowlands" }, NetworksPer10k = 1000, Seed = 7 };
            List<SiteSpec> first = FieldGenerator.Generate(sparse, "Mun", "MN", Grid, Cell, Terrain, Terrain);
            List<SiteSpec> both = FieldGenerator.Generate(new[] { sparse, dense }, "Mun", "MN", Grid, Cell, Terrain, Terrain);
            // The first field is unchanged by the second; the second fills the rest of the cap.
            Assert.Equal(Ser(first), Ser(both.Take(first.Count).ToList()));
            Assert.True(both.Count > first.Count);
            Assert.Equal(both.Count, both.Select(s => s.Name).Distinct().Count());
        }

        // A FieldSpec built in code skips the parser's checks: the generator must still finish.
        [Theory]
        [InlineData(0, 0, 2000, 20000)]
        [InlineData(-3000, -10, 2000, 20000)]
        [InlineData(double.NaN, double.NaN, 2000, 20000)]
        [InlineData(2000, 6000, 2000, double.PositiveInfinity)]
        public async System.Threading.Tasks.Task BadFieldSpecStillTerminates(double spacingMin, double spacingMax, double lengthMin, double lengthMax)
        {
            var spec = new FieldSpec
            {
                Name = "MunMaria",
                Biomes = { "Lowlands" },
                NetworksPer10k = 40,
                SkylightSpacing = new Range(spacingMin, spacingMax),
                Length = new Range(lengthMin, lengthMax),
            };
            var run = System.Threading.Tasks.Task.Run(() => FieldGenerator.Generate(spec, "Mun", "MN", Grid, Cell, Terrain, Terrain));
            Assert.Same(run, await System.Threading.Tasks.Task.WhenAny(run, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(20))));
            Assert.NotEmpty(await run);
        }

        // Networks are the same in every save and for every player (Lucas's ruling: maps must stay valid),
        // so the generator's output is pinned. If this fails, a change moved existing networks: undo it, or,
        // if it is deliberate, note it in the CHANGELOG and update the fingerprint.
        private const string Fingerprint = "CB289CE3CD3E38BA";

        [Fact]
        public void OutputIsPinned()
        {
            var spec = new FieldSpec { Name = "MunMaria", Biomes = { "Lowlands" } };
            var text = new StringBuilder();
            var seen = new HashSet<CellId>();
            for (double lat = -80; lat <= 80; lat += 8)
            for (double lon = -180; lon < 180; lon += 8)
            {
                CellId cell = Grid.CellOf(BodyFrame.Direction(lat, lon));
                if (!seen.Add(cell))
                {
                    continue;
                }

                foreach (SiteSpec s in FieldGenerator.Generate(spec, "Mun", "MN", Grid, cell, Terrain, Terrain))
                {
                    text.Append(s.Name).Append(':');
                    foreach (NodeSpec n in s.Nodes)
                    {
                        text.Append(FormattableString.Invariant($"{n.Lat:F6},{n.Lon:F6},{n.Width:F3},{n.Height:F3},{n.Depth:F3},{n.Feature};"));
                    }

                    foreach (EdgeSpec e in s.Edges)
                    {
                        text.Append(e.From).Append('-').Append(e.To).Append(';');
                    }

                    text.Append('\n');
                }
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", string.Empty).Substring(0, 16);
                Assert.True(text.Length > 0);
                Assert.Equal(Fingerprint, hash);
            }
        }
    }
}
