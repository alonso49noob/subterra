using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using Xunit;
using static Subterra.Core.Tests.Sites;

namespace Subterra.Core.Tests.Tubes
{
    public class TubePathTests
    {
        private static readonly FlatSampler Flat = new FlatSampler(R);

        private static List<PathPoint> Build(SiteSpec site, List<string> warnings = null) =>
            TubePath.Build(site, site.Edges[0], R, Flat, warnings ?? new List<string>());

        private static double Dist(PathPoint a, PathPoint b) => Vec3d.Distance(a.FloorCenter, b.FloorCenter);

        [Fact]
        public void SubdividesToMaxStep()
        {
            var site = Line("Two", 0, 10, 2000, 2, sinuosity: 0.5);
            var pts = Build(site);
            for (int i = 1; i < pts.Count; i++)
            {
                Assert.True(Dist(pts[i - 1], pts[i]) <= 500.0 + 1e-6, $"step {i}: {Dist(pts[i - 1], pts[i])}");
            }

            Assert.True(pts.Count >= 5);
        }

        [Fact]
        public void FloorRadiusIsSurfaceMinusDepthMinusHeight()
        {
            var pts = Build(Line("Two", 0, 10, 2000, 2));
            Assert.Equal(200000 - 95 - 60, pts[0].FloorCenter.Length, 6);
        }

        [Fact]
        public void RoofRuleRaisesDepthAndWarns()
        {
            var site = Line("Thin", 0, 10, 2000, 2, tweak: (i, n) => { n.Width = 200; n.Depth = 10; return n; });
            var warnings = new List<string>();
            var pts = Build(site, warnings);
            Assert.Equal(16, pts[0].Depth, 6);
            Assert.Contains(warnings, s => s.Contains("roof too thin"));
        }

        [Fact]
        public void ZeroSinuosityFollowsGreatCircle()
        {
            var site = new SiteSpec { Name = "Diag", IsNamed = true };
            site.Nodes.Add(Node(1, 10));
            site.Nodes.Add(Node(1.05, 10.06));
            site.Edges.Add(new EdgeSpec { From = 0, To = 1, Sinuosity = 0 });
            var pts = Build(site);
            Vec3d a = BodyFrame.Direction(1, 10), b = BodyFrame.Direction(1.05, 10.06);
            Vec3d normal = Vec3d.Cross(a, b).Normalized;
            foreach (var p in pts)
            {
                Assert.True(Math.Abs(Vec3d.Dot(p.Up, normal)) * R < 1.0, $"off the great circle by {Vec3d.Dot(p.Up, normal) * R} m");
            }
        }

        [Fact]
        public void SinuosityWandersButEndsStay()
        {
            var site = Line("Wavy", 0, 10, 6000, 2, sinuosity: 1);
            var pts = Build(site);
            Assert.Equal(0, pts[0].Lat, 9);
            Assert.Equal(0, pts[pts.Count - 1].Lat, 9);
            Assert.Contains(pts, p => Math.Abs(p.Lat) > Deg(10));
        }

        [Fact]
        public void Deterministic()
        {
            var site = Line("Det", 0, 10, 3000, 3, sinuosity: 0.6);
            Assert.Equal(Serialize(site), Serialize(site));
        }

        [Fact]
        public void SamplePointsCoverBuildPoints()
        {
            var site = Line("Cover", 0, 10, 3000, 4, sinuosity: 0.6);
            var samples = new PrecomputedSamples();
            foreach (var (lat, lon) in TubePath.SamplePoints(site, R))
            {
                samples.Add(lat, lon, R, "Lowlands");
            }

            foreach (EdgeSpec edge in site.Edges)
            {
                foreach (var p in TubePath.Build(site, edge, R, samples, new List<string>()))
                {
                    Assert.True(samples.Has(p.Lat, p.Lon));
                }
            }
        }

        private static string Serialize(SiteSpec site)
        {
            var sb = new StringBuilder();
            foreach (EdgeSpec edge in site.Edges)
            {
                foreach (var p in TubePath.Build(site, edge, R, Flat, new List<string>()))
                {
                    sb.Append(p.FloorCenter).Append(';');
                }
            }

            return sb.ToString();
        }
    }
}
