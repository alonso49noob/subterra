using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    internal sealed class SphereVoid : ISdf
    {
        private readonly Vec3 c;
        private readonly float r;

        public SphereVoid(Vec3 c, float r)
        {
            this.c = c;
            this.r = r;
        }

        public float Distance(Vec3 p) => (p - c).Length - r;
    }

    internal sealed class Const : ISdf
    {
        private readonly float value;

        public Const(float value) => this.value = value;

        public float Distance(Vec3 p) => value;
    }

    // Void below y = h (a floor seen from below is irrelevant: just a planar surface).
    internal sealed class HalfSpace : ISdf
    {
        private readonly float h;

        public HalfSpace(float h) => this.h = h;

        public float Distance(Vec3 p) => p.Y - h;
    }

    internal sealed class PrimitiveSdf : ISdf
    {
        private readonly Primitive prim;

        public PrimitiveSdf(Primitive prim) => this.prim = prim;

        public float Distance(Vec3 p) => prim.Distance(p);
    }

    public class SurfaceNetsTests
    {
        private static readonly Vec3 O = new Vec3(0, 0, 0);
        private static readonly Vec3 C = new Vec3(16, 16, 16);

        private static IEnumerable<(Vec3 Normal, Vec3 Centroid)> Tris(MeshData m, bool skirts = false)
        {
            int end = skirts ? m.Triangles.Count : m.SkirtTriangleStart;
            for (int i = 0; i < end; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                yield return (Vec3.Cross(b - a, c - a), (a + b + c) / 3f);
            }
        }

        [Fact]
        public void SphereVoidClosed()
        {
            var m = SurfaceNets.Build(new SphereVoid(C, 10), O, 1f, false);
            Assert.NotEmpty(m.Triangles);
            var uses = new Dictionary<(int, int), int>();
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = m.Triangles[i + k], b = m.Triangles[i + (k + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    uses.TryGetValue(key, out int n);
                    uses[key] = n + 1;
                }
            }

            Assert.All(uses.Values, n => Assert.Equal(2, n));
        }

        [Fact]
        public void TrianglesFaceTheVoid()
        {
            var m = SurfaceNets.Build(new SphereVoid(C, 10), O, 1f, false);
            foreach (var t in Tris(m))
            {
                Assert.True(Vec3.Dot(t.Normal, C - t.Centroid) > 0);
            }
        }

        [Fact]
        public void NormalsPointIntoTheVoid()
        {
            var m = SurfaceNets.Build(new SphereVoid(C, 10), O, 1f, false);
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Assert.True(Vec3.Dot(m.Normals[i], C - m.Vertices[i]) > 0);
            }
        }

        [Fact]
        public void NeighborsShareBorderVertices()
        {
            // A tilted plane crossing both chunks.
            var plane = new TiltedPlane();
            var a = SurfaceNets.Build(plane, O, 1f, false);
            var b = SurfaceNets.Build(plane, O + new Vec3(32, 0, 0), 1f, false);
            var nearA = a.Vertices.Where(v => Math.Abs(v.X - 32) < 1.5f).ToList();
            var nearB = b.Vertices.Where(v => Math.Abs(v.X - 32) < 1.5f).ToList();
            Assert.NotEmpty(nearA);
            foreach (Vec3 v in nearA.Where(v => v.X > 31 && v.X < 33 && v.Y > 1 && v.Y < 31 && v.Z > 1 && v.Z < 31))
            {
                Assert.Contains(nearB, w => (w - v).Length < 1e-4f);
            }
        }

        [Fact]
        public void NoVerticesForTheCellLayerPastTheChunk()
        {
            // Cells N (corners N .. N+1) belong to the next chunk: no emitted face uses them.
            var m = SurfaceNets.Build(new TiltedPlane(), O, 1f, false);
            Assert.Equal(m.Vertices.Count, m.Normals.Count);
            Assert.All(m.Vertices, v => Assert.True(v.X <= SurfaceNets.N && v.Y <= SurfaceNets.N && v.Z <= SurfaceNets.N, $"{v}"));
            int used = new HashSet<int>(m.Triangles).Count;
            // Only border cells crossed solely through their outer faces stay unused.
            Assert.True(used >= 0.98 * m.Vertices.Count, $"{used} of {m.Vertices.Count} vertices used");
        }

        [Fact] public void AllRockIsEmpty() => Assert.Empty(SurfaceNets.Build(new Const(5), O, 1f, true).Triangles);

        [Fact]
        public void SkirtsOnBoundary()
        {
            var tube = new TubeSegment(new Vec3(-20, 8, 16), new Vec3(60, 8, 16), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 20, 20, 12, 12, 0, ZoneKind.TubeGallery);
            var m = SurfaceNets.Build(new PrimitiveSdf(tube), O, 1f, true);
            Assert.True(m.SkirtTriangleStart < m.Triangles.Count);
            for (int i = m.SkirtTriangleStart; i < m.Triangles.Count; i++)
            {
                Vec3 v = m.Vertices[m.Triangles[i]];
                float nearest = Enumerable.Range(0, m.Vertices.Count).Where(j => j != m.Triangles[i]).Min(j => (m.Vertices[j] - v).Length);
                Assert.True(nearest <= 1f + 1e-4f);
            }
        }

        [Fact, Trait("Category", "Perf")]
        public void TubeChunkUnder30ms()
        {
            var tube = new TubeSegment(new Vec3(-34, 4, 16), new Vec3(66, 4, 16), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 24, 24, 14, 14, 0.2f, ZoneKind.TubeGallery)
            {
                Roughness = 1.2f,
                NoiseScale = 7f,
            };
            var prims = new List<Primitive> { tube };
            var sdf = new NetworkSdf(prims, new SpatialGrid(prims), 3);
            ISdf region = sdf.ForRegion(new Aabb(O, new Vec3(32, 32, 32)));
            SurfaceNets.Build(region, O, 1f, true);
            var times = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                SurfaceNets.Build(region, O, 1f, true);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }

            times.Sort();
            Assert.True(times[5] < 30, $"median {times[5]:F1} ms");
        }

        // Skipping far blocks must not change the mesh: compare with every corner evaluated.
        private static int AssertSameAsExact(NetworkSdf sdf, Vec3 origin, float cellSize, string context)
        {
            float size = SurfaceNets.N * cellSize;
            ISdf region = sdf.ForRegion(new Aabb(origin, origin + new Vec3(size, size, size)));
            MeshData fast = SurfaceNets.Build(region, origin, cellSize, false);
            MeshData exact = SurfaceNets.Build(region, origin, cellSize, false, skipFarBlocks: false);
            Assert.True(exact.Triangles.Count == fast.Triangles.Count,
                $"{context}: {exact.Triangles.Count / 3} triangles exact, {fast.Triangles.Count / 3} with far blocks skipped");
            Assert.Equal(exact.Triangles, fast.Triangles);
            Assert.Equal(exact.Vertices, fast.Vertices);
            return exact.Triangles.Count / 3;
        }

        [Fact]
        public void FarBlocksKeepTubeAtCoarseLods()
        {
            int total = 0;
            for (int offset = 0; offset < 32; offset++)
            {
                // The reviewer's case: a 120 × 40 m tube in its own network, met by LOD3 chunks.
                var tube = new TubeSegment(new Vec3(-100, offset, offset), new Vec3(500, offset, offset), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 120, 120, 40, 40, 0, ZoneKind.TubeGallery);
                var prims = new List<Primitive> { tube };
                var sdf = new NetworkSdf(prims, new SpatialGrid(prims), 1);
                foreach (float cellSize in new[] { 8f, 4f })
                {
                    float size = SurfaceNets.N * cellSize;
                    for (int y = -1; y <= 0; y++)
                    {
                        for (int z = -1; z <= 0; z++)
                        {
                            total += AssertSameAsExact(sdf, new Vec3(0, y * size, z * size), cellSize, $"offset {offset} cell {cellSize} chunk ({y}, {z})");
                        }
                    }
                }
            }

            Assert.True(total > 0);
        }

        [Fact]
        public void FarBlocksKeepPrimitivesOutsideTheirNeighboursBounds()
        {
            // A diagonal tube has a huge box whose corners are far from its void; a small chamber sits
            // in such a corner. Blocks there see only the tube (the chamber's bounds exclude them).
            int total = 0;
            for (int offset = 0; offset < 32; offset += 3)
            {
                var o = new Vec3(offset, 0, offset);
                var tube = new TubeSegment(o, o + new Vec3(400, 0, 400), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 30, 30, 15, 15, 0, ZoneKind.TubeGallery);
                var chamber = new Chamber(o + new Vec3(380, 0, 20), new Vec3(0, 1, 0), 20, 12);
                var prims = new List<Primitive> { tube, chamber };
                var sdf = new NetworkSdf(prims, new SpatialGrid(prims), 2);
                foreach (float cellSize in new[] { 8f, 4f, 2f })
                {
                    float size = SurfaceNets.N * cellSize;
                    var centre = o + new Vec3(380, 0, 20);
                    var first = new Vec3((float)Math.Floor((centre.X - 40) / size), (float)Math.Floor(-40 / size), (float)Math.Floor((centre.Z - 40) / size));
                    var last = new Vec3((float)Math.Floor((centre.X + 40) / size), (float)Math.Floor(40 / size), (float)Math.Floor((centre.Z + 40) / size));
                    for (float x = first.X; x <= last.X; x++)
                    {
                        for (float y = first.Y; y <= last.Y; y++)
                        {
                            for (float z = first.Z; z <= last.Z; z++)
                            {
                                total += AssertSameAsExact(sdf, new Vec3(x * size, y * size, z * size), cellSize, $"offset {offset} cell {cellSize} chunk ({x}, {y}, {z})");
                            }
                        }
                    }
                }
            }

            Assert.True(total > 0);
        }

        private sealed class TiltedPlane : ISdf
        {
            public float Distance(Vec3 p) => p.Y - 10 - 0.3f * p.X - 0.2f * p.Z;
        }
    }
}
