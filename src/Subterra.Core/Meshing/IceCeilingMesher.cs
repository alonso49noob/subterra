using System;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Water;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// A patch of an ocean's ceiling (the ice's underside, sub-project 4) around a point: a grid on the ceiling's sphere,
    /// pushed up and down by noise (broad ridges and channels, smaller bumps), its faces looking down into the water.
    /// Vertices are relative to <c>centre</c> (body frame, metres); the plugin places the patch there.
    /// </summary>
    public static class IceCeilingMesher
    {
        /// <summary>Most the ceiling's underside rises or hangs from its mean level (m).</summary>
        public const float Relief = 25f;

        public static MeshData Build(OceanShell ocean, Vec3d centreDir, float halfSize, float cell, ulong seed, out Vec3d centre)
        {
            centreDir = centreDir.Normalized;
            BodyFrame.ToLatLon(centreDir, out double lat0, out _);
            centre = centreDir * ocean.CeilingRadius(lat0);
            Vec3d east = Vec3d.Cross(new Vec3d(0, 1, 0), centreDir);
            east = east.LengthSquared < 1e-12 ? new Vec3d(1, 0, 0) : east.Normalized;
            Vec3d north = Vec3d.Cross(centreDir, east).Normalized;
            double radius = centre.Length;

            int n = Math.Max(2, (int)Math.Ceiling(2 * halfSize / cell));
            var m = new MeshData();
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    double x = -halfSize + i * (2.0 * halfSize / n), y = -halfSize + j * (2.0 * halfSize / n);
                    Vec3d dir = (centreDir + (east * x + north * y) / radius).Normalized;
                    BodyFrame.ToLatLon(dir, out double lat, out _);
                    Vec3d onCeiling = dir * (ocean.CeilingRadius(lat) - Hang(dir * radius, seed));
                    m.Vertices.Add((onCeiling - centre).ToVec3());
                    m.Uvs.Add(new Vec2((float)(x / 8), (float)(y / 8)));
                }
            }

            // Faces down (towards the body's centre): wound so that their normals point down.
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.Triangles.Add(a);
                    m.Triangles.Add(c);
                    m.Triangles.Add(b);
                    m.Triangles.Add(b);
                    m.Triangles.Add(c);
                    m.Triangles.Add(d);
                }
            }

            // Normals from the faces, averaged at each vertex.
            var normals = new Vec3[m.Vertices.Count];
            for (int t = 0; t < m.Triangles.Count; t += 3)
            {
                int a = m.Triangles[t], b = m.Triangles[t + 1], c = m.Triangles[t + 2];
                Vec3 face = Vec3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
                normals[a] += face;
                normals[b] += face;
                normals[c] += face;
            }

            foreach (Vec3 normal in normals)
            {
                m.Normals.Add(normal.Normalized);
            }

            m.SkirtTriangleStart = m.Triangles.Count;
            return m;
        }

        // How far the ice hangs below its mean ceiling at a point (negative: a channel up into it).
        private static float Hang(Vec3d at, ulong seed)
        {
            var p = new Vec3((float)(at.X / 400), (float)(at.Y / 400), (float)(at.Z / 400));
            float broad = Noise.Fbm(p, seed, 3);
            float fine = Noise.Fbm(p * 12, seed + 1, 2);
            return Relief * (0.8f * broad + 0.2f * fine);
        }
    }
}
