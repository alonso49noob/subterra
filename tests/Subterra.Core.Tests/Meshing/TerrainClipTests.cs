using System;
using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class TerrainClipTests
    {
        // A flat 6 × 6 grid of 30 m squares (two triangles each, faces up), like a terrain quad.
        private static (Vec3[] v, int[] t) Grid()
        {
            const int n = 6;
            var v = new Vec3[(n + 1) * (n + 1)];
            for (int z = 0; z <= n; z++)
            {
                for (int x = 0; x <= n; x++)
                {
                    v[z * (n + 1) + x] = new Vec3(x * 30 - 90, 0, z * 30 - 90);
                }
            }

            var t = new List<int>();
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                    t.AddRange(new[] { a, c, b, b, c, d });
                }
            }

            return (v, t.ToArray());
        }

        private static Vec3 Position(Vec3[] v, List<TerrainClip.Blend> added, int i)
        {
            if (i < v.Length)
            {
                return v[i];
            }

            TerrainClip.Blend b = added[i - v.Length];
            return v[b.A] * b.Wa + v[b.B] * b.Wb + v[b.C] * b.Wc;
        }

        private static float Area(Vec3 a, Vec3 b, Vec3 c) => Vec3.Cross(b - a, c - a).Length * 0.5f;

        [Fact]
        public void CutsARoundHoleWithASmoothEdge()
        {
            (Vec3[] v, int[] t) = Grid();
            var added = new List<TerrainClip.Blend>();
            var hole = new ClipHole(new Vec3(5, 0, -7), new Vec3(0, 1, 0), 25);
            int[] clipped = TerrainClip.Clip(v, t, new[] { hole }, 1.5f, added);

            float area = 0;
            for (int i = 0; i < clipped.Length; i += 3)
            {
                Vec3 a = Position(v, added, clipped[i]), b = Position(v, added, clipped[i + 1]), c = Position(v, added, clipped[i + 2]);
                area += Area(a, b, c);
                // No vertex is left inside the hole, and every triangle still faces up.
                foreach (Vec3 p in new[] { a, b, c })
                {
                    Vec3 d = p - hole.Centre;
                    Assert.True(new Vec3(d.X, 0, d.Z).Length > hole.Radius - 0.01f, $"vertex {p} inside, {new Vec3(d.X, 0, d.Z).Length}");
                }

                Assert.True(Vec3.Cross(b - a, c - a).Y >= -1e-3f, $"flipped {a} {b} {c}");
            }

            float expected = 180f * 180f - (float)(Math.PI * 25 * 25);
            Assert.InRange(area, expected - 40, expected + 40);
        }

        [Fact]
        public void TrianglesAwayFromTheHoleAreUntouched()
        {
            (Vec3[] v, int[] t) = Grid();
            var added = new List<TerrainClip.Blend>();
            int[] clipped = TerrainClip.Clip(v, t, new[] { new ClipHole(new Vec3(-75, 0, -75), new Vec3(0, 1, 0), 8) }, 1.5f, added);
            // The far corner's two triangles are kept as they were.
            int last = t.Length - 6;
            Assert.Equal(new[] { t[last], t[last + 1], t[last + 2] }, new[] { clipped[clipped.Length - 6], clipped[clipped.Length - 5], clipped[clipped.Length - 4] });
            Assert.NotEmpty(added);
        }

        [Fact]
        public void FieldHolesFollowTheirOwnShape()
        {
            (Vec3[] v, int[] t) = Grid();
            var added = new List<TerrainClip.Blend>();
            // A 40 × 10 m slot (a trench): negative inside, roughly metres from its edge.
            Func<Vec3, float> slot = p => Math.Max(Math.Abs(p.X) - 20, Math.Abs(p.Z) - 5);
            var hole = new ClipHole(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 25, slot);
            int[] clipped = TerrainClip.Clip(v, t, new[] { hole }, 1f, added);
            float area = 0;
            for (int i = 0; i < clipped.Length; i += 3)
            {
                Vec3 a = Position(v, added, clipped[i]), b = Position(v, added, clipped[i + 1]), c = Position(v, added, clipped[i + 2]);
                area += Area(a, b, c);
                Vec3 centroid = (a + b + c) / 3f;
                Assert.True(slot(centroid) > -0.6f, $"triangle inside the slot at {centroid}");
            }

            Assert.InRange(area, 180f * 180f - 400 - 40, 180f * 180f - 400 + 40);
        }

        [Fact]
        public void NoHolesNoChange()
        {
            (Vec3[] v, int[] t) = Grid();
            Assert.Same(t, TerrainClip.Clip(v, t, new ClipHole[0], 1.5f, new List<TerrainClip.Blend>()));
        }
    }
}
