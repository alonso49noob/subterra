using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// Naive Surface Nets over one chunk of N³ cells. Corners are sampled from -1 to N+1 on each axis
    /// at <c>origin + i × cellSize</c> (the same positions in neighbouring chunks); the chunk emits the
    /// faces of the sign-changing edges whose lower corner lies in [0, N)³, so neighbours neither
    /// overlap nor leave gaps. Triangles and normals face the void (the camera is inside the cave).
    /// Vertices are in the SDF's frame (network-local).
    /// </summary>
    public static class SurfaceNets
    {
        public const int N = 32;

        // Corner grid: indices -1 .. N+1 → 0 .. N+2.
        private const int Corners = N + 3;

        // Cell grid: cells -1 .. N-1 → 0 .. N, the cells around the emitted edges (lower corners 0 .. N-1).
        private const int Cells = N + 1;

        private static readonly int[,] CubeEdges =
        {
            { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 },
            { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
        };

        public static MeshData Build(ISdf sdf, Vec3 origin, float cellSize, bool skirts) => Build(sdf, origin, cellSize, skirts, true);

        /// <summary>
        /// <paramref name="skipFarBlocks"/> = false evaluates every corner: the exact reference the
        /// skipping must reproduce bit for bit (tests).
        /// </summary>
        internal static MeshData Build(ISdf sdf, Vec3 origin, float cellSize, bool skirts, bool skipFarBlocks)
        {
            var values = new float[Corners * Corners * Corners];
            var known = skipFarBlocks ? FillFarBlocks(sdf, origin, cellSize, values) : new bool[values.Length];
            bool anyVoid = false, anyRock = false;
            for (int x = 0; x < Corners; x++)
            {
                for (int y = 0; y < Corners; y++)
                {
                    for (int z = 0; z < Corners; z++)
                    {
                        int index = CornerIndex(x, y, z);
                        float d = known[index] ? values[index] : sdf.Distance(Corner(origin, cellSize, x, y, z));
                        values[index] = d;
                        if (d < 0)
                        {
                            anyVoid = true;
                        }
                        else
                        {
                            anyRock = true;
                        }
                    }
                }
            }

            var mesh = new MeshData();
            if (!anyVoid || !anyRock)
            {
                return mesh;
            }

            // One vertex per cell crossed by the surface: the mean of its edge crossings.
            var cellVertex = new int[Cells * Cells * Cells];
            var corner = new float[8];
            for (int x = 0; x < Cells; x++)
            {
                for (int y = 0; y < Cells; y++)
                {
                    for (int z = 0; z < Cells; z++)
                    {
                        int mask = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            corner[c] = values[CornerIndex(x + (c & 1), y + (c >> 1 & 1), z + (c >> 2 & 1))];
                            if (corner[c] < 0)
                            {
                                mask |= 1 << c;
                            }
                        }

                        if (mask == 0 || mask == 255)
                        {
                            cellVertex[CellIndex(x, y, z)] = -1;
                            continue;
                        }

                        float sx = 0, sy = 0, sz = 0;
                        int crossings = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int a = CubeEdges[e, 0], b = CubeEdges[e, 1];
                            float da = corner[a], db = corner[b];
                            if (da < 0 == db < 0)
                            {
                                continue;
                            }

                            float t = da / (da - db);
                            sx += (a & 1) + ((b & 1) - (a & 1)) * t;
                            sy += (a >> 1 & 1) + ((b >> 1 & 1) - (a >> 1 & 1)) * t;
                            sz += (a >> 2 & 1) + ((b >> 2 & 1) - (a >> 2 & 1)) * t;
                            crossings++;
                        }

                        var position = new Vec3(
                            origin.X + (x - 1 + sx / crossings) * cellSize,
                            origin.Y + (y - 1 + sy / crossings) * cellSize,
                            origin.Z + (z - 1 + sz / crossings) * cellSize);
                        cellVertex[CellIndex(x, y, z)] = mesh.Vertices.Count;
                        mesh.Vertices.Add(position);
                        mesh.Normals.Add(CellNormal(corner, sx / crossings, sy / crossings, sz / crossings));
                    }
                }
            }

            // Faces: one quad per sign-changing edge with its lower corner in [0, N)³.
            for (int x = 1; x <= N; x++)
            {
                for (int y = 1; y <= N; y++)
                {
                    for (int z = 1; z <= N; z++)
                    {
                        float d0 = values[CornerIndex(x, y, z)];
                        bool inside0 = d0 < 0;
                        // X edge: cells around it differ in y and z.
                        if (inside0 != values[CornerIndex(x + 1, y, z)] < 0)
                        {
                            Quad(mesh, cellVertex, inside0,
                                CellIndex(x, y - 1, z - 1), CellIndex(x, y, z - 1), CellIndex(x, y, z), CellIndex(x, y - 1, z));
                        }

                        // Y edge: cells differ in z and x.
                        if (inside0 != values[CornerIndex(x, y + 1, z)] < 0)
                        {
                            Quad(mesh, cellVertex, inside0,
                                CellIndex(x - 1, y, z - 1), CellIndex(x - 1, y, z), CellIndex(x, y, z), CellIndex(x, y, z - 1));
                        }

                        // Z edge: cells differ in x and y.
                        if (inside0 != values[CornerIndex(x, y, z + 1)] < 0)
                        {
                            Quad(mesh, cellVertex, inside0,
                                CellIndex(x - 1, y - 1, z), CellIndex(x, y - 1, z), CellIndex(x, y, z), CellIndex(x - 1, y, z));
                        }
                    }
                }
            }

            mesh.SkirtTriangleStart = mesh.Triangles.Count;
            if (skirts)
            {
                AddSkirts(mesh, cellSize);
            }

            return mesh;
        }

        // Blocks of 4³ corners whose centre is far from the surface (the field changes by at most about
        // the distance moved) all share its sign, and so do the corners one cell around them: they get the
        // centre value without being evaluated. No crossing can touch them, so the mesh is unchanged.
        // "Far" must be certain: a field that skips primitives by their bounds (IBoundedSdf) only
        // vouches for its sign up to the nearest skipped primitive, not for its full value.
        private static bool[] FillFarBlocks(ISdf sdf, Vec3 origin, float cellSize, float[] values)
        {
            const int block = 4;
            var known = new bool[values.Length];
            var bounded = sdf as IBoundedSdf;
            float halfDiagonal = 0.5f * block * cellSize * 1.7320508f;
            float safe = 2f * halfDiagonal + 2f * cellSize;
            for (int bx = 0; bx < Corners; bx += block)
            {
                for (int by = 0; by < Corners; by += block)
                {
                    for (int bz = 0; bz < Corners; bz += block)
                    {
                        var centre = new Vec3(
                            origin.X + (bx - 1 + 0.5f * (block - 1)) * cellSize,
                            origin.Y + (by - 1 + 0.5f * (block - 1)) * cellSize,
                            origin.Z + (bz - 1 + 0.5f * (block - 1)) * cellSize);
                        float d = bounded != null ? bounded.SignBound(centre) : sdf.Distance(centre);
                        if (Math.Abs(d) <= safe)
                        {
                            continue;
                        }

                        for (int x = bx; x < Math.Min(bx + block, Corners); x++)
                        {
                            for (int y = by; y < Math.Min(by + block, Corners); y++)
                            {
                                for (int z = bz; z < Math.Min(bz + block, Corners); z++)
                                {
                                    int index = CornerIndex(x, y, z);
                                    values[index] = d;
                                    known[index] = true;
                                }
                            }
                        }
                    }
                }
            }

            return known;
        }

        // The quad (a, b, c, d) is wound for an edge whose lower end is rock; flipped when the lower end is void.
        private static void Quad(MeshData mesh, int[] cellVertex, bool lowerIsVoid, int ca, int cb, int cc, int cd)
        {
            int a = cellVertex[ca], b = cellVertex[cb], c = cellVertex[cc], d = cellVertex[cd];
            if (a < 0 || b < 0 || c < 0 || d < 0)
            {
                return;
            }

            if (lowerIsVoid)
            {
                (b, d) = (d, b);
            }

            // Split along the shorter diagonal.
            List<Vec3> v = mesh.Vertices;
            if ((v[a] - v[c]).LengthSquared <= (v[b] - v[d]).LengthSquared)
            {
                mesh.Triangles.AddRange(new[] { a, b, c, a, c, d });
            }
            else
            {
                mesh.Triangles.AddRange(new[] { a, b, d, b, c, d });
            }
        }

        // A strip pushed cellSize into the rock under every open (border) edge, so LOD cracks show rock-coloured skirts, not holes.
        private static void AddSkirts(MeshData mesh, float cellSize)
        {
            var edgeUse = new Dictionary<long, int>();
            List<int> tris = mesh.Triangles;
            int count = tris.Count;
            for (int i = 0; i < count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[i + k], b = tris[i + (k + 1) % 3];
                    long key = EdgeKey(a, b);
                    edgeUse.TryGetValue(key, out int uses);
                    edgeUse[key] = uses + 1;
                }
            }

            var pushed = new Dictionary<int, int>();
            for (int i = 0; i < count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[i + k], b = tris[i + (k + 1) % 3];
                    if (edgeUse[EdgeKey(a, b)] != 1)
                    {
                        continue;
                    }

                    int a2 = Pushed(mesh, pushed, a, cellSize), b2 = Pushed(mesh, pushed, b, cellSize);
                    // Same winding as the triangle that owns the edge (a → b), continued downwards.
                    tris.Add(b);
                    tris.Add(a);
                    tris.Add(a2);
                    tris.Add(b);
                    tris.Add(a2);
                    tris.Add(b2);
                }
            }
        }

        private static int Pushed(MeshData mesh, Dictionary<int, int> pushed, int index, float cellSize)
        {
            if (!pushed.TryGetValue(index, out int copy))
            {
                copy = mesh.Vertices.Count;
                Vec3 n = mesh.Normals[index];
                mesh.Vertices.Add(mesh.Vertices[index] - n * cellSize);
                mesh.Normals.Add(n);
                pushed[index] = copy;
            }

            return copy;
        }

        private static long EdgeKey(int a, int b) => a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;

        // -∇d inside the cell, trilinear from its 8 corner values at the vertex (fx, fy, fz in [0, 1]):
        // no extra field evaluations (the field is the costly part of meshing).
        private static Vec3 CellNormal(float[] c, float fx, float fy, float fz)
        {
            float gx = Lerp2(c[1] - c[0], c[3] - c[2], c[5] - c[4], c[7] - c[6], fy, fz);
            float gy = Lerp2(c[2] - c[0], c[3] - c[1], c[6] - c[4], c[7] - c[5], fx, fz);
            float gz = Lerp2(c[4] - c[0], c[5] - c[1], c[6] - c[2], c[7] - c[3], fx, fy);
            Vec3 n = -new Vec3(gx, gy, gz);
            return n.LengthSquared > 0 ? n.Normalized : new Vec3(0, 1, 0);
        }

        // Bilinear blend of four values laid out as (00, 10, 01, 11) over (s, t).
        private static float Lerp2(float v00, float v10, float v01, float v11, float s, float t) =>
            (v00 * (1 - s) + v10 * s) * (1 - t) + (v01 * (1 - s) + v11 * s) * t;

        private static Vec3 Corner(Vec3 origin, float cellSize, int x, int y, int z) =>
            new Vec3(origin.X + (x - 1) * cellSize, origin.Y + (y - 1) * cellSize, origin.Z + (z - 1) * cellSize);

        private static int CornerIndex(int x, int y, int z) => (x * Corners + y) * Corners + z;

        private static int CellIndex(int x, int y, int z) => (x * Cells + y) * Cells + z;
    }
}
