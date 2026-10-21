namespace Subterra.Core.Numerics
{
    /// <summary>Deterministic xorshift64* generator seeded through SplitMix64. Not thread-safe.</summary>
    public sealed class Rng
    {
        private ulong state;

        public Rng(ulong seed)
        {
            state = Hash.Mix(seed + 0x9E3779B97F4A7C15UL);
            if (state == 0)
            {
                state = 0x9E3779B97F4A7C15UL;
            }
        }

        public ulong NextULong()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 2685821657736338717UL;
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform in [min, max) (min when max ≤ min, or when either end or the width is NaN or infinite).</summary>
        public double Range(double min, double max)
        {
            // Written so NaN fails the test: the redraw loop below would never end on NaN or infinity.
            double width = max - min;
            if (!(width > 0) || double.IsInfinity(width))
            {
                return min;
            }

            while (true)
            {
                // Rounding can land exactly on max; draw again in that (rare) case.
                double value = min + (max - min) * NextDouble();
                if (value < max)
                {
                    return value;
                }
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            return minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
        }
    }
}
