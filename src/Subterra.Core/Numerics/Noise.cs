using System;
using System.Collections.Concurrent;

namespace Subterra.Core.Numerics
{
    /// <summary>Seeded improved Perlin noise and fBm. Thread-safe.</summary>
    public static class Noise
    {
        private static readonly ConcurrentDictionary<ulong, int[]> Permutations = new ConcurrentDictionary<ulong, int[]>();

        /// <summary>Improved Perlin noise in [-1, 1]; 0 at integer lattice points.</summary>
        public static float Perlin3(Vec3 p, ulong seed)
        {
            int[] perm = Permutations.GetOrAdd(seed, BuildPermutation);
            double x = p.X, y = p.Y, z = p.Z;
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            int xi = (int)((long)fx & 255), yi = (int)((long)fy & 255), zi = (int)((long)fz & 255);
            x -= fx;
            y -= fy;
            z -= fz;
            double u = Fade(x), v = Fade(y), w = Fade(z);

            int a = perm[xi] + yi, aa = perm[a] + zi, ab = perm[a + 1] + zi;
            int b = perm[xi + 1] + yi, ba = perm[b] + zi, bb = perm[b + 1] + zi;

            double result = Lerp(w,
                Lerp(v,
                    Lerp(u, Grad(perm[aa], x, y, z), Grad(perm[ba], x - 1, y, z)),
                    Lerp(u, Grad(perm[ab], x, y - 1, z), Grad(perm[bb], x - 1, y - 1, z))),
                Lerp(v,
                    Lerp(u, Grad(perm[aa + 1], x, y, z - 1), Grad(perm[ba + 1], x - 1, y, z - 1)),
                    Lerp(u, Grad(perm[ab + 1], x, y - 1, z - 1), Grad(perm[bb + 1], x - 1, y - 1, z - 1))));
            return (float)Math.Max(-1.0, Math.Min(1.0, result));
        }

        /// <summary>Fractal sum of <paramref name="octaves"/> Perlin octaves, normalized to [-1, 1].</summary>
        public static float Fbm(Vec3 p, ulong seed, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            double sum = 0, amplitude = 1, total = 0;
            Vec3 q = p;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Perlin3(q, Hash.Combine(seed, (ulong)i));
                total += amplitude;
                amplitude *= gain;
                q = q * lacunarity;
            }

            return total > 0 ? (float)(sum / total) : 0f;
        }

        private static int[] BuildPermutation(ulong seed)
        {
            var source = new int[256];
            for (int i = 0; i < 256; i++)
            {
                source[i] = i;
            }

            var rng = new Rng(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                int t = source[i];
                source[i] = source[j];
                source[j] = t;
            }

            var perm = new int[512];
            for (int i = 0; i < 512; i++)
            {
                perm[i] = source[i & 255];
            }

            return perm;
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static double Lerp(double t, double a, double b) => a + t * (b - a);

        private static double Grad(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}
