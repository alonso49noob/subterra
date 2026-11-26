using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// UVs that follow the tubes: on a tube's walls the texture runs along the tube and around its section (one seam,
    /// along the crest of the roof), instead of box projection, whose seams wherever a triangle changes its dominant
    /// axis showed as streaks on the walls. Triangles elsewhere (chambers, shafts, across the crest seam, joins of
    /// different tubes) keep box UVs. Vertices are duplicated per (vertex, mapping).
    /// </summary>
    public static class TubeUvProjector
    {
        public static MeshData Project(MeshData m, TubeNetwork net, float metersPerUv)
        {
            // The tube piece nearest each vertex, and the vertex's wall coordinates on it (null: not on a tube).
            var wall = new Vec2?[m.Vertices.Count];
            var piece = new int[m.Vertices.Count];
            for (int i = 0; i < wall.Length; i++)
            {
                int nearest = net.Sdf.NearestPrimitive(m.Vertices[i]);
                piece[i] = nearest;
                // Not on a dead end's rounded end, nor in a fissure: box UVs there, as on a chamber.
                if (nearest >= 0 && net.Primitives[nearest] is TubeSegment tube && !tube.Tall && !tube.PastEnd(m.Vertices[i]))
                {
                    wall[i] = tube.WallUv(m.Vertices[i]);
                }
                else if (nearest >= 0 && net.Primitives[nearest] is Shaft shaft)
                {
                    // Shafts (dug or natural) wrap their texture round the wall: box UVs swirled and stretched across
                    // a big shaft's wall wherever its facing changed axis (Lucas).
                    wall[i] = shaft.WallUv(m.Vertices[i]);
                }
            }

            // Box UVs in the network's own level frame (its vertical, and east and north), not the body's axes: in those, a floor
            // is tilted, and every bump in it switched some triangles to another axis, a patchwork of rotated rock (Lucas, a
            // dug tunnel's floor). Level, floors and roofs keep the projection from above all over, and walls switch only
            // where they turn past 45° between east and north. One frame for the whole network: the same in every chunk.
            Vec3 up = net.Anchor.Length > 1 ? net.Anchor.Normalized.ToVec3() : new Vec3(0, 1, 0);
            Vec3 east = Vec3.Cross(new Vec3(0, 1, 0), up);
            east = east.Length < 1e-4f ? new Vec3(1, 0, 0) : east.Normalized;
            Vec3 north = Vec3.Cross(up, east);
            var result = new MeshData();
            var remap = new Dictionary<long, int>();
            List<int> tris = m.Triangles;
            int skirtStart = -1;
            for (int i = 0; i < tris.Count; i += 3)
            {
                if (i == m.SkirtTriangleStart)
                {
                    skirtStart = result.Triangles.Count;
                }

                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int mode = TubeMapped(m, net, wall, piece, a, b, c) ? 3 : BoxAxis(m, a, b, c, east, up, north);
                foreach (int source in new[] { a, b, c })
                {
                    long key = (long)source * 4 + mode;
                    if (!remap.TryGetValue(key, out int index))
                    {
                        index = result.Vertices.Count;
                        Vec3 v = m.Vertices[source];
                        result.Vertices.Add(v);
                        result.Normals.Add(m.Normals.Count > source ? m.Normals[source] : new Vec3(0, 1, 0));
                        float pe = Vec3.Dot(v, east), pu = Vec3.Dot(v, up), pn = Vec3.Dot(v, north);
                        Vec2 uv = mode == 3 ? wall[source].Value
                            : mode == 0 ? new Vec2(pn, pu) : mode == 1 ? new Vec2(pe, pn) : new Vec2(pe, pu);
                        result.Uvs.Add(new Vec2(uv.X / metersPerUv, uv.Y / metersPerUv));
                        remap[key] = index;
                    }

                    result.Triangles.Add(index);
                }
            }

            result.SkirtTriangleStart = skirtStart < 0 ? result.Triangles.Count : skirtStart;
            return result;
        }

        // All three corners on tubes of the same chain and close together in wall coordinates (not across the crest seam).
        private static bool TubeMapped(MeshData m, TubeNetwork net, Vec2?[] wall, int[] piece, int a, int b, int c)
        {
            if (wall[a] == null || wall[b] == null || wall[c] == null)
            {
                return false;
            }

            Primitive pa = net.Primitives[piece[a]], pb = net.Primitives[piece[b]], pc = net.Primitives[piece[c]];
            if (pa is Shaft shaft)
            {
                // One shaft, not across its seam, and a wall (not a floor facing along the axis).
                if (piece[b] != piece[a] || piece[c] != piece[a])
                {
                    return false;
                }

                Vec3 n = Vec3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
                float nl = n.Length;
                if (nl <= 0 || Math.Abs(Vec3.Dot(n, shaft.Up)) > 0.7f * nl)
                {
                    return false;
                }

                float seam = shaft.Radius * (float)Math.PI * 0.5f;
                Vec2 sa = wall[a].Value, sb = wall[b].Value, sc = wall[c].Value;
                return Math.Abs(sa.X - sb.X) < seam && Math.Abs(sa.X - sc.X) < seam && Math.Abs(sb.X - sc.X) < seam;
            }

            if (!(pa is TubeSegment) || !(pb is TubeSegment) || !(pc is TubeSegment))
            {
                return false;
            }

            if (pa.Group != pb.Group || pa.Group != pc.Group)
            {
                return false;
            }

            float limit = ((TubeSegment)pa).HalfPerimeter(default) * 0.5f;
            Vec2 wa = wall[a].Value, wb = wall[b].Value, wc = wall[c].Value;
            return Math.Abs(wa.X - wb.X) < limit && Math.Abs(wa.X - wc.X) < limit && Math.Abs(wb.X - wc.X) < limit
                && Math.Abs(wa.Y - wb.Y) < limit && Math.Abs(wa.Y - wc.Y) < limit;
        }

        // 0: facing east or west, 1: up or down (floors, roofs), 2: north or south. Floors win ties a little (bumpy floors stay
        // in one projection up to slopes of about 50°).
        private static int BoxAxis(MeshData m, int a, int b, int c, Vec3 east, Vec3 up, Vec3 north)
        {
            Vec3 n = Vec3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
            float ae = Math.Abs(Vec3.Dot(n, east)), au = Math.Abs(Vec3.Dot(n, up)) * 1.2f, an = Math.Abs(Vec3.Dot(n, north));
            return au >= ae && au >= an ? 1 : ae >= an ? 0 : 2;
        }
    }
}
