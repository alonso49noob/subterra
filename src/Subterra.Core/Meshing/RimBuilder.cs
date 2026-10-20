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
        public const float LipDrop = 2f;
        public const float SkirtDepth = 2f;
        public const float MaxBump = 1.5f;

        /// <param name="heights">Terrain height over the centre's plane at [ring × segments + segment].</param>
        public static MeshData Build(float innerRadius, float outerRadius, int segments, int rings, float[] heights, ulong seed)
        {
            if (rings < 2 || segments < 3 || heights.Length != rings * segments)
            {
                throw new ArgumentException("needs rings ≥ 2, segments ≥ 3 and rings × segments heights");
            }

            var mesh = new MeshData();
            int[][] tables = Noise.Tables(seed);
            for (int r = 0; r < rings; r++)
            {
                float t = (float)r / (rings - 1);
                float radius = innerRadius + (outerRadius - innerRadius) * t;
                for (int s = 0; s < segments; s++)
                {
                    double angle = 2 * Math.PI * s / segments;
                    float x = (float)Math.Cos(angle) * radius, z = (float)Math.Sin(angle) * radius;
                    float y = heights[r * segments + s];
                    if (r == 0)
                    {
                        y -= LipDrop;
                    }
                    else if (r == rings - 1)
                    {
                        y += OuterLift;
                    }
                    else
                    {
                        float bump = (Noise.Fbm(new Vec3(x * 0.15f, 0.5f, z * 0.15f), tables, 3) + 1) * 0.5f;
                        y += MaxBump * (1 - t) * bump;
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

            mesh.SkirtTriangleStart = mesh.Triangles.Count;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                int a = (rings - 1) * segments + s, b = (rings - 1) * segments + s1, c = skirt + s, d = skirt + s1;
                // Facing outwards.
                mesh.Triangles.AddRange(new[] { a, b, c, b, d, c });
            }

            return mesh;
        }
    }
}
