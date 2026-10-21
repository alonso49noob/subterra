using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Config;
using Subterra.Core.Environment;
using Subterra.Core.Excavation;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;
using Xunit;

namespace Subterra.Core.Tests.Excavation
{
    public class DrillMathTests
    {
        [Fact]
        public void HarderRockIsSlower()
        {
            Assert.Equal(0.5, DrillMath.Rate(0.5, 0, null, 1000), 6);
            Assert.True(DrillMath.Rate(0.5, 0.6, null, 1000) < DrillMath.Rate(0.5, 0.15, null, 1000));
            Assert.Equal(0, DrillMath.Rate(0.5, 1, null, 1000), 9);
        }

        [Fact]
        public void HeatStopsTheDrill()
        {
            Assert.Equal(0.5, DrillMath.Rate(0.5, 0, 800, 1000), 6);
            Assert.Equal(0.25, DrillMath.Rate(0.5, 0, 900, 1000), 6);
            Assert.Equal(0, DrillMath.Rate(0.5, 0, 1000, 1000), 9);
            Assert.Equal(0, DrillMath.Rate(0.5, 0, 1500, 1000), 9);
        }

        [Fact]
        public void VoidLimitScalesWithGravity()
        {
            Assert.InRange(DrillMath.VoidLimit(150, 2.7, 9.81), 5600, 5700);
            Assert.InRange(DrillMath.VoidLimit(150, 2.7, 1.63), 34000, 34200);
            Assert.True(double.IsPositiveInfinity(DrillMath.VoidLimit(150, 2.7, 0)));
        }
    }

    public class BoreholeTests
    {
        private static readonly Strata Rock = new Strata(new List<LayerSpec>
        {
            new LayerSpec { Name = "Regolith", Top = 0, Bottom = 8, Hardness = 0.15, Density = 1.6, Gradient = 4 },
            new LayerSpec { Name = "Basalt", Top = 8, Bottom = 1500, Hardness = 0.6, Density = 3.1, Gradient = 4 },
        });

        private static readonly Geotherm Heat = new Geotherm(new GeothermSpec { SurfaceMeanTemp = 250 }, Rock);

        [Fact]
        public void CrossesLayersAtEachLayersSpeed()
        {
            // 8 m of regolith at 0.5 · 0.85² = 0.36125 m/s, then basalt at 0.5 · 0.4² = 0.08 m/s.
            Borehole.Progress p = Borehole.Advance(0, 100, 0.5, 2000, 2000, Rock, Heat, "Lowlands");
            double inRegolith = 8 / 0.36125;
            Assert.Equal(8 + (100 - inRegolith) * 0.08, p.Depth, 6);
            Assert.Equal("Basalt", p.Layer.Name);
            Assert.False(p.Blocked);
        }

        [Fact]
        public void StopsAtTheMaximumDepth()
        {
            Borehole.Progress p = Borehole.Advance(0, 1e6, 0.5, 2000, 50, Rock, Heat, "Lowlands");
            Assert.Equal(50, p.Depth, 6);
            Assert.True(p.Blocked);
        }

        [Fact]
        public void TooHotRockBlocks()
        {
            var hot = new Strata(new List<LayerSpec> { new LayerSpec { Name = "Hot", Top = 0, Bottom = 100000, Hardness = 0, Density = 3, Gradient = 30 } });
            var heat = new Geotherm(new GeothermSpec { SurfaceMeanTemp = 250 }, hot);

            // 30 K/km: the drill rated 400 K stops where the rock reaches 400 K, 5 km down.
            Borehole.Progress p = Borehole.Advance(0, 1e6, 1, 400, 100000, hot, heat, "Lowlands");
            Assert.True(p.Blocked);
            Assert.InRange(p.Depth, 4800, 5000);
        }
    }

    public class ExcavationSpecTests
    {
        private static ExcavationSpec Tunnel() => new ExcavationSpec("XP-Mun-1", "Mun", new Vec3d(0, 200000, 0), 2.5f);

        [Fact]
        public void StraightDiggingGrowsOnePiece()
        {
            ExcavationSpec t = Tunnel();
            t.Append(new Vec3(0, 0, 0));
            t.Append(new Vec3(1, 0, 0));
            t.Append(new Vec3(2, 0, 0));
            t.Append(new Vec3(3, 0, 0));
            Assert.Equal(1, t.PieceCount);
            Assert.Equal(new Vec3(3, 0, 0), t.Points[1]);
        }

        [Fact]
        public void AWobblingBorerStillGrowsOnePiece()
        {
            // A rover settling on its wheels while it digs: each metre a little off the line.
            ExcavationSpec t = Tunnel();
            t.Append(new Vec3(0, 0, 0));
            for (int x = 1; x <= 6; x++)
            {
                t.Append(new Vec3(x, x % 2 == 0 ? 0.1f : -0.1f, x % 3 == 0 ? 0.08f : 0f));
            }

            Assert.Equal(1, t.PieceCount);
        }

        [Fact]
        public void TurnsAndLongRunsStartNewPieces()
        {
            ExcavationSpec t = Tunnel();
            t.Append(new Vec3(0, 0, 0));
            t.Append(new Vec3(5, 0, 0));
            t.Append(new Vec3(10, 0, 3));
            Assert.Equal(2, t.PieceCount);
            for (int x = 11; x <= 60; x++)
            {
                t.Append(new Vec3(x, 0, 3));
            }

            Assert.All(Enumerable.Range(0, t.PieceCount), i => Assert.True((t.Points[i + 1] - t.Points[i]).Length <= ExcavationSpec.MaxPieceLength + 1.5f));
        }

        [Fact]
        public void RoundTripsThroughCfg()
        {
            ExcavationSpec t = Tunnel();
            t.Append(new Vec3(0, 0, 0));
            t.Append(new Vec3(4.25f, -1.5f, 0.125f));
            t.JoinedTo = "site:DevTube";
            var warnings = new List<string>();
            CfgNode node = CfgText.Parse(Write(t.ToCfg()))[0];
            ExcavationSpec back = ExcavationSpec.FromCfg(node, warnings);
            Assert.Empty(warnings);
            Assert.Equal(t.Id, back.Id);
            Assert.Equal(t.Anchor, back.Anchor);
            Assert.Equal(t.Radius, back.Radius);
            Assert.Equal("site:DevTube", back.JoinedTo);
            Assert.Equal(t.Points, back.Points);
        }

        [Fact]
        public void BrokenNodesAreSkippedWithAWarning()
        {
            var warnings = new List<string>();
            Assert.Null(ExcavationSpec.FromCfg(CfgText.Parse("EXCAVATION { id = X }")[0], warnings));
            Assert.NotEmpty(warnings);
        }

        // CfgNode → text, the way KSP writes a ConfigNode.
        private static string Write(CfgNode node)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(node.Name).Append("\n{\n");
            foreach (var pair in node.Values)
            {
                sb.Append(pair.Key).Append(" = ").Append(pair.Value).Append('\n');
            }

            sb.Append("}\n");
            return sb.ToString();
        }
    }

    public class TunnelJoinTests
    {
        private const double R = 200000;

        // A straight cave along x, floor at the anchor, 100 m under flat ground.
        private static TubeNetwork Cave()
        {
            var anchor = new Vec3d(0, R - 100, 0);
            var prims = new List<Primitive>
            {
                new TubeSegment(new Vec3(-50, 0, 0), new Vec3(50, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 20, 20, 12, 12, 0, ZoneKind.TubeGallery),
            };
            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), Hash.Of("cave"));
            return new TubeNetwork("cave", "Test", null, "cave", anchor, prims, sdf, new Skylight[0],
                new[] { new Vec3d(0, 1, 0) }, new[] { R });
        }

        [Fact]
        public void ATunnelReachingACaveOpensItsWall()
        {
            TubeNetwork cave = Cave();

            // Dug from 40 m to the side of the cave, at its floor level, straight into it.
            var tunnel = new ExcavationSpec("XP-Test-1", "Test", cave.Anchor + new Vec3d(0, 0, 40), 2.5f);
            for (int z = 0; z >= -40; z--)
            {
                tunnel.Append(new Vec3(0, 0, z));
            }

            // Before: rock between the tunnel's start and the cave wall.
            Assert.True(cave.Sdf.Distance(new Vec3(0, 1.5f, 25)) > 0);

            List<Primitive> pieces = tunnel.Pieces(cave.Anchor);
            TubeNetwork joined = cave.WithPrimitives(pieces, new[] { cave.Anchor.Normalized }, new[] { R });

            // After: void all the way from the tunnel's start into the cave, no wall at the junction.
            for (float z = 39; z >= 0; z -= 0.5f)
            {
                Assert.True(joined.Sdf.Distance(new Vec3(0, 1.5f, z)) < 0, $"rock at z = {z}");
            }

            Assert.Equal(ZoneKind.Excavation, joined.ZoneAt(new Vec3(0, 1.5f, 30)).Kind);
            Assert.Equal(ZoneKind.TubeGallery, joined.ZoneAt(new Vec3(0, 1.5f, 0)).Kind);
        }

        [Fact]
        public void JoiningKeepsTheCaveUnchangedAwayFromTheTunnel()
        {
            TubeNetwork cave = Cave();
            var tunnel = new ExcavationSpec("XP-Test-1", "Test", cave.Anchor + new Vec3d(0, 0, 40), 2.5f);
            tunnel.Append(new Vec3(0, 0, 0));
            tunnel.Append(new Vec3(0, 0, -35));
            TubeNetwork joined = cave.WithPrimitives(tunnel.Pieces(cave.Anchor), new Vec3d[0], new double[0]);

            // Far along the cave the field is identical (same noise seed, no extra primitive nearby).
            foreach (Vec3 p in new[] { new Vec3(-45, 3, 0), new Vec3(-40, 11, 4), new Vec3(-30, 6, -9) })
            {
                Assert.Equal(cave.Sdf.Distance(p), joined.Sdf.Distance(p), 4);
            }
        }

        [Fact]
        public void AStandaloneTunnelIsItsOwnNetwork()
        {
            var tunnel = new ExcavationSpec("XP-Test-2", "Test", new Vec3d(0, R - 10, 0), 2.5f);
            tunnel.Append(new Vec3(0, 0, 0));
            tunnel.Append(new Vec3(12, -3, 0));
            TubeNetwork net = TubeNetwork.ForExcavation(tunnel, new[] { new Vec3d(0, 1, 0) }, new[] { R }, null);
            Assert.Equal("XP-Test-2", net.Id);
            Assert.False(net.IsNamed);
            Assert.True(net.IsInside(new Vec3(6, 0, 0)));
            Assert.Equal(ZoneKind.Excavation, net.ZoneAt(new Vec3(6, 0, 0)).Kind);
        }
    }
}

namespace Subterra.Core.Tests.Excavation
{
    public class BlastTests
    {
        private static readonly Strata Rock = new Strata(new System.Collections.Generic.List<LayerSpec>
        {
            new LayerSpec { Name = "Regolith", Top = 0, Bottom = 8, Hardness = 0.15, Density = 1.6 },
            new LayerSpec { Name = "Basalt", Top = 8, Bottom = 1500, Hardness = 0.6, Density = 3.1 },
        });

        [Fact]
        public void DecksAlternateWithGapsBelowTheStemming()
        {
            BlastDesign d = BlastDesign.Plan(0, 40, 3, Rock, "Lowlands", null);
            Assert.Equal(3, d.Stemming);
            Assert.NotEmpty(d.Decks);
            Assert.All(d.Decks, deck => Assert.True(deck.Top >= d.HoleTop + d.Stemming - 1e-9 && deck.Bottom <= 40));
            for (int i = 1; i < d.Decks.Count; i++)
            {
                Assert.True(d.Decks[i - 1].Top - d.Decks[i].Bottom >= BlastDesign.DeckGap - 1e-9, "decks overlap or touch");
            }
        }

        [Fact]
        public void HardRockGetsMoreExplosive()
        {
            BlastDesign d = BlastDesign.Plan(0, 40, 3, Rock, "Lowlands", null);
            double soft = d.Decks.First(x => x.Layer == "Regolith").Kg, hard = d.Decks.First(x => x.Layer == "Basalt").Kg;
            Assert.True(hard > soft * 2, $"basalt {hard:F0} kg vs regolith {soft:F0} kg");
        }

        [Fact]
        public void NothingWhereTheHoleCrossesACave()
        {
            BlastDesign d = BlastDesign.Plan(0, 80, 3, Rock, "Lowlands", new[] { (30.0, 55.0) });
            Assert.DoesNotContain(d.Decks, deck => deck.Top < 55 && deck.Bottom > 30);
            Assert.Contains(d.Decks, deck => deck.Bottom > 55);
            Assert.Contains(d.Decks, deck => deck.Top < 30);
        }

        [Fact]
        public void ConfinementShrinksTheDangerZone()
        {
            var open = BlastSafety.Radii(100, false);
            var buried = BlastSafety.Radii(100, true);
            Assert.InRange(open.destroy, 13.5, 14.5);
            Assert.True(buried.damage < open.damage * 0.4);
            Assert.True(open.destroy < open.damage);
            Assert.Equal((0.0, 0.0), BlastSafety.Radii(0, true));
        }
    }
}

namespace Subterra.Core.Tests.Excavation
{
    public class BlastShaftTests
    {
        private const double R = 200000;

        [Fact]
        public void ABlastShaftIsAVerticalVoidThatRoundTrips()
        {
            var shaft = new ExcavationSpec("XP-Test-3", "Test", new Vec3d(0, R - 40, 0), 3f) { Shape = ExcavationShape.Shaft };
            shaft.Points.Add(new Vec3(0, 0, 0));
            shaft.Points.Add(new Vec3(0, 40, 0));
            Assert.IsType<Shaft>(shaft.Piece(0, shaft.Anchor));
            TubeNetwork net = TubeNetwork.ForExcavation(shaft, new[] { new Vec3d(0, 1, 0) }, new[] { R }, null);
            Assert.True(net.IsInside(new Vec3(0, 20, 0)));
            Assert.False(net.IsInside(new Vec3(5, 20, 0)));
            Assert.Equal(ZoneKind.Excavation, net.ZoneAt(new Vec3(0, 20, 0)).Kind);

            ExcavationSpec back = ExcavationSpec.FromCfg(CfgText.Parse(Cfg(shaft.ToCfg()))[0], new List<string>());
            Assert.Equal(ExcavationShape.Shaft, back.Shape);
        }

        private static string Cfg(CfgNode node) =>
            node.Name + "\n{\n" + string.Concat(node.Values.Select(v => v.Key + " = " + v.Value + "\n")) + "}\n";
    }
}
