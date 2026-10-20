using System;
using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Xunit;

namespace Subterra.Core.Tests.Sdf
{
    public class SdfTests
    {
        private static readonly TubeSegment Seg0 = Seg();

        private static TubeSegment Seg(float bench = 0) =>
            new TubeSegment(new Vec3(0, 0, 0), new Vec3(100, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 40, 40, 20, 20, bench, ZoneKind.TubeGallery);

        [Fact] public void InsideAboveFloor() => Assert.True(Seg0.Distance(new Vec3(50, 10, 0)) < 0);

        [Fact] public void RockBelowFloor() => Assert.True(Seg0.Distance(new Vec3(50, -1, 0)) > 0);

        [Fact] public void RockAboveApex() => Assert.True(Seg0.Distance(new Vec3(50, 21, 0)) > 0);

        [Fact] public void FlatFloorFullWidth() => Assert.True(Seg0.Distance(new Vec3(50, 0.2f, 19.5f)) < 0);

        [Fact] public void RoundedEnds() { Assert.True(Seg0.Distance(new Vec3(110, 5, 0)) < 0); Assert.True(Seg0.Distance(new Vec3(121, 5, 0)) > 0); }

        [Fact]
        public void BenchMakesLedge()
        {
            var s = Seg(bench: 0.25f);
            Assert.True(s.Distance(new Vec3(50, 2, 17)) > 0);
            Assert.True(s.Distance(new Vec3(50, 2, 10)) < 0);
            Assert.True(s.Distance(new Vec3(50, 6, 17)) < 0);
        }

        [Fact]
        public void ShaftOpenAlongAxis()
        {
            var sh = new Shaft(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 10, 50);
            Assert.True(sh.Distance(new Vec3(0, 49, 0)) < 0);
            Assert.True(sh.Distance(new Vec3(11, 25, 0)) > 0);
            Assert.True(sh.Distance(new Vec3(0, 51, 0)) > 0);
        }

        [Fact]
        public void ChamberIsDomeOnFlatFloor()
        {
            var c = new Chamber(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 100, 40);
            Assert.True(c.Distance(new Vec3(90, 1, 0)) < 0);
            Assert.True(c.Distance(new Vec3(0, -1, 0)) > 0);
            Assert.True(c.Distance(new Vec3(0, 41, 0)) > 0);
        }

        [Fact]
        public void SmoothMinNeverAboveMin()
        {
            Assert.Equal(Math.Min(3f, 5f), SdfOps.SmoothMin(3, 5, 0));
            Assert.True(SdfOps.SmoothMin(3, 3.5f, 2) < 3);
        }

        [Fact]
        public void GridMatchesBruteForce()
        {
            var prims = RandomPrims(50, 1);
            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), 1, smoothFactor: 0);
            var rng = new Rng(2);
            int compared = 0;
            for (int i = 0; i < 6000; i++)
            {
                var p = RandomPoint(rng);
                float brute = float.MaxValue;
                foreach (var prim in prims)
                {
                    brute = Math.Min(brute, prim.Distance(p));
                }

                // Near the surfaces (where meshing looks) the grid must find the same primitive.
                if (brute < 4f)
                {
                    compared++;
                    Assert.True(Math.Abs(sdf.DistanceNoNoise(p) - brute) < 1e-4f, $"at {p}: grid {sdf.DistanceNoNoise(p)} vs brute {brute}");
                }
                else
                {
                    Assert.True(sdf.DistanceNoNoise(p) > 0);
                }
            }

            Assert.True(compared > 200, $"only {compared} points near surfaces");
        }

        [Fact]
        public void RegionViewMatchesFullField()
        {
            var prims = RandomPrims(30, 3);
            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), 9);
            var region = new Aabb(new Vec3(100, -20, 100), new Vec3(300, 60, 300));
            ISdf view = sdf.ForRegion(region);
            var rng = new Rng(4);
            for (int i = 0; i < 2000; i++)
            {
                var p = new Vec3((float)rng.Range(100, 300), (float)rng.Range(-20, 60), (float)rng.Range(100, 300));
                Assert.Equal(sdf.Distance(p), view.Distance(p));
            }
        }

        [Fact]
        public void NoiseBounded()
        {
            var prims = RandomPrims(30, 5);
            foreach (var prim in prims)
            {
                prim.Roughness = 3f;
                prim.NoiseScale = 15f;
            }

            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), 7);
            var rng = new Rng(6);
            for (int i = 0; i < 2000; i++)
            {
                var p = RandomPoint(rng);
                int nearest = sdf.NearestPrimitive(p);
                float limit = nearest < 0 ? 0 : prims[nearest].Roughness;
                Assert.True(Math.Abs(sdf.Distance(p) - sdf.DistanceNoNoise(p)) <= limit + 1e-4f);
            }
        }

        private static List<Primitive> RandomPrims(int count, ulong seed)
        {
            var rng = new Rng(seed);
            var up = new Vec3(0, 1, 0);
            var prims = new List<Primitive>();
            for (int i = 0; i < count; i++)
            {
                var a = new Vec3((float)rng.Range(0, 400), (float)rng.Range(0, 40), (float)rng.Range(0, 400));
                switch (i % 3)
                {
                    case 0:
                        var b = a + new Vec3((float)rng.Range(-80, 80), (float)rng.Range(-5, 5), (float)rng.Range(-80, 80));
                        float w = (float)rng.Range(20, 60);
                        prims.Add(new TubeSegment(a, b, up, up, w, w, w * 0.5f, w * 0.5f, 0, ZoneKind.TubeGallery));
                        break;
                    case 1:
                        prims.Add(new Chamber(a, up, (float)rng.Range(20, 50), (float)rng.Range(10, 30)));
                        break;
                    default:
                        prims.Add(new Shaft(a, up, (float)rng.Range(5, 20), (float)rng.Range(20, 80)));
                        break;
                }
            }

            return prims;
        }

        private static Vec3 RandomPoint(Rng rng) =>
            new Vec3((float)rng.Range(-50, 450), (float)rng.Range(-30, 120), (float)rng.Range(-50, 450));
    }
}
