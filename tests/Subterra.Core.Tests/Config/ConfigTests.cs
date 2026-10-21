using System.IO;
using System.Linq;
using Subterra.Core.Config;
using Subterra.Core.Model;
using Xunit;

namespace Subterra.Core.Tests.Config
{
    public class CfgTextTests
    {
        [Fact]
        public void ParsesNestedNodesAndComments()
        {
            var n = CfgText.Parse("A { x = 1 // c\n B\n {\n y = 2\n }\n}")[0];
            Assert.Equal("1", n.GetValue("x"));
            Assert.Equal("2", n.GetNodes("B").Single().GetValue("y"));
        }

        [Fact]
        public void BraceOnSameLineAndSiblings()
        {
            var roots = CfgText.Parse("A {\n}\nB\n{\n k = v = w\n C { }\n}");
            Assert.Equal(new[] { "A", "B" }, roots.Select(r => r.Name));
            Assert.Equal("v = w", roots[1].GetValue("k"));
            Assert.Single(roots[1].GetNodes("C"));
        }
    }

    public class BodySpecParserTests
    {
        private static ParseResult<BodySpec> Parse(string path) =>
            BodySpecParser.Parse(CfgText.Parse(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, path)))[0]);

        private static ParseResult<BodySpec> ParseText(string text) => BodySpecParser.Parse(CfgText.Parse(text)[0]);

        private const string TwoNodes = @"
SUBTERRA_BODY
{
    body = Mun
    TUBE_SITE
    {
        name = BadSite
        NODE
        {
            lat = 0
            lon = 0
            width = WIDTH
        }
        NODE
        {
            lat = 0
            lon = 1
        }
        EDGE
        {
            from = 0
            to = TO
        }
    }
}";

        [Fact]
        public void ParsesMunSample()
        {
            var r = Parse("Data/mun-sample.cfg");
            var b = r.Value;
            Assert.Equal("Mun", b.Body);
            Assert.Equal("MN", b.IdPrefix);
            Assert.Equal(250, b.Geotherm.SurfaceMeanTemp);
            Assert.Equal("Regolith", b.Strata[0].Name);
            Assert.Equal(8, b.Strata[0].Bottom);
            Assert.Equal(new[] { 0.55f, 0.53f, 0.50f }, b.Strata[0].Tint);
            var s = b.Sites.Single();
            Assert.Equal(NodeFeature.Skylight, s.Nodes[0].Feature);
            Assert.Equal(50, s.Nodes[0].SkylightRadius);
            Assert.Equal(new Range(20, 350), b.Fields[0].Width);
            Assert.Contains("Northern Basin", b.Fields[0].Biomes);
            Assert.Equal(ScatterPlacement.Floor, b.Scatters[0].Placement);
            Assert.Equal(new[] { ZoneKind.TubeGallery, ZoneKind.TubeChamber }, b.Scatters[0].Zones);
            Assert.Equal(0.4, b.Scatters[0].NoiseThreshold);
            Assert.Equal(ZoneKind.TubeGallery, b.Enrichment[0].Zone);
            Assert.Empty(r.Warnings);
        }

        [Fact]
        public void RejectsSiteWithEdgeToMissingNode()
        {
            var r = ParseText(TwoNodes.Replace("WIDTH", "60").Replace("TO", "5"));
            Assert.Empty(r.Value.Sites);
            Assert.Contains(r.Warnings, w => w.Contains("BadSite"));
        }

        [Fact]
        public void ClampsWidthAndWarns()
        {
            var r = ParseText(TwoNodes.Replace("WIDTH", "600").Replace("TO", "1"));
            Assert.Equal(500, r.Value.Sites[0].Nodes[0].Width);
            Assert.Contains(r.Warnings, w => w.Contains("width"));
        }

        [Fact]
        public void MissingBodyNameDropsNode()
        {
            var r = BodySpecParser.Parse(CfgText.Parse("SUBTERRA_BODY { idPrefix = XX }")[0]);
            Assert.Null(r.Value);
            Assert.NotEmpty(r.Warnings);
        }

        [Fact]
        public void MalformedNumberWarnsAndUsesDefault()
        {
            var r = ParseText("SUBTERRA_BODY\n{\n body = Mun\n caveScienceMultiplier = abc\n}");
            Assert.Equal(1.5, r.Value.CaveScienceMultiplier);
            Assert.Contains(r.Warnings, w => w.Contains("caveScienceMultiplier"));
        }

        private static FieldSpec Field(string lines) =>
            ParseText("SUBTERRA_BODY\n{\n body = Mun\n TUBE_FIELD\n {\n  name = F\n  biomes = Lowlands\n" + lines + "\n }\n}").Value.Fields.Single();

        [Theory]
        [InlineData("0, 0")]
        [InlineData("-3000, -10")]
        [InlineData("100, 200")]
        public void ClampsSkylightSpacingAndWarns(string value)
        {
            var r = ParseText("SUBTERRA_BODY\n{\n body = Mun\n TUBE_FIELD\n {\n  name = F\n  biomes = Lowlands\n  skylightSpacing = " + value + "\n }\n}");
            FieldSpec f = r.Value.Fields.Single();
            Assert.True(f.SkylightSpacing.Min >= 500, $"min {f.SkylightSpacing.Min}");
            Assert.True(f.SkylightSpacing.Max >= f.SkylightSpacing.Min);
            Assert.Contains(r.Warnings, w => w.Contains("skylightSpacing"));
        }

        [Fact]
        public void ClampsLengthAndRoofDepthAndWarns()
        {
            var r = ParseText("SUBTERRA_BODY\n{\n body = Mun\n TUBE_FIELD\n {\n  name = F\n  biomes = Lowlands\n  length = -100, 0\n  roofDepth = 0, -5\n }\n}");
            FieldSpec f = r.Value.Fields.Single();
            Assert.True(f.Length.Min > 0 && f.Length.Max >= f.Length.Min, $"length {f.Length}");
            Assert.True(f.RoofDepth.Min > 0 && f.RoofDepth.Max >= f.RoofDepth.Min, $"roofDepth {f.RoofDepth}");
            Assert.Contains(r.Warnings, w => w.Contains("'length'"));
            Assert.Contains(r.Warnings, w => w.Contains("'roofDepth'"));
        }

        [Fact]
        public void NonFiniteFieldRangesFallBackToDefaults()
        {
            FieldSpec defaults = new FieldSpec();
            FieldSpec f = Field("  skylightSpacing = NaN, 3000\n  length = 1000, Infinity");
            Assert.Equal(defaults.SkylightSpacing, f.SkylightSpacing);
            Assert.Equal(defaults.Length, f.Length);
        }

        [Fact]
        public void GoodFieldRangesNeedNoWarning()
        {
            var r = ParseText("SUBTERRA_BODY\n{\n body = Mun\n TUBE_FIELD\n {\n  name = F\n  biomes = Lowlands\n  length = 2000, 20000\n  roofDepth = 15, 120\n  skylightSpacing = 2000, 6000\n }\n}");
            Assert.Empty(r.Warnings);
            Assert.Equal(new Range(2000, 6000), r.Value.Fields.Single().SkylightSpacing);
        }

        [Fact]
        public void DefaultPrefixIsFirstTwoLetters() => Assert.Equal("MU", ParseText("SUBTERRA_BODY\n{\n body = Mun\n}").Value.IdPrefix);
    }
}

namespace Subterra.Core.Tests.Config
{
    public class BodyMergeTests
    {
        [Fact]
        public void MergesSameBody()
        {
            var nodes = CfgText.Parse(@"
SUBTERRA_BODY
{
    body = Mun
    idPrefix = MN
    GEOTHERM
    {
        surfaceMeanTemp = 250
    }
    TUBE_SITE
    {
        name = A
    }
}
SUBTERRA_BODY
{
    body = Mun
    idPrefix = ZZ
    caveScienceMultiplier = 3
    GEOTHERM
    {
        surfaceMeanTemp = 999
    }
    TUBE_SITE
    {
        name = B
    }
}
SUBTERRA_BODY
{
    body = Minmus
}");
            var merged = BodyMerge.MergeBodies(nodes);
            Assert.Equal(2, merged.Count);
            var mun = merged[0];
            Assert.Equal("MN", mun.GetValue("idPrefix"));
            Assert.Equal("3", mun.GetValue("caveScienceMultiplier"));
            Assert.Equal("250", mun.GetNode("GEOTHERM").GetValue("surfaceMeanTemp"));
            Assert.Equal(new[] { "A", "B" }, mun.GetNodes("TUBE_SITE").Select(n => n.GetValue("name")));
        }
    }
}
