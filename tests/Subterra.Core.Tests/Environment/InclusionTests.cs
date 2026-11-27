using System.Collections.Generic;
using Subterra.Core.Config;
using Subterra.Core.Environment;
using Subterra.Core.Gameplay;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Xunit;

namespace Subterra.Core.Tests.Environment
{
    // Lucas: Gilly as the fragment of a differentiated parent: blocks of its iron core and olivine mantle in the rubble.
    public class InclusionTests
    {
        private const double R = 13000;

        private static readonly LayerSpec Rubble = new LayerSpec { Name = "Rubble", Top = 0, Bottom = 30000 };

        private static InclusionSpec Iron() => new InclusionSpec
        {
            Name = "GillyIronBlock", Lat = 20, Lon = 110, Depth = 7500, Radius = 2600, Lumpiness = 0.25,
            Layer = new LayerSpec { Name = "GillyIronBlock", Top = 4900, Bottom = 10100 },
        };

        [Fact]
        public void ABlockHoldsItsCentreAndNotFarPoints()
        {
            InclusionSpec iron = Iron();
            Assert.True(iron.Contains(iron.Centre(R), R));
            Assert.False(iron.Contains(BodyFrame.Direction(-20, -70) * (R - 7500), R), "the other side of the body");
            Assert.False(iron.Contains(iron.Centre(R) + BodyFrame.Direction(20, 110) * 3300, R), "past its lumpiest reach");
        }

        [Fact]
        public void ItsSurfaceIsLumpyButBounded()
        {
            InclusionSpec iron = Iron();
            double least = double.MaxValue, most = 0;
            for (int i = 0; i < 200; i++)
            {
                double r = iron.SurfaceRadius(BodyFrame.Direction(-90 + i * 0.9, i * 7.3));
                least = System.Math.Min(least, r);
                most = System.Math.Max(most, r);
            }

            Assert.InRange(least, 2600 * 0.75 - 1, 2600);
            Assert.InRange(most, 2600, 2600 * 1.25 + 1);
            Assert.True(most - least > 200, "not a plain sphere");
        }

        [Fact]
        public void StrataGiveTheBlocksRockWhereItHoldsThePoint()
        {
            InclusionSpec iron = Iron();
            var strata = new Strata(new List<LayerSpec> { Rubble }, new List<InclusionSpec> { iron }, R);
            Assert.Same(iron.Layer, strata.LayerAt(iron.Centre(R), 7500, null));
            Assert.Same(Rubble, strata.LayerAt(BodyFrame.Direction(-20, -70) * (R - 7500), 7500, null));
            Assert.Same(Rubble, strata.LayerAt(7500, null));
        }

        [Fact]
        public void ParsedFromTheBodyConfig()
        {
            CfgNode node = CfgText.Parse(@"SUBTERRA_BODY
{
	body = Gilly
	STRATA
	{
		LAYER
		{
			name = Rubble
			top = 0
			bottom = 30000
		}
	}
	INCLUSION
	{
		name = GillyIronBlock
		lat = 20
		lon = 110
		depth = 7500
		radius = 2600
		lumpiness = 0.25
		hardness = 0.85
		density = 7.6
		YIELD
		{
			resource = Ore
			rate = 0.08
		}
	}
}")[0];
            BodySpec spec = BodySpecParser.Parse(node).Value;
            Assert.Single(spec.Inclusions);
            InclusionSpec iron = spec.Inclusions[0];
            Assert.Equal(7500, iron.Depth);
            Assert.Equal(2600, iron.Radius);
            Assert.Equal(0.85, iron.Layer.Hardness);
            Assert.Equal(0.08, iron.Layer.Yields["Ore"]);
            Assert.Equal(4900, iron.Layer.Top);
            Assert.Same(iron.Layer, BiomeIds.LayerOf(BiomeIds.LayerPrefix + "GillyIronBlock", spec));
        }
    }
}
