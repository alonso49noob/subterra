using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>A triangle mesh without Unity types (built on worker threads, uploaded on the main thread).</summary>
    public sealed class MeshData
    {
        public List<Vec3> Vertices { get; } = new List<Vec3>();

        public List<Vec3> Normals { get; } = new List<Vec3>();

        /// <summary>Empty unless UVs were projected.</summary>
        public List<Vec2> Uvs { get; } = new List<Vec2>();

        public List<int> Triangles { get; } = new List<int>();

        /// <summary>Index in <see cref="Triangles"/> where the skirt triangles start (= Count without skirts).</summary>
        public int SkirtTriangleStart { get; set; }

        /// <summary>Approximate memory of the uploaded mesh.</summary>
        public long ByteSize => Vertices.Count * 24L + Uvs.Count * 8L + Triangles.Count * 4L + 64;

        public bool IsEmpty => Triangles.Count == 0;
    }
}
