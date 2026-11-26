using System.Linq;
using Subterra.Core.Geo;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Water;
using Xunit;

namespace Subterra.Core.Tests.Water
{
    public class SeaFloorTests
    {
        private static readonly OceanShell Vall = new OceanShell(new OceanSpec { CeilingPole = 1000, CeilingEquator = 10000, Thickness = 30000 }, 300000);

        [Fact]
        public void TheFloorLiesUnderTheWaterAndItsHillsOnlyRise()
        {
            Vec3d dir = new Vec3d(0.3, 0.9, 0.2).Normalized;
            MeshData m = SeaFloorMesher.Build(Vall, dir, 300, 8, 4, out Vec3d centre);
            Assert.False(m.IsEmpty);
            foreach (Vec3 v in m.Vertices)
            {
                Vec3d p = centre + Vec3d.FromVec3(v);
                BodyFrame.ToLatLon(p.Normalized, out double lat, out _);
                double over = p.Length - Vall.FloorRadius(lat);
                Assert.InRange(over, -0.01, SeaFloorMesher.Relief + 0.01);
            }

            // Some relief, not a flat plate.
            Assert.True(m.Vertices.Max(v => (centre + Vec3d.FromVec3(v)).Length) - m.Vertices.Min(v => (centre + Vec3d.FromVec3(v)).Length) > 5);
        }

        [Fact]
        public void ItsFacesLookUpIntoTheWater()
        {
            Vec3d dir = new Vec3d(0.3, 0.9, 0.2).Normalized;
            MeshData m = SeaFloorMesher.Build(Vall, dir, 100, 8, 4, out Vec3d centre);
            for (int t = 0; t < m.Triangles.Count; t += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[t]], b = m.Vertices[m.Triangles[t + 1]], c = m.Vertices[m.Triangles[t + 2]];
                Vec3 n = Vec3.Cross(b - a, c - a);
                Vec3d up = (centre + Vec3d.FromVec3((a + b + c) * (1f / 3))).Normalized;
                Assert.True(Vec3d.Dot(Vec3d.FromVec3(n), up) > 0);
            }
        }

        [Fact]
        public void ItOpensWhereACaveGoesThrough()
        {
            Vec3d dir = new Vec3d(0.3, 0.9, 0.2).Normalized;
            SeaFloorMesher.Build(Vall, dir, 100, 8, 4, out Vec3d centre);
            double Off(Vec3 local) => ((centre + Vec3d.FromVec3(local)).Normalized - centre.Normalized).Length * centre.Length;
            MeshData open = SeaFloorMesher.Build(Vall, dir, 100, 8, 4, out _, p => (p.Normalized - centre.Normalized).Length * centre.Length < 20);
            // Nothing over the hole, and the floor comes close to its round edge (pieces of 2 m, not whole 8 m cells: Lucas saw an
            // L-shaped black hole round a round shaft).
            double nearest = double.MaxValue;
            for (int i = 0; i < open.Triangles.Count; i += 3)
            {
                Vec3 a = open.Vertices[open.Triangles[i]], b = open.Vertices[open.Triangles[i + 1]], c = open.Vertices[open.Triangles[i + 2]];
                double off = Off((a + b + c) / 3f);
                Assert.True(off >= 19, $"a triangle {off:F1} m from the middle of a 20 m hole");
                nearest = System.Math.Min(nearest, off);
            }

            Assert.True(nearest < 22.5, $"the floor stops {nearest:F1} m from the middle of a 20 m hole");
        }

        [Fact]
        public void TheWaterReachesTheSmoothFloor()
        {
            BodyFrame.ToLatLon(new Vec3d(0.3, 0.9, 0.2).Normalized, out double lat, out _);
            Vec3d dir = new Vec3d(0.3, 0.9, 0.2).Normalized;
            Assert.NotNull(Vall.At(dir * (Vall.FloorRadius(lat) + 1)));
            Assert.Null(Vall.At(dir * (Vall.FloorRadius(lat) - 1)));
        }
    }
}
