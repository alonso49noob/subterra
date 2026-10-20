using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Scatter;
using Xunit;

namespace Subterra.Core.Tests.Scatter
{
    public class ScatterTests
    {
        private static readonly Vec3 Up = new Vec3(0, 1, 0);

        // 100 × 100 m plane of 10 m squares at y = 0 facing +Y (a floor), or -Y (a ceiling).
        private static MeshData Plane(bool ceiling = false)
        {
            var m = new MeshData();
            for (int i = 0; i <= 10; i++)
            {
                for (int j = 0; j <= 10; j++)
                {
                    m.Vertices.Add(new Vec3(i * 10, 0, j * 10));
                    m.Normals.Add(ceiling ? -Up : Up);
                }
            }

            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    int a = i * 11 + j, b = a + 1, c = a + 11, d = c + 1;
                    // (a, b, c): x along i, z along j → cross((0,0,10), (10,0,0)) = +Y.
                    if (ceiling)
                    {
                        m.Triangles.AddRange(new[] { a, c, b, b, c, d });
                    }
                    else
                    {
                        m.Triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
                }
            }

            m.SkirtTriangleStart = m.Triangles.Count;
            return m;
        }

        private static ScatterSpec Spec(ScatterPlacement placement = ScatterPlacement.Floor, double threshold = 0, params ZoneKind[] zones) =>
            new ScatterSpec { Name = "Boulders", Models = { "x" }, Placement = placement, Density = 0.8, Scale = new Range(0.5, 4), AlignToSurface = 0.7,
                NoiseFrequency = threshold > 0 ? 0.02 : 0, NoiseThreshold = threshold, Zones = zones.ToList() };

        private static List<ScatterInstance> Place(MeshData m, ScatterSpec s, ZoneKind zone = ZoneKind.TubeGallery) =>
            ScatterPlacer.Place(m, s, 1234, _ => Up, _ => zone, _ => false);

        [Fact]
        public void Deterministic()
        {
            var a = Place(Plane(), Spec());
            var b = Place(Plane(), Spec());
            Assert.Equal(a.Select(i => i.Position), b.Select(i => i.Position));
        }

        [Fact] public void FloorDensity() => Assert.InRange(Place(Plane(), Spec()).Count, 72, 88);

        [Fact] public void CeilingGetsNoFloorProps() => Assert.Empty(Place(Plane(ceiling: true), Spec()));

        [Fact] public void CeilingProps() => Assert.NotEmpty(Place(Plane(ceiling: true), Spec(ScatterPlacement.Ceiling)));

        [Fact] public void ZonesFilter() => Assert.Empty(Place(Plane(), Spec(ScatterPlacement.Floor, 0, ZoneKind.FrozenVault)));

        [Fact] public void ThresholdOneRemovesAll() => Assert.Empty(Place(Plane(), Spec(threshold: 1)));

        [Fact]
        public void MergeCapsVertices()
        {
            var prop = new MeshData();
            for (int i = 0; i < 1000; i++)
            {
                prop.Vertices.Add(new Vec3(i, 0, 0));
            }

            prop.Triangles.AddRange(new[] { 0, 1, 2 });
            var inst = Enumerable.Range(0, 100).Select(i => new ScatterInstance(new Vec3(i, 0, 0), Up, new Vec3(0, 0, 1), 1)).ToList();
            var merged = ScatterMerger.Merge(prop, inst, out int dropped);
            Assert.True(merged.Vertices.Count <= 65000);
            Assert.Equal(100 - 65, dropped);
        }

        [Fact]
        public void MergeTransformsVertices()
        {
            var prop = new MeshData();
            prop.Vertices.Add(new Vec3(1, 2, 3));
            prop.Triangles.AddRange(new[] { 0, 0, 0 });
            var p = new Vec3(10, 20, 30);
            var s = new ScatterInstance(p, Up, new Vec3(0, 0, 1), 2);
            var merged = ScatterMerger.Merge(prop, new[] { s }, out _);
            Vec3 expected = p + (s.Right * 1 + Up * 2 + new Vec3(0, 0, 1) * 3) * 2;
            Assert.Equal(expected, merged.Vertices[0]);
        }
    }
}
