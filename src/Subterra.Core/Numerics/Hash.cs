namespace Subterra.Core.Numerics
{
    /// <summary>Stable 64-bit hashes for seeds (never string.GetHashCode, which changes between runtimes).</summary>
    public static class Hash
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const ulong Golden = 0x9E3779B97F4A7C15UL;

        /// <summary>FNV-1a 64 over the UTF-16 code units of <paramref name="text"/> (low byte, then high byte).</summary>
        public static ulong Of(string text)
        {
            ulong hash = FnvOffset;
            if (text == null)
            {
                return hash;
            }

            foreach (char c in text)
            {
                hash = (hash ^ (byte)c) * FnvPrime;
                hash = (hash ^ (byte)(c >> 8)) * FnvPrime;
            }

            return hash;
        }

        /// <summary>SplitMix64 finalizer.</summary>
        public static ulong Mix(ulong x)
        {
            x ^= x >> 30;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return x;
        }

        /// <summary>Order-sensitive combination of several values.</summary>
        public static ulong Combine(params ulong[] values)
        {
            ulong hash = Golden;
            foreach (ulong value in values)
            {
                hash = Mix(hash + Golden + Mix(value));
            }

            return hash;
        }
    }
}
