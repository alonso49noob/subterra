using System;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Water;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// A patch of an ocean's floor around a point (Lucas: from the water, pockets carved in the rock under the floor floated
    /// in the dark, for the floor was never drawn, and a sinking craft had nothing to land on). A grid on the floor's sphere
    /// with rolling hills that only rise from it (the water reaches the smooth floor everywhere), faces up into the water,
    /// left open where a cave or shaft goes through. Vertices are relative to <c>centre</c> (body frame, metres).
    /// </summary>
    public static class SeaFloorMesher
    {
        /// <summary>Highest the floor's hills rise over the smooth floor (m).</summary>
        public const float Relief = 40f;

        public static MeshData Build(OceanShell ocean, Vec3d centreDir, float halfSize, float cell, ulong seed, out Vec3d centre, Func<Vec3d, bool> isOpen = null)
        {
            centreDir = centreDir.Normalized;
            BodyFrame.ToLatLon(centreDir, out double lat0, out _);
            centre = centreDir * ocean.FloorRadius(lat0);
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
                    Vec3d onFloor = dir * (ocean.FloorRadius(lat) + Rise(dir * radius, seed));
                    m.Vertices.Add((onFloor - centre).ToVec3());
                    m.Uvs.Add(new Vec2((float)(x / 8), (float)(y / 8)));
                }
            }

            // Faces up (away from the body's centre), opposite to the ceiling's.
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    if (isOpen != null && isOpen(centre + Vec3d.FromVec3((m.Vertices[a] + m.Vertices[d]) * 0.5f)))
                    {
                        continue;
                    }

                    m.Triangles.Add(a);
                    m.Triangles.Add(b);
                    m.Triangles.Add(c);
                    m.Triangles.Add(b);
                    m.Triangles.Add(d);
                    m.Triangles.Add(c);
                }
            }

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

        /// <summary>How high (m) the floor rises over the smooth floor at a point: 0 to <see cref="Relief"/>.</summary>
        public static float Rise(Vec3d at, ulong seed)
        {
            var p = new Vec3((float)(at.X / 600), (float)(at.Y / 600), (float)(at.Z / 600));
            float broad = Noise.Fbm(p, seed + 11, 4);
            float fine = Noise.Fbm(p * 10, seed + 12, 2);
            float h = 0.5f + 1.4f * broad + 0.3f * fine;
            return Relief * Math.Max(0, Math.Min(1, h));
        }
    }
}
