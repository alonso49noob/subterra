using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class UvProjectorTests
    {
        // Unit-ish cube (0..2) with outward faces, 8 vertices, 12 triangles.
        private static MeshData Cube()
        {
            var m = new MeshData();
            for (int i = 0; i < 8; i++)
            {
                m.Vertices.Add(new Vec3((i & 1) * 2, (i >> 1 & 1) * 2, (i >> 2 & 1) * 2));
                m.Normals.Add(new Vec3(0, 1, 0));
            }

            int[] faces =
            {
                0, 2, 3, 1, // z = 0
                4, 5, 7, 6, // z = 2
                0, 1, 5, 4, // y = 0
                2, 6, 7, 3, // y = 2
                0, 4, 6, 2, // x = 0
                1, 3, 7, 5, // x = 2
            };
            for (int f = 0; f < 6; f++)
            {
                int a = faces[f * 4], b = faces[f * 4 + 1], c = faces[f * 4 + 2], d = faces[f * 4 + 3];
                m.Triangles.AddRange(new[] { a, b, c, a, c, d });
            }

            m.SkirtTriangleStart = m.Triangles.Count;
            return m;
        }

        [Fact]
        public void CubeGets24Vertices()
        {
            var p = UvProjector.Project(Cube(), 2f);
            Assert.Equal(24, p.Vertices.Count);
            Assert.Equal(36, p.Triangles.Count);
        }

        [Fact]
        public void UvsAreNonDominantCoordinates()
        {
            var p = UvProjector.Project(Cube(), 2f);
            for (int i = 0; i < p.Triangles.Count; i += 3)
            {
                Vec3 a = p.Vertices[p.Triangles[i]], b = p.Vertices[p.Triangles[i + 1]], c = p.Vertices[p.Triangles[i + 2]];
                Vec3 n = Vec3.Cross(b - a, c - a);
                for (int k = 0; k < 3; k++)
                {
                    Vec3 v = p.Vertices[p.Triangles[i + k]];
                    Vec2 uv = p.Uvs[p.Triangles[i + k]];
                    Vec2 expected = n.X != 0 ? new Vec2(v.Y / 2, v.Z / 2) : n.Y != 0 ? new Vec2(v.X / 2, v.Z / 2) : new Vec2(v.X / 2, v.Y / 2);
                    Assert.Equal(expected, uv);
                }
            }
        }

        [Fact]
        public void OrientationKept()
        {
            var m = Cube();
            var p = UvProjector.Project(m, 1f);
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 n0 = Vec3.Cross(m.Vertices[m.Triangles[i + 1]] - m.Vertices[m.Triangles[i]], m.Vertices[m.Triangles[i + 2]] - m.Vertices[m.Triangles[i]]);
                Vec3 n1 = Vec3.Cross(p.Vertices[p.Triangles[i + 1]] - p.Vertices[p.Triangles[i]], p.Vertices[p.Triangles[i + 2]] - p.Vertices[p.Triangles[i]]);
                Assert.Equal(n0, n1);
            }
        }
    }
}
