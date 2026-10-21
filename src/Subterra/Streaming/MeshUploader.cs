using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Subterra
{
    /// <summary>MeshData (worker thread) → Unity Mesh (main thread).</summary>
    public static class MeshUploader
    {
        /// <param name="readable">False for render-only meshes: the CPU copy is freed after the upload.</param>
        public static Mesh Upload(MeshData data, string name, bool readable = true)
        {
            var mesh = new Mesh { name = name };
            int count = data.Vertices.Count;
            if (count >= 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            var vertices = new List<Vector3>(count);
            foreach (Vec3 v in data.Vertices)
            {
                vertices.Add(v.ToUnity());
            }

            mesh.SetVertices(vertices);
            if (data.Normals.Count == count)
            {
                var normals = new List<Vector3>(count);
                foreach (Vec3 n in data.Normals)
                {
                    normals.Add(n.ToUnity());
                }

                mesh.SetNormals(normals);
            }

            if (data.Uvs.Count == count)
            {
                var uvs = new List<Vector2>(count);
                foreach (Vec2 uv in data.Uvs)
                {
                    uvs.Add(new Vector2(uv.X, uv.Y));
                }

                mesh.SetUVs(0, uvs);
            }

            mesh.SetTriangles(data.Triangles, 0, true);
            if (data.Normals.Count != count)
            {
                mesh.RecalculateNormals();
            }

            if (!readable)
            {
                mesh.UploadMeshData(true);
            }

            return mesh;
        }
    }
}
