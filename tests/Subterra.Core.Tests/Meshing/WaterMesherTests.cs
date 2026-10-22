using System;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class WaterMesherTests
    {
        // A box-shaped cave 20 m × 10 m × 40 m around the origin.
        private sealed class Box : ISdf
        {
            public float Distance(Vec3 p)
            {
                float dx = Math.Abs(p.X) - 10, dy = Math.Abs(p.Y) - 5, dz = Math.Abs(p.Z) - 20;
                float outside = new Vec3(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length;
                return outside + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0);
            }
        }

        private static float Area(MeshData m)
        {
            float area = 0;
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                area += Vec3.Cross(b - a, c - a).Length / 2;
            }

            return area;
        }

        [Fact]
        public void FillsTheCaveAtItsLevelUpToTheWalls()
        {
            MeshData m = WaterMesher.Build(new Box(), new WaterSurface(new Vec3(0, -2, 0), new Vec3(0, 1, 0), 100, null));
            Assert.False(m.IsEmpty);
            Assert.All(m.Vertices, v => Assert.Equal(-2, v.Y, 3));
            Assert.All(m.Vertices, v => Assert.True(Math.Abs(v.X) <= 10.5f && Math.Abs(v.Z) <= 20.5f));

            // The box's section plus the strip that runs into the rock (0.4 m all round); the grid's straight cuts lose a little at the corners.
            float expected = (20 + 2 * WaterMesher.IntoRock) * (40 + 2 * WaterMesher.IntoRock);
            Assert.InRange(Area(m), expected * 0.98f, expected * 1.001f);
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                Assert.True(Vec3.Cross(b - a, c - a).Y > 0, "faces up");
            }
        }

        [Fact]
        public void StopsAtItsRadiusAndIsDryAboveTheRoof()
        {
            MeshData disc = WaterMesher.Build(new Box(), new WaterSurface(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 6, null));
            Assert.All(disc.Vertices, v => Assert.True(new Vec3(v.X, 0, v.Z).Length <= 6.01f));
            Assert.InRange(Area(disc), Math.PI * 36 * 0.97, Math.PI * 36 * 1.001);

            Assert.True(WaterMesher.Build(new Box(), new WaterSurface(new Vec3(0, 8, 0), new Vec3(0, 1, 0), 50, null)).IsEmpty);
        }
    }
}
