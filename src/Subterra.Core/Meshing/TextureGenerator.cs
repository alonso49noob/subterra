using System;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// Procedural, seamlessly tiling rock and ice textures (no image files needed): a height field from
    /// fBm blended across the edges, albedo from it, and a tangent-space normal map from its slopes.
    /// Row-major, y up, values 0–1.
    /// </summary>
    public static class TextureGenerator
    {
        /// <summary>Tiling height field in [0, 1].</summary>
        public static float[] Height(int size, ulong seed, float features = 6f, int octaves = 5)
        {
            int[][] tables = Noise.Tables(seed);
            var h = new float[size * size];
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    // Blend four shifted copies so opposite edges match (seamless tiling).
                    float a = Sample(u, v), b = Sample(u - 1, v), c = Sample(u, v - 1), d = Sample(u - 1, v - 1);
                    float value = (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
                    h[y * size + x] = value;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }

            float range = Math.Max(1e-6f, max - min);
            for (int i = 0; i < h.Length; i++)
            {
                h[i] = (h[i] - min) / range;
            }

            return h;

            float Sample(float u, float v) => Noise.Fbm(new Vec3(u * features, v * features, 0.37f), tables, octaves);
        }

        /// <summary>Grey albedo with cracks darkened: <paramref name="dark"/>..<paramref name="light"/>.</summary>
        public static float[] Albedo(float[] height, int size, float dark = 0.35f, float light = 0.75f)
        {
            var albedo = new float[height.Length];
            for (int i = 0; i < height.Length; i++)
            {
                float t = height[i];
                float crack = t < 0.18f ? t / 0.18f : 1f;
                albedo[i] = (dark + (light - dark) * t) * (0.6f + 0.4f * crack);
            }

            return albedo;
        }

        /// <summary>Tangent-space normals (x, y, z in [-1, 1], z up) from the height slopes; wraps at the edges.</summary>
        public static Vec3[] Normals(float[] height, int size, float strength = 4f)
        {
            var normals = new Vec3[height.Length];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float l = height[y * size + (x + size - 1) % size], r = height[y * size + (x + 1) % size];
                    float d = height[(y + size - 1) % size * size + x], u = height[(y + 1) % size * size + x];
                    normals[y * size + x] = new Vec3((l - r) * strength, (d - u) * strength, 1f).Normalized;
                }
            }

            return normals;
        }
    }
}
