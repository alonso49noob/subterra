using System.Collections.Generic;
using Subterra.Core.Meshing;

namespace Subterra.Core.Scatter
{
    /// <summary>Copies a prop mesh once per instance into one mesh (under Unity's 16-bit index limit).</summary>
    public static class ScatterMerger
    {
        public const int MaxVertices = 65000;

        public static MeshData Merge(MeshData prop, IReadOnlyList<ScatterInstance> inst, out int dropped)
        {
            var merged = new MeshData();
            int perProp = prop.Vertices.Count;
            int fit = perProp == 0 ? inst.Count : MaxVertices / perProp;
            int used = System.Math.Min(fit, inst.Count);
            dropped = inst.Count - used;
            bool uvs = prop.Uvs.Count == perProp;
            bool normals = prop.Normals.Count == perProp;
            for (int i = 0; i < used; i++)
            {
                ScatterInstance s = inst[i];
                int offset = merged.Vertices.Count;
                for (int v = 0; v < perProp; v++)
                {
                    merged.Vertices.Add(s.Transform(prop.Vertices[v]));
                    if (normals)
                    {
                        merged.Normals.Add(s.Rotate(prop.Normals[v]));
                    }

                    if (uvs)
                    {
                        merged.Uvs.Add(prop.Uvs[v]);
                    }
                }

                foreach (int t in prop.Triangles)
                {
                    merged.Triangles.Add(t + offset);
                }
            }

            merged.SkirtTriangleStart = merged.Triangles.Count;
            return merged;
        }
    }
}
