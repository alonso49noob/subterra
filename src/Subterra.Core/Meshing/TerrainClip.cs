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

        // A crossed triangle refined only where the hole's edge passes: a sub-triangle well outside every hole is kept
        // whole, one well inside is dropped, and one near an edge is split in four until it is no bigger than `cell`;
        // at that size its inner corners are moved out onto the edge. Points are shared by their barycentric weights.
        private static void Subdivide(int baseCount, int a, int b, int c, Vec3 pa, Vec3 pb, Vec3 pc, IList<ClipHole> holes, float cell,
            List<Blend> added, List<int> result)
        {
            var points = new Dictionary<long, int>();
            var depthOf = new Dictionary<long, (float d, int hole)>();
            const float Q = 1 << 20;

            long Key(float wb, float wc) => ((long)Math.Round(wb * Q) << 32) ^ (uint)(long)Math.Round(wc * Q);

            Vec3 At(float wb, float wc) => pa * (1 - wb - wc) + pb * wb + pc * wc;

            // Signed distance to the nearest hole's edge at a point (negative inside a hole), and that hole.
            (float d, int hole) Depth(float wb, float wc)
            {
                long key = Key(wb, wc);
                if (depthOf.TryGetValue(key, out var known))
                {
                    return known;
                }

                Vec3 p = At(wb, wc);
                float best = float.MaxValue;
                int which = -1;
                for (int h = 0; h < holes.Count; h++)
                {
                    float d = holes[h].Inside(p);
                    if (d < best)
                    {
                        best = d;
                        which = h;
                    }
                }

                return depthOf[key] = (best, which);
            }

            var placed = new Dictionary<int, Vec3>();

            int Vertex(float wb, float wc, bool onEdge)
            {
                float wa = 1 - wb - wc;
                (float d, int hole) = Depth(wb, wc);
                bool move = onEdge && d < 0;
                long key = Key(wb, wc) * 2 + (move ? 1 : 0);
                if (points.TryGetValue(key, out int index))
                {
                    return index;
                }

                if (move)
                {
                    // Out onto the hole's edge, along the hole's plane, staying in the parent triangle's plane.
                    ClipHole h = holes[hole];
                    (wa, wb, wc) = Barycentric(OntoEdge(At(wb, wc), h), pa, pb, pc, h.Up);
                }

                // Corners of the parent triangle that stay where they are keep their own index.
                if (!move && wa == 1f)
                {
                    index = a;
                }
                else if (!move && wb == 1f)
                {
                    index = b;
                }
                else if (!move && wc == 1f)
                {
                    index = c;
                }
                else
                {
                    added.Add(new Blend(a, b, c, wa, wb, wc));
                    index = baseCount + added.Count - 1;
                }

                placed[index] = pa * wa + pb * wb + pc * wc;
                return points[key] = index;
            }

            float size = Math.Max((pb - pa).Length, Math.Max((pc - pb).Length, (pa - pc).Length));

            // Sub-triangle with corners given as (wb, wc), same winding as (A, B, C); `scale` is its size over the parent's.
            void Refine(float b0, float c0, float b1, float c1, float b2, float c2, float scale, int depth)
            {
                (float d0, _) = Depth(b0, c0);
                (float d1, _) = Depth(b1, c1);
                (float d2, _) = Depth(b2, c2);
                float span = size * scale;
                if (d0 > span && d1 > span && d2 > span)
                {
                    Emit(b0, c0, b1, c1, b2, c2, false);
                    return;
                }

                if (d0 < -span && d1 < -span && d2 < -span)
                {
                    return;
                }

                if (span <= cell || depth >= 8)
                {
                    if (d0 < 0 && d1 < 0 && d2 < 0)
                    {
                        return;
                    }

                    Emit(b0, c0, b1, c1, b2, c2, true);
                    return;
                }

                float b01 = (b0 + b1) * 0.5f, c01 = (c0 + c1) * 0.5f, b12 = (b1 + b2) * 0.5f, c12 = (c1 + c2) * 0.5f;
                float b20 = (b2 + b0) * 0.5f, c20 = (c2 + c0) * 0.5f;
                float half = scale * 0.5f;
                Refine(b0, c0, b01, c01, b20, c20, half, depth + 1);
                Refine(b01, c01, b1, c1, b12, c12, half, depth + 1);
                Refine(b20, c20, b12, c12, b2, c2, half, depth + 1);
                Refine(b01, c01, b12, c12, b20, c20, half, depth + 1);
            }

            Vec3 normal = Vec3.Cross(pb - pa, pc - pa);

            void Emit(float b0, float c0, float b1, float c1, float b2, float c2, bool onEdge)
            {
                int i0 = Vertex(b0, c0, onEdge), i1 = Vertex(b1, c1, onEdge), i2 = Vertex(b2, c2, onEdge);
                if (onEdge)
                {
                    // Corners moved onto the edge can cross over: a flipped or flattened sliver is left out.
                    Vec3 q0 = placed[i0], q1 = placed[i1], q2 = placed[i2];
                    if (Vec3.Dot(Vec3.Cross(q1 - q0, q2 - q0), normal) <= 1e-6f * normal.Length)
                    {
                        return;
                    }
                }

                result.Add(i0);
                result.Add(i1);
                result.Add(i2);
            }

            Refine(0, 0, 1, 0, 0, 1, 1, 0);
        }


        // The field may reach into the triangle: it is roughly a distance (it changes by at most a metre per metre), so a
        // triangle whose corners are all farther from the hole than the triangle is wide cannot be reached.
        private static bool FieldReaches(Vec3 pa, Vec3 pb, Vec3 pc, ClipHole h)
        {
            float size = Math.Max((pb - pa).Length, Math.Max((pc - pb).Length, (pa - pc).Length));
            return h.Field(pa) < size || h.Field(pb) < size || h.Field(pc) < size;
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
