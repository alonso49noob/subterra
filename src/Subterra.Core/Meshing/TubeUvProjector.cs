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
            }

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
                int mode = TubeMapped(net, wall, piece, a, b, c) ? 3 : BoxAxis(m, a, b, c);
                foreach (int source in new[] { a, b, c })
                {
                    long key = (long)source * 4 + mode;
                    if (!remap.TryGetValue(key, out int index))
                    {
                        index = result.Vertices.Count;
                        Vec3 v = m.Vertices[source];
                        result.Vertices.Add(v);
                        result.Normals.Add(m.Normals.Count > source ? m.Normals[source] : new Vec3(0, 1, 0));
                        Vec2 uv = mode == 3 ? wall[source].Value
                            : mode == 0 ? new Vec2(v.Y, v.Z) : mode == 1 ? new Vec2(v.X, v.Z) : new Vec2(v.X, v.Y);
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
        private static bool TubeMapped(TubeNetwork net, Vec2?[] wall, int[] piece, int a, int b, int c)
        {
            if (wall[a] == null || wall[b] == null || wall[c] == null)
            {
                return false;
            }

            Primitive pa = net.Primitives[piece[a]], pb = net.Primitives[piece[b]], pc = net.Primitives[piece[c]];
            if (pa.Group != pb.Group || pa.Group != pc.Group)
            {
                return false;
            }

            float limit = ((TubeSegment)pa).HalfPerimeter(default) * 0.5f;
            Vec2 wa = wall[a].Value, wb = wall[b].Value, wc = wall[c].Value;
            return Math.Abs(wa.X - wb.X) < limit && Math.Abs(wa.X - wc.X) < limit && Math.Abs(wb.X - wc.X) < limit
                && Math.Abs(wa.Y - wb.Y) < limit && Math.Abs(wa.Y - wc.Y) < limit;
        }

        private static int BoxAxis(MeshData m, int a, int b, int c)
        {
            Vec3 n = Vec3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
            float ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
            return ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
        }
    }
}
