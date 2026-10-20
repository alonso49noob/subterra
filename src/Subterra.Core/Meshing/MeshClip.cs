using System;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// The SDF's skylight shafts rise above the terrain so that rays and meshes clear it; the mesh
    /// above the terrain (shaft tops, their lids) must not be drawn: the terrain and the skylight rim
    /// are there instead.
    /// </summary>
    public static class MeshClip
    {
        /// <summary>Removes the triangles whose centroid is less than <paramref name="below"/> m under the terrain.</summary>
        public static MeshData DropAboveSurface(MeshData mesh, TubeNetwork net, float below = 1f)
        {
            if (mesh.IsEmpty || !NearSurface(mesh, net, below))
            {
                return mesh;
            }

            var result = new MeshData();
            result.Vertices.AddRange(mesh.Vertices);
            result.Normals.AddRange(mesh.Normals);
            result.Uvs.AddRange(mesh.Uvs);
            int skirtStart = -1;
            for (int i = 0; i < mesh.Triangles.Count; i += 3)
            {
                if (i == mesh.SkirtTriangleStart)
                {
                    skirtStart = result.Triangles.Count;
                }

                Vec3 c = (mesh.Vertices[mesh.Triangles[i]] + mesh.Vertices[mesh.Triangles[i + 1]] + mesh.Vertices[mesh.Triangles[i + 2]]) / 3f;
                if (net.AboveSurface(c) > -below)
                {
                    continue;
                }

                result.Triangles.Add(mesh.Triangles[i]);
                result.Triangles.Add(mesh.Triangles[i + 1]);
                result.Triangles.Add(mesh.Triangles[i + 2]);
            }

            result.SkirtTriangleStart = skirtStart < 0 ? result.Triangles.Count : skirtStart;
            return result;
        }

        // Cheap test: can any vertex be within reach of the lowest terrain over the network?
        private static bool NearSurface(MeshData mesh, TubeNetwork net, float below)
        {
            double limit = net.MinSurfaceRadius - below - 1;
            foreach (Vec3 v in mesh.Vertices)
            {
                if (net.ToBody(v).Length > limit)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
