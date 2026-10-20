using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// Box-projected UVs for shaders without triplanar mapping: each triangle takes the two
    /// coordinates other than its dominant normal axis (X → (Y, Z), Y → (X, Z), Z → (X, Y)),
    /// divided by <c>metersPerUv</c>. Vertices are duplicated per (vertex, axis).
    /// </summary>
    public static class UvProjector
    {
        public static MeshData Project(MeshData m, float metersPerUv)
        {
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

                Vec3 a = m.Vertices[tris[i]], b = m.Vertices[tris[i + 1]], c = m.Vertices[tris[i + 2]];
                Vec3 n = Vec3.Cross(b - a, c - a);
                float ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
                int axis = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
                for (int k = 0; k < 3; k++)
                {
                    int source = tris[i + k];
                    long key = (long)source * 3 + axis;
                    if (!remap.TryGetValue(key, out int index))
                    {
                        index = result.Vertices.Count;
                        Vec3 v = m.Vertices[source];
                        result.Vertices.Add(v);
                        result.Normals.Add(m.Normals.Count > source ? m.Normals[source] : n.Normalized);
                        result.Uvs.Add(axis == 0 ? new Vec2(v.Y / metersPerUv, v.Z / metersPerUv)
                            : axis == 1 ? new Vec2(v.X / metersPerUv, v.Z / metersPerUv)
                            : new Vec2(v.X / metersPerUv, v.Y / metersPerUv));
                        remap[key] = index;
                    }

                    result.Triangles.Add(index);
                }
            }

            result.SkirtTriangleStart = skirtStart < 0 ? result.Triangles.Count : skirtStart;
            return result;
        }
    }
}
