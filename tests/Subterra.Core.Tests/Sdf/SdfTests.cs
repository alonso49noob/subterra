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
        public void CenoteShaftIsBellShapedWithALobedRimAndQuietUnderTheGround()
        {
            var sh = new Shaft(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 10, 50) { Flare = 1.8f, Lobes = 0.22f, LobeSeed = 1.3f, Ground = 20 };

            // Wider at the bottom than under the rim: open 15 m out near the bottom, rock there under the rim.
            Assert.True(sh.Distance(new Vec3(15, 2, 0)) < 0);
            Assert.True(sh.Distance(new Vec3(15, 45, 0)) > 0);

            // The rim wanders in and out of the 10 m circle.
            float min = float.MaxValue, max = float.MinValue;
            for (int k = 0; k < 72; k++)
            {
                double a = k * Math.PI / 36;
                float d = sh.Distance(new Vec3((float)(10 * Math.Cos(a)), 45, (float)(10 * Math.Sin(a))));
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }

            Assert.True(min < -1 && max > 1, $"rim from {min} to {max}");

            // No wall noise at the top (the ground is cut along the smooth shape there), full noise lower down.
            Assert.Equal(0, sh.NoiseMask(new Vec3(10, 45, 0)));
            Assert.Equal(1, sh.NoiseMask(new Vec3(10, 5, 0)));
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
            var prims = RandomPrims(30, 5, roughness: 3f, noiseScale: 15f);
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

        private static List<Primitive> RandomPrims(int count, ulong seed, float roughness = 0f, float noiseScale = 20f)
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
                        prims.Add(new TubeSegment(a, b, up, up, w, w, w * 0.5f, w * 0.5f, 0, ZoneKind.TubeGallery) { Roughness = roughness, NoiseScale = noiseScale });
                        break;
                    case 1:
                        prims.Add(new Chamber(a, up, (float)rng.Range(20, 50), (float)rng.Range(10, 30)) { Roughness = roughness, NoiseScale = noiseScale });
                        break;
                    default:
                        prims.Add(new Shaft(a, up, (float)rng.Range(5, 20), (float)rng.Range(20, 80)) { Roughness = roughness, NoiseScale = noiseScale });
                        break;
                }
            }

            return prims;
        }

        private static Vec3 RandomPoint(Rng rng) =>
            new Vec3((float)rng.Range(-50, 450), (float)rng.Range(-30, 120), (float)rng.Range(-50, 450));
    }
}

namespace Subterra.Core.Tests.Sdf
{
    public class PrimitiveImmutabilityTests
    {
        // Workers read primitives while meshing: nothing may change them after construction.
        [Fact]
        public void PublicSettersAreInitOnly()
        {
            foreach (var property in typeof(Primitive).GetProperties())
            {
                var setter = property.GetSetMethod(nonPublic: true);
                if (setter == null || setter.IsPrivate)
                {
                    continue;
                }

                bool init = Array.Exists(setter.ReturnParameter.GetRequiredCustomModifiers(), m => m.Name == "IsExternalInit");
                Assert.True(init, $"{property.Name} can be set after construction");
            }
        }
    }

    public class MayHaveSurfaceTests
    {
        private static NetworkSdf Tube()
        {
            var prims = new System.Collections.Generic.List<Primitive>
            {
                new TubeSegment(new Vec3(0, 0, 0), new Vec3(400, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 40, 40, 20, 20, 0, ZoneKind.TubeGallery),
            };
            return new NetworkSdf(prims, new SpatialGrid(prims), 1);
        }

        [Fact]
        public void BigBoxAroundTubeHasSurface() => Assert.True(Tube().MayHaveSurface(new Aabb(new Vec3(-128, -128, -128), new Vec3(128, 128, 128)) .Expand(0)));

        [Fact]
        public void BoxFarAwayHasNone() => Assert.False(Tube().MayHaveSurface(new Aabb(new Vec3(0, 200, 0), new Vec3(32, 232, 32))));

        [Fact]
        public void BoxInsideTheVoidHasNone()
        {
            var big = new TubeSegment(new Vec3(0, 0, 0), new Vec3(400, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 300, 300, 150, 150, 0, ZoneKind.TubeGallery);
            var prims = new System.Collections.Generic.List<Primitive> { big };
            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), 1);
            Assert.False(sdf.MayHaveSurface(new Aabb(new Vec3(190, 60, -8), new Vec3(206, 76, 8))));
            Assert.True(sdf.MayHaveSurface(new Aabb(new Vec3(190, -8, -8), new Vec3(206, 8, 8))));
        }
    }
}
