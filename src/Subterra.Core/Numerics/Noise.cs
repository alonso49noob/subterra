using System;
using System.Collections.Concurrent;

namespace Subterra.Core.Numerics
{
    /// <summary>Seeded improved Perlin noise and fBm (single precision). Thread-safe.</summary>
    public static class Noise
    {
        public const int MaxOctaves = 8;
        private static readonly ConcurrentDictionary<ulong, int[]> Permutations = new ConcurrentDictionary<ulong, int[]>();
        private static readonly ConcurrentDictionary<ulong, int[][]> OctaveTables = new ConcurrentDictionary<ulong, int[][]>();

        /// <summary>Improved Perlin noise in [-1, 1]; 0 at integer lattice points.</summary>
        public static float Perlin3(Vec3 p, ulong seed) => Perlin(p.X, p.Y, p.Z, Permutations.GetOrAdd(seed, BuildPermutation));

        /// <summary>Fractal sum of <paramref name="octaves"/> Perlin octaves, normalized to [-1, 1].</summary>
        public static float Fbm(Vec3 p, ulong seed, int octaves, float lacunarity = 2f, float gain = 0.5f) =>
            Fbm(p, Tables(seed), octaves, lacunarity, gain);

        /// <summary>The per-octave permutations of a seed, to keep for hot loops (see <see cref="Fbm(Vec3, int[][], int, float, float)"/>).</summary>
        public static int[][] Tables(ulong seed) => OctaveTables.GetOrAdd(seed, s =>
        {
            var tables = new int[MaxOctaves][];
            for (int i = 0; i < MaxOctaves; i++)
            {
                tables[i] = Permutations.GetOrAdd(Hash.Combine(s, (ulong)i), BuildPermutation);
            }

            return tables;
        });

        public static float Fbm(Vec3 p, int[][] tables, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            octaves = Math.Min(octaves, MaxOctaves);
            float sum = 0, amplitude = 1, total = 0;
            float x = p.X, y = p.Y, z = p.Z;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Perlin(x, y, z, tables[i]);
                total += amplitude;
                amplitude *= gain;
                x *= lacunarity;
                y *= lacunarity;
                z *= lacunarity;
            }

            return total > 0 ? Math.Max(-1f, Math.Min(1f, sum / total)) : 0f;
        }

        private static float Perlin(float x, float y, float z, int[] perm)
        {
            int ix = FastFloor(x), iy = FastFloor(y), iz = FastFloor(z);
            x -= ix;
            y -= iy;
            z -= iz;
            int xi = ix & 255, yi = iy & 255, zi = iz & 255;
            float u = Fade(x), v = Fade(y), w = Fade(z);

            int a = perm[xi] + yi, aa = perm[a] + zi, ab = perm[a + 1] + zi;
            int b = perm[xi + 1] + yi, ba = perm[b] + zi, bb = perm[b + 1] + zi;

            float result = Lerp(w,
                Lerp(v,
                    Lerp(u, Grad(perm[aa], x, y, z), Grad(perm[ba], x - 1, y, z)),
                    Lerp(u, Grad(perm[ab], x, y - 1, z), Grad(perm[bb], x - 1, y - 1, z))),
                Lerp(v,
                    Lerp(u, Grad(perm[aa + 1], x, y, z - 1), Grad(perm[ba + 1], x - 1, y, z - 1)),
                    Lerp(u, Grad(perm[ab + 1], x, y - 1, z - 1), Grad(perm[bb + 1], x - 1, y - 1, z - 1))));
            return Math.Max(-1f, Math.Min(1f, result));
        }

        private static int FastFloor(float f)
        {
            int i = (int)f;
            return f < i ? i - 1 : i;
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

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static float Lerp(float t, float a, float b) => a + t * (b - a);

        private static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}
