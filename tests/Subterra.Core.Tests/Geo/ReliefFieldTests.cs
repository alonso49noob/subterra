using System.Collections.Generic;
using Subterra.Core.Config;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Geo
{
    public class ReliefFieldTests
    {
        private const double R = 600000;

        private static double Deg(double metres) => metres / R * 180 / System.Math.PI;

        private static Vec3d At(double lat, double lon) => BodyFrame.Direction(lat, lon);

        [Fact]
        public void VolcanoRisesToACraterAndFadesAtItsFoot()
        {
            var v = new ReliefSpec { Kind = ReliefKind.Volcano, Lat = 0, Lon = 0, Radius = 6000, Height = 1500, CraterRadius = 600, CraterDepth = 250 };
            var field = new ReliefField(new[] { v }, R);
            Assert.Equal(0, field.HeightDelta(At(0, Deg(7000))), 6);
            double flank = field.HeightDelta(At(0, Deg(3000)));
            double rim = field.HeightDelta(At(0, Deg(600)));
            double floor = field.HeightDelta(At(0, 0));
            Assert.InRange(flank, 400, 1100);
            Assert.True(rim > flank);
            Assert.True(floor < rim - 200, "crater floor under the rim");
            Assert.True(floor > 0, "crater floor over the plain");
        }

        [Fact]
        public void RilleSinksAlongItsPathButNotOverBridges()
        {
            var rille = new ReliefSpec { Kind = ReliefKind.Rille, Width = 400, Depth = 60 };
            rille.Path.Add((0, 0));
            rille.Path.Add((0, Deg(10000)));
            rille.Bridges.Add((0.5, 400));
            var field = new ReliefField(new[] { rille }, R);
            Assert.Equal(-60, field.HeightDelta(At(0, Deg(2500))), 1);
            Assert.Equal(0, field.HeightDelta(At(Deg(300), Deg(2500))), 6);
            Assert.Equal(0, field.HeightDelta(At(0, Deg(5000))), 3);
            Assert.True(field.HeightDelta(At(0, Deg(100))) > -60, "the ends taper");
        }

        [Fact]
        public void ParsesReliefNodes()
        {
            var r = BodySpecParser.Parse(CfgText.Parse(@"
SUBTERRA_BODY
{
	body = Kerbin
	RELIEF
	{
		name = Sleeper
		kind = volcano
		lat = 1
		lon = 2
		radius = 5000
		height = 1200
	}
	RELIEF
	{
		name = Hadley
		kind = rille
		point = 0, 0
		point = 0, 1
		width = 500
		depth = 80
		bridge = 0.4, 300
	}
}")[0]);
            Assert.Empty(r.Warnings);
            Assert.Equal(2, r.Value.Reliefs.Count);
            Assert.Equal(ReliefKind.Rille, r.Value.Reliefs[1].Kind);
            Assert.Single(r.Value.Reliefs[1].Bridges);
        }
    }
}
