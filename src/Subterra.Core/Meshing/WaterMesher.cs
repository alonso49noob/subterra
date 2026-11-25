using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// The mesh of a still water level: a grid over the disc of the <see cref="WaterSurface"/>, each grid triangle cut
    /// where the cave ends (the field crosses zero along its edges), so the water meets the walls whatever their shape.
    /// It reaches <see cref="IntoRock"/> into the rock, where the walls hide its edge. Faces up. Thread-safe.
    /// </summary>
    public static class WaterMesher
    {
        /// <summary>How far the water goes on into the rock (m), so no gap shows between it and a wall.</summary>
        public const float IntoRock = 0.4f;

        /// <summary>Metres per texture tile.</summary>
        public const float MetersPerUv = 8f;

        public static MeshData Build(ISdf cave, WaterSurface water)
        {
            var mesh = new MeshData();
            Vec3 up = water.Up.Normalized;
            Vec3 helper = Math.Abs(up.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            Vec3 e1 = Vec3.Cross(up, helper).Normalized;
            Vec3 e2 = Vec3.Cross(e1, up);
            // About 200 cells across, 0.75–8 m each: fine at a pool's edge, few enough over a lake kilometres wide.
            float cell = Math.Max(0.75f, Math.Min(8f, water.Radius / 100f));
            int n = (int)Math.Ceiling(2 * water.Radius / cell);
            var points = new Vec3[(n + 1) * (n + 1)];
            var values = new float[points.Length];
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    float x = -water.Radius + i * cell, y = -water.Radius + j * cell;
                    Vec3 p = water.Centre + e1 * x + e2 * y;
                    int k = j * (n + 1) + i;
                    points[k] = p;
                    float disc = (float)Math.Sqrt(x * x + y * y) - water.Radius;
                    values[k] = Math.Max(cave.Distance(p) - IntoRock, disc);
                }
            }

            var poly = new List<(Vec3 p, float v)>(4);
            var clipped = new List<Vec3>(4);
            var shared = new int[points.Length];
            for (int k = 0; k < shared.Length; k++)
            {
                shared[k] = -1;
            }

            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    Triangle(mesh, points, values, a, c, b, poly, clipped, shared);
                    Triangle(mesh, points, values, b, c, d, poly, clipped, shared);
                }
            }

            foreach (Vec3 v in mesh.Vertices)
            {
                mesh.Normals.Add(up);
                Vec3 q = v - water.Centre;
                mesh.Uvs.Add(new Vec2(Vec3.Dot(q, e1) / MetersPerUv, Vec3.Dot(q, e2) / MetersPerUv));
            }

            mesh.SkirtTriangleStart = mesh.Triangles.Count;
            return mesh;
        }

        // The part of triangle (i0, i1, i2) where the field is negative, fanned into triangles (winding kept).
        private static void Triangle(MeshData mesh, Vec3[] points, float[] values, int i0, int i1, int i2,
            List<(Vec3 p, float v)> poly, List<Vec3> clipped, int[] shared)
        {
            float v0 = values[i0], v1 = values[i1], v2 = values[i2];
            if (v0 >= 0 && v1 >= 0 && v2 >= 0)
            {
                return;
            }

            // Wholly in the water (most of a lake): grid vertices shared between triangles.
            if (v0 < 0 && v1 < 0 && v2 < 0)
            {
                foreach (int g in new[] { i0, i1, i2 })
                {
                    if (shared[g] < 0)
                    {
                        shared[g] = mesh.Vertices.Count;
                        mesh.Vertices.Add(points[g]);
                    }

                    mesh.Triangles.Add(shared[g]);
                }

                return;
            }

            poly.Clear();
            poly.Add((points[i0], v0));
            poly.Add((points[i1], v1));
            poly.Add((points[i2], v2));
            clipped.Clear();
            for (int k = 0; k < 3; k++)
            {
                var (p, v) = poly[k];
                var (q, w) = poly[(k + 1) % 3];
                if (v < 0)
                {
                    clipped.Add(p);
                }

                if (v < 0 != w < 0)
                {
                    clipped.Add(p + (q - p) * (v / (v - w)));
                }
            }

            int start = mesh.Vertices.Count;
            mesh.Vertices.AddRange(clipped);
            for (int k = 1; k + 1 < clipped.Count; k++)
            {
                mesh.Triangles.Add(start);
                mesh.Triangles.Add(start + k);
                mesh.Triangles.Add(start + k + 1);
            }
        }
    }
}
