using System;
using Subterra.Core.Model;
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
        /// <summary>Clip depth (m) for the walls of dug tunnels.</summary>
        public const float DugBelow = 0.05f;

        /// <summary>
        /// Clips the mesh <paramref name="below"/> m under the terrain: triangles wholly above that level are
        /// removed, and the vertices above it of the triangles crossing it are pulled down onto it (a clean
        /// edge along the ground instead of a saw of whole triangles).
        /// </summary>
        public static MeshData DropAboveSurface(MeshData mesh, TubeNetwork net, float below = 1f)
        {
            if (mesh.IsEmpty || !NearSurface(mesh, net, below))
            {
                return mesh;
            }

            // Metres each vertex is over the clip level (≤ 0: under it). Dug tunnels are clipped just under the ground (their
            // terrain is sampled every 2 m, and a ramp must start level with it); natural shafts 1 m under (rims hide them).
            var over = new float[mesh.Vertices.Count];
            for (int i = 0; i < over.Length; i++)
            {
                Vec3 v = mesh.Vertices[i];
                double above = net.AboveSurface(v);
                float level = below;
                if (above > -below - 1)
                {
                    int nearest = net.Sdf.NearestPrimitive(v);
                    if (nearest >= 0 && net.Primitives[nearest].Zone == ZoneKind.Excavation)
                    {
                        level = DugBelow;
                    }
                }

                over[i] = (float)above + level;
            }

            var result = new MeshData();
            result.Vertices.AddRange(mesh.Vertices);
            result.Normals.AddRange(mesh.Normals);
            result.Uvs.AddRange(mesh.Uvs);
            var pull = new bool[over.Length];
            int skirtStart = -1;
            for (int i = 0; i < mesh.Triangles.Count; i += 3)
            {
                if (i == mesh.SkirtTriangleStart)
                {
                    skirtStart = result.Triangles.Count;
                }

                int a = mesh.Triangles[i], b = mesh.Triangles[i + 1], c = mesh.Triangles[i + 2];
                int up = (over[a] > 0 ? 1 : 0) + (over[b] > 0 ? 1 : 0) + (over[c] > 0 ? 1 : 0);
                if (up == 3)
                {
                    continue;
                }

                if (up > 0)
                {
                    pull[a] |= over[a] > 0;
                    pull[b] |= over[b] > 0;
                    pull[c] |= over[c] > 0;
                }

                result.Triangles.Add(a);
                result.Triangles.Add(b);
                result.Triangles.Add(c);
            }

            for (int i = 0; i < pull.Length; i++)
            {
                if (pull[i])
                {
                    result.Vertices[i] = mesh.Vertices[i] - net.UpAt(mesh.Vertices[i]) * over[i];
                }
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
