using System.Collections.Generic;
using Subterra.Core.Gameplay;
using Subterra.Core.Model;
using Subterra.Core.Tubes;
using Xunit;

namespace Subterra.Core.Tests.Gameplay
{
    public class BiomeIdsTests
    {
        private static readonly BodySpec Mun = new BodySpec { Body = "Mun", Sites = { new SiteSpec { Name = "SerenitySkylight", IsNamed = true } } };

        [Fact] public void ZoneId() => Assert.Equal("TubeGallery", BiomeIds.For(new ZoneInfo(ZoneKind.TubeGallery, null, "MN-0001")));

        [Fact] public void SiteId() => Assert.Equal("SerenitySkylight", BiomeIds.For(new ZoneInfo(ZoneKind.TubeGallery, "SerenitySkylight", "site:SerenitySkylight")));

        [Fact]
        public void Multipliers()
        {
            Assert.Equal(1.5, BiomeIds.Multiplier("TubeGallery", Mun));
            Assert.Equal(2.0, BiomeIds.Multiplier("SerenitySkylight", Mun));
        }

        [Fact]
        public void IsSubterra()
        {
            Assert.False(BiomeIds.IsSubterra("Lowlands", Mun));
            Assert.True(BiomeIds.IsSubterra("FrozenVault", Mun));
            Assert.True(BiomeIds.IsSubterra("SerenitySkylight", Mun));
            Assert.False(BiomeIds.IsSubterra("None", Mun));
        }

        [Fact]
        public void LocKeys()
        {
            Assert.Equal("#Subterra_Biome_TubeChamber", BiomeIds.LocKey("TubeChamber"));
            Assert.Equal("#Subterra_Site_DevTube", BiomeIds.LocKey("DevTube"));
        }
    }

    public class EnrichmentTests
    {
        private static EnrichmentSpec Rule(string resource, ZoneKind? zone = null, string layer = null, double mult = 1, double min = 0) =>
            new EnrichmentSpec { Resource = resource, Zone = zone, Layer = layer, Multiplier = mult, MinAbundance = min };

        [Fact] public void NoRulesBase() => Assert.Equal(0.03, Enrichment.Apply(new List<EnrichmentSpec>(), "Ore", ZoneKind.TubeGallery, "MareBasalt", 0.03));

        [Fact] public void ZoneMultiplier() => Assert.Equal(0.06, Enrichment.Apply(new[] { Rule("Ore", ZoneKind.TubeGallery, mult: 2) }, "Ore", ZoneKind.TubeGallery, null, 0.03), 12);

        [Fact] public void LayerMismatchBase() => Assert.Equal(0.03, Enrichment.Apply(new[] { Rule("Ore", layer: "MareBasalt", mult: 2.5) }, "Ore", ZoneKind.TubeGallery, "Regolith", 0.03));

        [Fact]
        public void BestOfTwo() => Assert.Equal(0.075, Enrichment.Apply(new[] { Rule("Ore", ZoneKind.TubeGallery, mult: 1.5), Rule("Ore", layer: "MareBasalt", mult: 2.5) },
            "Ore", ZoneKind.TubeGallery, "MareBasalt", 0.03), 12);

        [Fact] public void MinAbundanceFloor() => Assert.Equal(0.05, Enrichment.Apply(new[] { Rule("Water", ZoneKind.FrozenVault, min: 0.05) }, "Water", ZoneKind.FrozenVault, null, 0));
    }
}
