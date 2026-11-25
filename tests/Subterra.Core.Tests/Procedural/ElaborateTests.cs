using System.Linq;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using Xunit;

namespace Subterra.Core.Tests.Procedural
{
    public class ElaborateTests
    {
        private const double R = 200000;

        private sealed class Flat : IHeightSampler
        {
            public double SurfaceRadius(double latDeg, double lonDeg) => R + 0.001 * lonDeg;
        }

        private static SiteSpec Corridor(string name)
        {
            var site = new SiteSpec { Name = name, IsNamed = true };
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = 0, Depth = 40, Width = 60, Height = 30, Feature = NodeFeature.Skylight, SkylightRadius = 20 });
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = 0.4, Depth = 60, Width = 120, Height = 50, Feature = NodeFeature.Chamber });
            site.Nodes.Add(new NodeSpec { Lat = 0, Lon = 0.8, Depth = 70, Width = 60, Height = 30, Feature = NodeFeature.End });
            site.Edges.Add(new EdgeSpec { From = 0, To = 1 });
            site.Edges.Add(new EdgeSpec { From = 1, To = 2 });
            return site;
        }

        [Fact]
        public void ANamedCorridorGrowsIntoANetworkAroundItsOwnNodes()
        {
            SiteSpec site = Corridor("Test");
            FieldGenerator.Elaborate(site, R, new Flat());
            Assert.True(site.Nodes.Count > 8, $"{site.Nodes.Count} nodes");
            Assert.True(site.Edges.Count > site.Nodes.Count - 1, "loops (more edges than a tree)");
            Assert.Equal(NodeFeature.Skylight, site.Nodes[0].Feature);
            Assert.Equal(NodeFeature.Chamber, site.Nodes[1].Feature);
            Assert.Equal(0.4, site.Nodes[1].Lon);
            Assert.All(site.Nodes.Skip(3), n => Assert.InRange(n.Width, 10, 120));

            // Once only, and the same every time.
            int count = site.Nodes.Count;
            FieldGenerator.Elaborate(site, R, new Flat());
            Assert.Equal(count, site.Nodes.Count);
            SiteSpec again = Corridor("Test");
            FieldGenerator.Elaborate(again, R, new Flat());
            Assert.Equal(site.Nodes.Select(n => n.Lat + n.Lon), again.Nodes.Select(n => n.Lat + n.Lon));
        }

        [Fact]
        public void SpecialShapesStayAsTheyAre()
        {
            SiteSpec shaft = Corridor("Shaft");
            shaft.Nodes[1].Altitude = -500;
            FieldGenerator.Elaborate(shaft, R, new Flat());
            Assert.Equal(3, shaft.Nodes.Count);

            SiteSpec off = Corridor("Off");
            off.Network = false;
            FieldGenerator.Elaborate(off, R, new Flat());
            Assert.Equal(3, off.Nodes.Count);
        }
    }
}
