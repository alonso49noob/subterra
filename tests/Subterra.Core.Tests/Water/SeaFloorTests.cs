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
            MeshData whole = SeaFloorMesher.Build(Vall, dir, 100, 8, 4, out Vec3d centre);
            MeshData open = SeaFloorMesher.Build(Vall, dir, 100, 8, 4, out _, p => (p.Normalized - centre.Normalized).Length * centre.Length < 20);
            Assert.True(open.Triangles.Count < whole.Triangles.Count);
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
