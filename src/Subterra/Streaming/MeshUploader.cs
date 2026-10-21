using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Subterra
{
    /// <summary>
    /// A mesh's arrays already in Unity's types, with its bounds. Built by <see cref="MeshUploader.Prepare"/>
    /// on a worker thread (only plain structs, no Unity calls), so the main thread just hands them over.
    /// </summary>
    public sealed class PreparedMesh
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] Triangles;
        public Bounds Bounds;
    }

    /// <summary>MeshData (worker thread) → Unity Mesh (main thread).</summary>
    public static class MeshUploader
    {
        /// <summary>Converts <paramref name="data"/> to Unity arrays; safe on any thread.</summary>
        public static PreparedMesh Prepare(MeshData data)
        {
            int count = data.Vertices.Count;
            var prepared = new PreparedMesh { Vertices = new Vector3[count], Triangles = data.Triangles.ToArray() };
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = data.Vertices[i].ToUnity();
                prepared.Vertices[i] = v;
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            prepared.Bounds = count == 0 ? default : new Bounds((min + max) * 0.5f, max - min);
            if (data.Normals.Count == count)
            {
                prepared.Normals = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    prepared.Normals[i] = data.Normals[i].ToUnity();
                }
            }

            if (data.Uvs.Count == count)
            {
                prepared.Uvs = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    Vec2 uv = data.Uvs[i];
                    prepared.Uvs[i] = new Vector2(uv.X, uv.Y);
                }
            }

            return prepared;
        }

        /// <param name="readable">False for render-only meshes: the CPU copy is freed after the upload.</param>
        public static Mesh Upload(MeshData data, string name, bool readable = true) => Upload(Prepare(data), name, readable);

        /// <param name="readable">False for render-only meshes: the CPU copy is freed after the upload.</param>
        public static Mesh Upload(PreparedMesh prepared, string name, bool readable = true)
        {
            var mesh = new Mesh { name = name };
            if (prepared.Vertices.Length >= 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.vertices = prepared.Vertices;
            if (prepared.Normals != null)
            {
                mesh.normals = prepared.Normals;
            }

            if (prepared.Uvs != null)
            {
                mesh.uv = prepared.Uvs;
            }

            mesh.SetTriangles(prepared.Triangles, 0, false);
            mesh.bounds = prepared.Bounds;
            if (prepared.Normals == null)
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
