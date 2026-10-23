using System;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Radar;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class WireframeBuilderTests
    {
        private const double R = 200000;

        private static TubeNetwork Net(params Primitive[] prims)
        {
            var list = new List<Primitive>(prims);
            var sdf = new NetworkSdf(list, new SpatialGrid(list), Hash.Of("wf"));
            return new TubeNetwork("wf", "Test", null, "wf", new Vec3d(0, R - 100, 0), list, sdf, new Skylight[0],
                new[] { new Vec3d(0, 1, 0) }, new[] { R });
        }

        private static TubeSegment Tube(float length, float width, float height) =>
            new TubeSegment(new Vec3(0, 0, 0), new Vec3(length, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), width, width, height, height, 0, ZoneKind.TubeGallery);

        [Fact]
        public void TubeRings()
        {
            // Rings at 0, 20, ..., 100 (6) of 24 segments each, plus the axis.
            var lines = WireframeBuilder.Build(Net(Tube(100, 20, 10)), i => true);
            Assert.Equal(6 * 24 + 1, lines.Count);
        }

        [Fact]
        public void ExcludedPrimitivesSkipped()
        {
            var net = Net(Tube(100, 20, 10), new Chamber(new Vec3(0, 0, 50), new Vec3(0, 1, 0), 30, 15));
            Assert.Empty(WireframeBuilder.Build(net, i => false));
        }

        [Fact]
        public void OnlyIncludedPrimitivesDrawn()
        {
            var net = Net(Tube(100, 20, 10), new Chamber(new Vec3(0, 0, 50), new Vec3(0, 1, 0), 30, 15));
            Assert.Equal(72, WireframeBuilder.Build(net, i => i == 1).Count);
            Assert.Equal(145, WireframeBuilder.Build(net, i => i == 0).Count);
            Assert.Equal(145 + 72, WireframeBuilder.Build(net, i => true).Count);
        }

        [Fact]
        public void ChamberThreeEllipses()
        {
            var lines = WireframeBuilder.Build(Net(new Chamber(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 30, 15)), i => true);
            Assert.Equal(72, lines.Count);
        }

        [Fact]
        public void ChamberEllipsesFollowSize()
        {
            var lines = WireframeBuilder.Build(Net(new Chamber(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 30, 15)), i => true);
            var points = lines.SelectMany(l => new[] { l.a, l.b }).ToList();
            Assert.InRange(points.Max(p => p.Y), 14.9f, 15.1f);
            Assert.InRange(points.Min(p => p.Y), -0.1f, 0.1f);
            Assert.InRange(points.Max(p => p.X), 29.9f, 30.1f);
            Assert.InRange(points.Max(p => p.Z), 29.9f, 30.1f);
        }

        [Fact]
        public void RingsFollowSize()
        {
            // 40 m long, 20 wide, 10 high: rings at 0, 20, 40. The middle one stands at x = 20.
            var lines = WireframeBuilder.Build(Net(Tube(40, 20, 10)), i => true);
            var ring = lines.Where(l => Math.Abs(l.a.X - 20) < 0.01f && Math.Abs(l.b.X - 20) < 0.01f)
                .SelectMany(l => new[] { l.a, l.b }).ToList();
            Assert.Equal(24 * 2, ring.Count);
            Assert.InRange(ring.Max(p => p.Z), 9.9f, 10.1f);
            Assert.InRange(ring.Min(p => p.Z), -10.1f, -9.9f);
            Assert.InRange(ring.Max(p => p.Y), 9.9f, 10.1f);
            Assert.InRange(ring.Min(p => p.Y), -0.1f, 0.1f);
        }

        [Fact]
        public void ShaftRingsEveryTwoRadiiAndAxis()
        {
            // Radius 8: rings every 16 m over 48 m (0, 16, 32, 48) of 24 segments, plus the axis.
            var lines = WireframeBuilder.Build(Net(new Shaft(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 8, 48)), i => true);
            Assert.Equal(4 * 24 + 1, lines.Count);
            Assert.All(lines.Where(l => Math.Abs(l.a.Y - l.b.Y) < 0.01f).SelectMany(l => new[] { l.a, l.b }),
                p => Assert.InRange(new Vec3(p.X, 0, p.Z).Length, 7.9f, 8.1f));
        }

        [Fact]
        public void ShortTubeStillClosedAtTheEnd()
        {
            // 25 m with a 20 m step: rings at 0, 20 and the end at 25.
            var lines = WireframeBuilder.Build(Net(Tube(25, 20, 10)), i => true);
            Assert.Equal(3 * 24 + 1, lines.Count);
        }
    }
}
