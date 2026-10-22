using System.Linq;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class RimBuilderTests
    {
        private const int Segments = 24, Rings = 6;

        private static float[] Heights() => Enumerable.Range(0, Segments * Rings).Select(i => (float)(i % 7) * 0.4f - 1f).ToArray();

        private static MeshData Rim(ulong seed = 3) => RimBuilder.Build(30, 135, Segments, Rings, Heights(), seed);

        [Fact]
        public void OuterRingSitsJustUnderTheTerrain()
        {
            var m = Rim();
            float[] h = Heights();
            for (int s = 0; s < Segments; s++)
            {
                int i = (Rings - 1) * Segments + s;
                Assert.Equal(h[i] - RimBuilder.OuterSink, m.Vertices[i].Y, 5);
            }
        }

        [Fact]
        public void InnerWallHangsFromTheLipFacingThePit()
        {
            var m = Rim();
            int wall = m.Vertices.Count - Segments;
            for (int s = 0; s < Segments; s++)
            {
                Assert.Equal(m.Vertices[s].Y - RimBuilder.InnerWall, m.Vertices[wall + s].Y, 4);
                Vec3 toCentre = new Vec3(-m.Vertices[s].X, 0, -m.Vertices[s].Z);
                Assert.True(Vec3.Dot(m.Normals[wall + s], toCentre) > 0);
            }
        }

        [Fact]
        public void ClippedHoleWallFacesThePit()
        {
            float[] h = Enumerable.Range(0, Segments).Select(i => (float)(i % 3) * 0.2f).ToArray();
            var m = RimBuilder.Wall(30, Segments, h);
            Assert.Equal(2 * Segments, m.Vertices.Count);
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                Vec3 centroid = (a + b + c) / 3f;
                Assert.True(Vec3.Dot(Vec3.Cross(b - a, c - a), new Vec3(-centroid.X, 0, -centroid.Z)) > 0);
            }

            Assert.Equal(h[0] - RimBuilder.WallTopDrop, m.Vertices[0].Y, 4);
        }

        [Fact]
        public void SkirtIsTwoMetresBelowTheOuterRing()
        {
            var m = Rim();
            for (int s = 0; s < Segments; s++)
            {
                Assert.Equal(m.Vertices[(Rings - 1) * Segments + s].Y - 2f, m.Vertices[Rings * Segments + s].Y, 5);
            }
        }

        [Fact]
        public void InnerRingDropsIntoTheShaft()
        {
            var m = Rim();
            float[] h = Heights();
            for (int s = 0; s < Segments; s++)
            {
                Assert.Equal(h[s] - 2f, m.Vertices[s].Y, 5);
            }
        }

        [Fact]
        public void RimFacesUp()
        {
            var m = Rim();
            for (int i = 0; i < m.SkirtTriangleStart; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                Assert.True(Vec3.Cross(b - a, c - a).Y > 0);
            }
        }

        [Fact] public void Deterministic() => Assert.Equal(Rim().Vertices, Rim().Vertices);
    }
}
