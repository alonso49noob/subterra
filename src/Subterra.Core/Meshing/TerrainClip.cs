using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// A hole cut into a terrain mesh, in the mesh's coordinates: round (centre, local up, radius), or any shape given
    /// by <see cref="Field"/> (negative inside, roughly metres from its edge), with Radius bounding it.
    /// </summary>
    public readonly struct ClipHole
    {
        public readonly Vec3 Centre;
        public readonly Vec3 Up;
        public readonly float Radius;
        public readonly Func<Vec3, float> Field;

        public ClipHole(Vec3 centre, Vec3 up, float radius, Func<Vec3, float> field = null)
        {
            Centre = centre;
            Up = up.Normalized;
            Radius = radius;
            Field = field;
        }

        /// <summary>Negative inside the hole.</summary>
        public float Inside(Vec3 p)
        {
            if (Field != null)
            {
                return Field(p);
            }

            Vec3 d = p - Centre;
            d -= Up * Vec3.Dot(d, Up);
            return d.Length - Radius;
        }
    }

    /// <summary>
    /// Cuts round holes into a terrain mesh with a smooth edge: triangles wholly inside a hole are dropped, the ones
    /// its edge crosses are split into a fine grid whose inner corners are moved out onto the circle (seen along the
    /// hole's up), and the rest are kept. The terrain stays terrain (its own material, colours and collider) up to the
    /// hole. New vertices are given as barycentric blends of a parent triangle's corners, so every vertex attribute
    /// (normals, colours, UVs) can be interpolated the same way.
    /// </summary>
    public static class TerrainClip
    {
        /// <summary>A new vertex: weights of the corners (A, B, C) of its parent triangle (they may leave [0, 1] a little).</summary>
        public readonly struct Blend
        {
            public readonly int A, B, C;
            public readonly float Wa, Wb, Wc;

            public Blend(int a, int b, int c, float wa, float wb, float wc)
            {
                A = a;
                B = b;
                C = c;
                Wa = wa;
                Wb = wb;
                Wc = wc;
            }
        }

        /// <summary>
        /// The clipped triangle list; new vertices (indices from <c>vertices.Length</c> on) are appended to
        /// <paramref name="added"/>. <paramref name="cell"/> is the target size (m) of the grid on crossed triangles.
        /// </summary>
        public static int[] Clip(Vec3[] vertices, int[] triangles, IList<ClipHole> holes, float cell, List<Blend> added)
        {
            if (holes.Count == 0)
            {
                return triangles;
            }

            var result = new List<int>(triangles.Length);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vec3 pa = vertices[a], pb = vertices[b], pc = vertices[c];
                bool touched = false, allInsideOne = false;
                foreach (ClipHole h in holes)
                {
                    float da = Dist(pa, h), db = Dist(pb, h), dc = Dist(pc, h);
                    if (h.Field == null && da < h.Radius && db < h.Radius && dc < h.Radius)
                    {
                        allInsideOne = true;
                        break;
                    }

                    bool near = da < h.Radius || db < h.Radius || dc < h.Radius || EdgeNear(pa, pb, h) || EdgeNear(pb, pc, h) || EdgeNear(pc, pa, h) || CentreInside(pa, pb, pc, h);
                    // A shaped hole only touches the triangles its field reaches (sampled), not all of its bound.
                    if (near && (h.Field == null || FieldReaches(pa, pb, pc, h)))
                    {
                        touched = true;
                    }
                }

                if (allInsideOne)
                {
                    continue;
                }

                if (!touched)
                {
                    result.Add(a);
                    result.Add(b);
                    result.Add(c);
                    continue;
                }

                Subdivide(vertices.Length, a, b, c, pa, pb, pc, holes, cell, added, result);
            }

            return result.ToArray();
        }

        // The grid on one crossed triangle: (n + 1)(n + 2)/2 points in barycentric steps of 1/n.
        private static void Subdivide(int baseCount, int a, int b, int c, Vec3 pa, Vec3 pb, Vec3 pc, IList<ClipHole> holes, float cell,
            List<Blend> added, List<int> result)
        {
            float longest = Math.Max((pb - pa).Length, Math.Max((pc - pb).Length, (pa - pc).Length));
            int n = Math.Max(2, Math.Min(64, (int)Math.Ceiling(longest / Math.Max(0.1f, cell))));
            int Index(int row, int col) => row * (row + 1) / 2 + col;
            int count = (n + 1) * (n + 2) / 2;
            var weights = new (float wa, float wb, float wc)[count];
            var inside = new int[count];
            for (int row = 0; row <= n; row++)
            {
                for (int col = 0; col <= row; col++)
                {
                    // Row r goes from A (r = 0) towards the edge B–C (r = n); col runs from B's side to C's.
                    float wa = 1f - (float)row / n, wb = (float)(row - col) / n, wc = (float)col / n;
                    int k = Index(row, col);
                    weights[k] = (wa, wb, wc);
                    Vec3 p = pa * wa + pb * wb + pc * wc;
                    inside[k] = -1;
                    for (int h = 0; h < holes.Count; h++)
                    {
                        if (holes[h].Inside(p) < 0)
                        {
                            inside[k] = h;
                            break;
                        }
                    }
                }
            }

            var map = new int[count];
            for (int k = 0; k < count; k++)
            {
                map[k] = -1;
            }

            int Vertex(int k)
            {
                if (map[k] >= 0)
                {
                    return map[k];
                }

                (float wa, float wb, float wc) = weights[k];
                if (inside[k] >= 0)
                {
                    // Out onto the hole's edge, along the hole's plane, staying in the parent triangle's plane.
                    ClipHole h = holes[inside[k]];
                    Vec3 p = pa * wa + pb * wb + pc * wc;
                    Vec3 target = OntoEdge(p, h);
                    (wa, wb, wc) = Barycentric(target, pa, pb, pc, h.Up);
                }

                // Corners of the parent triangle that stay where they are keep their own index.
                if (inside[k] < 0 && wa == 1f && wb == 0 && wc == 0)
                {
                    return map[k] = a;
                }

                if (inside[k] < 0 && wb == 1f && wa == 0 && wc == 0)
                {
                    return map[k] = b;
                }

                if (inside[k] < 0 && wc == 1f && wa == 0 && wb == 0)
                {
                    return map[k] = c;
                }

                added.Add(new Blend(a, b, c, wa, wb, wc));
                return map[k] = baseCount + added.Count - 1;
            }

            void Emit(int k0, int k1, int k2)
            {
                // Dropped if every corner is inside a hole; kept (inner corners moved onto the edge) otherwise.
                if (inside[k0] >= 0 && inside[k1] >= 0 && inside[k2] >= 0)
                {
                    return;
                }

                result.Add(Vertex(k0));
                result.Add(Vertex(k1));
                result.Add(Vertex(k2));
            }

            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col <= row; col++)
                {
                    // Same winding as (A, B, C): A towards the B–C edge, B's side before C's.
                    Emit(Index(row, col), Index(row + 1, col), Index(row + 1, col + 1));
                    if (col < row)
                    {
                        Emit(Index(row, col), Index(row + 1, col + 1), Index(row, col + 1));
                    }
                }
            }
        }

        // The field is under one grid cell somewhere on the triangle (8 × 8 samples).
        private static bool FieldReaches(Vec3 pa, Vec3 pb, Vec3 pc, ClipHole h)
        {
            const int n = 8;
            float margin = Math.Max((pb - pa).Length, Math.Max((pc - pb).Length, (pa - pc).Length)) / n;
            for (int row = 0; row <= n; row++)
            {
                for (int col = 0; col <= row; col++)
                {
                    float wa = 1f - (float)row / n, wb = (float)(row - col) / n, wc = (float)col / n;
                    if (h.Field(pa * wa + pb * wb + pc * wc) < margin)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // The nearest point of the hole's edge (seen along its up): exact for a round hole, Newton steps on the field
        // (gradient across the plane) otherwise.
        private static Vec3 OntoEdge(Vec3 p, ClipHole h)
        {
            if (h.Field == null)
            {
                Vec3 flat = p - h.Centre;
                flat -= h.Up * Vec3.Dot(flat, h.Up);
                float len = flat.Length;
                return len > 1e-4f ? h.Centre + flat * (h.Radius / len) : p;
            }

            Vec3 e1 = Vec3.Cross(h.Up, Math.Abs(h.Up.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized;
            Vec3 e2 = Vec3.Cross(h.Up, e1);
            Vec3 q = p;
            for (int i = 0; i < 6; i++)
            {
                float d = h.Field(q);
                if (Math.Abs(d) < 0.02f)
                {
                    break;
                }

                const float step = 0.25f;
                float gx = (h.Field(q + e1 * step) - h.Field(q - e1 * step)) / (2 * step);
                float gy = (h.Field(q + e2 * step) - h.Field(q - e2 * step)) / (2 * step);
                float g2 = gx * gx + gy * gy;
                if (g2 < 1e-6f)
                {
                    break;
                }

                // At most 3 m per step (the field is only roughly a distance).
                float scale = Math.Max(-3f, Math.Min(3f, d / g2 * (float)Math.Sqrt(g2))) / (float)Math.Sqrt(g2);
                q -= (e1 * gx + e2 * gy) * scale;
            }

            return q;
        }

        // Barycentric weights of p (projected along up onto the triangle's plane).
        private static (float, float, float) Barycentric(Vec3 p, Vec3 a, Vec3 b, Vec3 c, Vec3 up)
        {
            Vec3 e1 = Vec3.Cross(up, Math.Abs(up.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized;
            Vec3 e2 = Vec3.Cross(up, e1);
            float ax = Vec3.Dot(a, e1), ay = Vec3.Dot(a, e2), bx = Vec3.Dot(b, e1), by = Vec3.Dot(b, e2);
            float cx = Vec3.Dot(c, e1), cy = Vec3.Dot(c, e2), px = Vec3.Dot(p, e1), py = Vec3.Dot(p, e2);
            float det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(det) < 1e-9f)
            {
                return (1f / 3, 1f / 3, 1f / 3);
            }

            float wa = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / det;
            float wb = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / det;
            return (wa, wb, 1 - wa - wb);
        }

        // Distance from the hole's axis (seen along its up).
        private static float Dist(Vec3 p, ClipHole h)
        {
            Vec3 d = p - h.Centre;
            d -= h.Up * Vec3.Dot(d, h.Up);
            return d.Length;
        }

        private static bool EdgeNear(Vec3 p, Vec3 q, ClipHole h)
        {
            Vec3 a = p - h.Centre, b = q - h.Centre;
            a -= h.Up * Vec3.Dot(a, h.Up);
            b -= h.Up * Vec3.Dot(b, h.Up);
            Vec3 ab = b - a;
            float t = ab.LengthSquared > 0 ? Math.Max(0, Math.Min(1, -Vec3.Dot(a, ab) / ab.LengthSquared)) : 0;
            return (a + ab * t).Length < h.Radius;
        }

        private static bool CentreInside(Vec3 pa, Vec3 pb, Vec3 pc, ClipHole h)
        {
            (float wa, float wb, float wc) = Barycentric(h.Centre, pa, pb, pc, h.Up);
            return wa >= 0 && wb >= 0 && wc >= 0;
        }
    }
}
