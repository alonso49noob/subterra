using System;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// The debris rim around a skylight, in a local frame (X/Z horizontal, Y up, origin at the hole's
    /// centre on the terrain). Rings from the inner radius (a lip 2 m down into the shaft) to the outer
    /// radius (0.3 m over the terrain, covering the jagged cut), rubble bumps in between, and a 2 m
    /// skirt under the outer ring. Faces point up.
    /// </summary>
    public static class RimBuilder
    {
        public const float OuterLift = 0.3f;

        /// <summary>How far a skylight rim's outer edge sits under the terrain (m).</summary>
        public const float OuterSink = 0.05f;

        /// <summary>Depth of the wall hanging from a skylight rim's inner edge (m): it closes the gap down to the shaft wall.</summary>
        public const float InnerWall = 6f;
        public const float LipDrop = 2f;
        public const float SkirtDepth = 2f;
        public const float MaxBump = 1.5f;

        /// <summary>
        /// Only the wall hanging into a hole clipped into the terrain itself: from just under the terrain's edge
        /// (<paramref name="heights"/>, one per segment) down <see cref="InnerWall"/> m, facing the pit. It closes the gap
        /// down to the shaft wall, which is clipped under the terrain.
        /// </summary>
        public static MeshData Wall(float radius, int segments, float[] heights)
        {
            var radii = new float[Math.Max(0, segments)];
            for (int s = 0; s < radii.Length; s++)
            {
                radii[s] = radius;
            }

            return Wall(radii, heights);
        }

        /// <summary>The same along an irregular edge: one radius (and one height) per segment, evenly spaced in angle.</summary>
        public static MeshData Wall(float[] radii, float[] heights)
        {
            int segments = radii.Length;
            if (segments < 3 || heights.Length != segments)
            {
                throw new ArgumentException("needs segments ≥ 3 and one height per segment");
            }

            var mesh = new MeshData();
            for (int ring = 0; ring < 2; ring++)
            {
                for (int s = 0; s < segments; s++)
                {
                    double angle = 2 * Math.PI * s / segments;
                    float x = (float)Math.Cos(angle) * radii[s], z = (float)Math.Sin(angle) * radii[s];
                    float y = heights[s] - (ring == 0 ? WallTopDrop : InnerWall);
                    mesh.Vertices.Add(new Vec3(x, y, z));
                    mesh.Normals.Add(new Vec3(-x, 0, -z).Normalized);
                    mesh.Uvs.Add(new Vec2((float)(angle * radii[s]) / 4f, y / 4f));
                }
            }

            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                int a = s, b = s1, c = segments + s, d = segments + s1;
                mesh.Triangles.AddRange(new[] { a, c, b, b, c, d });
            }

            mesh.SkirtTriangleStart = mesh.Triangles.Count;
            return mesh;
        }

        /// <summary>How far under the terrain's edge a clipped hole's wall starts (m): the terrain hides its top.</summary>
        public const float WallTopDrop = 0.15f;

        /// <param name="heights">Terrain height over the centre's plane at [ring × segments + segment].</param>
        public static MeshData Build(float innerRadius, float outerRadius, int segments, int rings, float[] heights, ulong seed, bool smooth = false)
        {
            if (rings < 2)
            {
                throw new ArgumentException("needs rings ≥ 2");
            }

            var radii = new float[rings];
            for (int r = 0; r < rings; r++)
            {
                radii[r] = innerRadius + (outerRadius - innerRadius) * r / (rings - 1);
            }

            return Build(radii, segments, heights, seed, smooth);
        }

        /// <summary>The same with rings at the given radii (increasing): finer where the rim must follow an edge.</summary>
        public static MeshData Build(float[] radii, int segments, float[] heights, ulong seed, bool smooth = false)
        {
            int rings = radii.Length;
            if (rings < 2 || segments < 3 || heights.Length != rings * segments)
            {
                throw new ArgumentException("needs rings ≥ 2, segments ≥ 3 and rings × segments heights");
            }

            var mesh = new MeshData();
            int[][] tables = Noise.Tables(seed);
            for (int r = 0; r < rings; r++)
            {
                float radius = radii[r];
                float t = (radius - radii[0]) / Math.Max(1e-6f, radii[rings - 1] - radii[0]);
                for (int s = 0; s < segments; s++)
                {
                    double angle = 2 * Math.PI * s / segments;
                    float x = (float)Math.Cos(angle) * radius, z = (float)Math.Sin(angle) * radius;
                    float y = heights[r * segments + s];
                    if (r == 0)
                    {
                        // A smooth rim (tunnel mouth) is a flat disc just over the ground: no lip, no debris bumps.
                        y += smooth ? 0.03f : -LipDrop;
                    }
                    else if (r == rings - 1)
                    {
                        // A smooth rim (driven on) sits just over the ground; a skylight's outer edge just under it, so the terrain
                        // hides the seam and there is no step.
                        y += smooth ? 0.03f : -OuterSink;
                    }
                    else
                    {
                        // The ground slumps towards the pit (no raised lip, as at real lunar pits), a little uneven.
                        float noise = Noise.Fbm(new Vec3(x * 0.15f, 0.5f, z * 0.15f), tables, 3);
                        y += smooth ? 0.03f : -LipDrop * (1 - t) * (1 - t) + MaxBump * 0.15f * noise * (1 - t);
                    }

                    mesh.Vertices.Add(new Vec3(x, y, z));
                    mesh.Normals.Add(new Vec3(0, 1, 0));
                    mesh.Uvs.Add(new Vec2(x / 4f, z / 4f));
                }
            }

            // Skirt ring under the outer ring.
            int skirt = mesh.Vertices.Count;
            for (int s = 0; s < segments; s++)
            {
                Vec3 o = mesh.Vertices[(rings - 1) * segments + s];
                Vec3 outward = new Vec3(o.X, 0, o.Z).Normalized;
                mesh.Vertices.Add(new Vec3(o.X, o.Y - SkirtDepth, o.Z));
                mesh.Normals.Add(outward);
                mesh.Uvs.Add(new Vec2(o.X / 4f, (o.Z + SkirtDepth) / 4f));
            }

            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    int a = r * segments + s, b = r * segments + s1, c = (r + 1) * segments + s, d = (r + 1) * segments + s1;
                    // Cross(b - a, c - a) must point up: a → b (around) → c (outwards).
                    mesh.Triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            }

            // Normals from the surface itself (lit like the terrain around it, not like a flat plate).
            var sums = new Vec3[rings * segments];
            for (int i = 0; i < mesh.Triangles.Count; i += 3)
            {
                int a = mesh.Triangles[i], b = mesh.Triangles[i + 1], c = mesh.Triangles[i + 2];
                Vec3 n = Vec3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]);
                sums[a] += n;
                sums[b] += n;
                sums[c] += n;
            }

            for (int i = 0; i < sums.Length; i++)
            {
                if (sums[i].Length > 1e-6f)
                {
                    mesh.Normals[i] = sums[i].Normalized;
                }
            }

            mesh.SkirtTriangleStart = mesh.Triangles.Count;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                int a = (rings - 1) * segments + s, b = (rings - 1) * segments + s1, c = skirt + s, d = skirt + s1;
                // Facing outwards.
                mesh.Triangles.AddRange(new[] { a, b, c, b, d, c });
            }

            if (!smooth)
            {
                // A wall hanging from the inner edge, facing the pit: no gap (sky, a wheel trap) between the rim and the
                // shaft wall, which is clipped under the terrain.
                int wall = mesh.Vertices.Count;
                for (int s = 0; s < segments; s++)
                {
                    Vec3 o = mesh.Vertices[s];
                    Vec3 inward = new Vec3(-o.X, 0, -o.Z).Normalized;
                    mesh.Vertices.Add(new Vec3(o.X, o.Y - InnerWall, o.Z));
                    mesh.Normals.Add(inward);
                    mesh.Uvs.Add(new Vec2(o.X / 4f, (o.Z - InnerWall) / 4f));
                }

                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    int a = s, b = s1, c = wall + s, d = wall + s1;
                    // Facing inwards (the reverse of the skirt's winding).
                    mesh.Triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            }

            return mesh;
        }
    }
}
