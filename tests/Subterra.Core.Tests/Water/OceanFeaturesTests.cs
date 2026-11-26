using System;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Geo;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Water;
using Xunit;

namespace Subterra.Core.Tests.Water
{
    public class OceanFeaturesTests
    {
        private const double PolRadius = 44000;

        private static OceanShell Ocean(double towers, double pillars, double radius = 300000, double thickness = 30000) =>
            new OceanShell(new OceanSpec
            {
                CeilingPole = 1000, CeilingEquator = 3000, Thickness = thickness,
                TowerChance = towers, TowerLength = 200, PillarChance = pillars, PillarRadius = 150,
            }, radius);

        // Tower hangs sampled every 10 m over a 1.2 km square around a point.
        private static List<double> Hangs(OceanShell ocean, Vec3d dir, ulong seed)
        {
            dir = dir.Normalized;
            Vec3d east = Vec3d.Cross(new Vec3d(0, 1, 0), dir).Normalized, north = Vec3d.Cross(dir, east);
            var hangs = new List<double>();
            for (double x = -600; x <= 600; x += 10)
            {
                for (double y = -600; y <= 600; y += 10)
                {
                    Vec3d at = (dir + (east * x + north * y) / ocean.BodyRadius).Normalized * ocean.BodyRadius;
                    hangs.Add(OceanFeatures.TowerHang(ocean, at, seed));
                }
            }

            return hangs;
        }

        [Fact]
        public void NoTowersWithoutAChance()
        {
            Assert.All(Hangs(Ocean(0, 0), new Vec3d(0.3, 0.9, 0.1), 5), h => Assert.Equal(0, h));
        }

        [Fact]
        public void TowersHangFromTheCeilingAsDeepAsTheirLength()
        {
            List<double> hangs = Hangs(Ocean(0.5, 0), new Vec3d(0.3, 0.9, 0.1), 5);
            // About half of the 120 m cells hold a tower 15-60 m in radius: some of the square under towers, most not.
            double covered = hangs.Count(h => h > 0) / (double)hangs.Count;
            Assert.InRange(covered, 0.02, 0.5);
            Assert.All(hangs, h => Assert.InRange(h, 0, 200));
            Assert.Contains(hangs, h => h > 60);
        }

        [Fact]
        public void TowersAreTheSameEveryTime()
        {
            OceanShell ocean = Ocean(0.5, 0);
            Assert.Equal(Hangs(ocean, new Vec3d(1, 0.2, 0.4), 9), Hangs(ocean, new Vec3d(1, 0.2, 0.4), 9));
            Assert.NotEqual(Hangs(ocean, new Vec3d(1, 0.2, 0.4), 9), Hangs(ocean, new Vec3d(1, 0.2, 0.4), 10));
        }

        [Fact]
        public void PillarsOnlyWhereTheBodyHasThem()
        {
            Assert.Empty(OceanFeatures.PillarsNear(Ocean(0.5, 0), new Vec3d(0, 1, 0), 5000, 3));
            // A chance of 0.6 per 1.5 km cell: a 5 km reach holds several.
            List<OceanPillar> pillars = OceanFeatures.PillarsNear(Ocean(0, 0.6, PolRadius, 5000), new Vec3d(0, 1, 0), 5000, 3);
            Assert.InRange(pillars.Count, 3, 60);
            Assert.All(pillars, p =>
            {
                Assert.InRange(p.Radius, 150 * 0.4 - 1e-6, 150 + 1e-6);
                Assert.True((p.Direction - new Vec3d(0, 1, 0)).Length * PolRadius <= 5000);
            });
        }

        [Fact]
        public void APillarFlaresAtBothEnds()
        {
            var pillar = new OceanPillar(new Vec3d(0, 1, 0), 100, 4);
            double top = pillar.RadiusAt(0, 0, 5000), middle = pillar.RadiusAt(0.5, 0, 5000), bottom = pillar.RadiusAt(1, 0, 5000);
            Assert.True(top > middle * 1.4 && bottom > middle * 1.4, $"top {top:F0}, middle {middle:F0}, bottom {bottom:F0}");
        }

        [Fact]
        public void ThePatchCarriesPillarsDownToTheFloorFacingOut()
        {
            OceanShell pol = Ocean(0.3, 1, PolRadius, 5000);
            // Centred 100 m off a pillar (they stand about 1.5 km apart).
            Vec3d some = OceanFeatures.PillarsNear(pol, new Vec3d(0.2, 1, 0.1).Normalized, 5000, 11)[0].Direction;
            Vec3d dir = (some + Vec3d.Cross(some, new Vec3d(1, 0, 0)).Normalized * (100 / PolRadius)).Normalized;
            List<OceanPillar> near = OceanFeatures.PillarsNear(pol, dir, 300 * 1.5 + 3 * 150, 11);
            Assert.NotEmpty(near);
            MeshData m = IceCeilingMesher.Build(pol, dir, 300, 8, 11, out Vec3d centre);
            int grid = 76 * 76;
            Assert.True(m.Vertices.Count > grid);

            // The deepest vertex is at the sea floor, 5 km under the ceiling.
            BodyFrame.ToLatLon(dir, out double lat, out _);
            double floor = pol.CeilingRadius(lat) - 5000;
            double deepest = m.Vertices.Skip(grid).Min(v => (centre + Vec3d.FromVec3(v)).Length);
            Assert.InRange(deepest, floor - 300, floor + 300);

            // Pillar faces point away from their axis.
            OceanPillar pillar = near[0];
            int outward = 0, inward = 0;
            for (int t = 0; t < m.Triangles.Count; t += 3)
            {
                if (m.Triangles[t] < grid)
                {
                    continue;
                }

                Vec3 a = m.Vertices[m.Triangles[t]], b = m.Vertices[m.Triangles[t + 1]], c = m.Vertices[m.Triangles[t + 2]];
                Vec3d mid = centre + Vec3d.FromVec3((a + b + c) * (1f / 3));
                Vec3d nearest = near.OrderBy(p => (mid.Normalized - p.Direction).Length).First().Direction;
                Vec3d radial = mid - nearest * Vec3d.Dot(mid, nearest);
                Vec3 normal = Vec3.Cross(b - a, c - a);
                if (Vec3d.Dot(Vec3d.FromVec3(normal), radial) > 0)
                {
                    outward++;
                }
                else
                {
                    inward++;
                }
            }

            Assert.True(outward > inward * 20, $"{outward} out, {inward} in (pillar {pillar.Radius:F0} m)");
        }
    }
}
