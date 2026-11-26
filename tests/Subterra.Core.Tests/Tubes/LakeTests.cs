using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Config;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using Xunit;
using static Subterra.Core.Tests.Sites;

namespace Subterra.Core.Tests.Tubes
{
    public class LakeTests
    {
        private const string LakeCfg = @"
SUBTERRA_BODY
{
    body = Mun
    TUBE_SITE
    {
        name = TestLake
        NODE
        {
            lat = 10
            lon = 20
            depth = 1500
            width = 2000
            height = 200
            feature = lake
        }
    }
}";

        private static SiteSpec Parsed(out List<string> warnings)
        {
            var r = BodySpecParser.Parse(CfgText.Parse(LakeCfg)[0]);
            warnings = r.Warnings;
            return r.Value.Sites.Single();
        }

        [Fact]
        public void ALakeIsWideAndNearlyFullOfWater()
        {
            SiteSpec site = Parsed(out List<string> warnings);
            Assert.Empty(warnings);
            NodeSpec lake = site.Nodes.Single();
            Assert.Equal(NodeFeature.Lake, lake.Feature);
            Assert.Equal(2000, lake.Width);
            Assert.Equal(BodySpecParser.LakeFill * 200, lake.Water, 6);
            Assert.True(lake.WaterReach >= lake.Width);
        }

        [Fact]
        public void ItCompilesToASmoothLensFullOfWater()
        {
            SiteSpec site = Parsed(out _);
            var warnings = new List<string>();
            TubeNetwork net = NetworkCompiler.Compile(site, "Mun", R, new FlatSampler(R), warnings);
            Assert.Empty(warnings);
            Assert.Single(net.Waters);
            Assert.Contains(net.Primitives, p => p.Zone == ZoneKind.SubglacialLake);

            // A point 1 km from the centre, half way up: in the void and under water.
            NodeSpec lake = site.Nodes.Single();
            Vec3d dir = BodyFrame.Direction(lake.Lat, lake.Lon);
            Vec3d east = Vec3d.Cross(new Vec3d(0, 1, 0), dir).Normalized;
            Vec3d floor = dir * (R - lake.Depth - lake.Height);
            Vec3d inside = floor + dir * (0.4 * lake.Height) + east * 1000;
            Vec3 local = net.ToLocal(inside);
            Assert.True(net.Sdf.Distance(local) < 0, "in the lake's void");
            Assert.NotNull(net.WaterAt(local, margin: 1f, above: 0f));
        }
    }
}
