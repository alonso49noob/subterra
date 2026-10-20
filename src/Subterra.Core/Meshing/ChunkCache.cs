using System.Collections.Generic;

namespace Subterra.Core.Meshing
{
    /// <summary>Least-recently-used cache of chunk payloads with a byte budget. Not thread-safe (main thread).</summary>
    public sealed class ChunkCache<T>
    {
        private readonly long budget;
        private readonly Dictionary<ChunkKey, Entry> entries = new Dictionary<ChunkKey, Entry>();
        private long clock;

        public ChunkCache(long budgetBytes)
        {
            budget = budgetBytes;
        }

        public long Bytes { get; private set; }

        public int Count => entries.Count;

        public IEnumerable<ChunkKey> Keys => entries.Keys;

        public void Put(ChunkKey k, T value, long bytes)
        {
            if (entries.TryGetValue(k, out Entry old))
            {
                Bytes -= old.Bytes;
            }

            entries[k] = new Entry { Value = value, Bytes = bytes, LastUse = ++clock };
            Bytes += bytes;
        }

        public bool TryGet(ChunkKey k, out T value)
        {
            if (entries.TryGetValue(k, out Entry entry))
            {
                value = entry.Value;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>Marks the chunk as used now; <paramref name="frame"/> must not go backwards.</summary>
        public void Touch(ChunkKey k, long frame)
        {
            if (entries.TryGetValue(k, out Entry entry))
            {
                if (frame > clock)
                {
                    clock = frame;
                }

                entry.LastUse = clock;
            }
        }

        public bool Remove(ChunkKey k, out T value)
        {
            if (entries.TryGetValue(k, out Entry entry))
            {
                entries.Remove(k);
                Bytes -= entry.Bytes;
                value = entry.Value;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>Removes least-recently-used entries until within budget, never those in <paramref name="keep"/>; returns them.</summary>
        public IEnumerable<KeyValuePair<ChunkKey, T>> EvictOver(ISet<ChunkKey> keep)
        {
            var evicted = new List<KeyValuePair<ChunkKey, T>>();
            if (Bytes <= budget)
            {
                return evicted;
            }

            var candidates = new List<KeyValuePair<ChunkKey, Entry>>();
            foreach (var pair in entries)
            {
                if (keep == null || !keep.Contains(pair.Key))
                {
                    candidates.Add(pair);
                }
            }

            candidates.Sort((a, b) => a.Value.LastUse.CompareTo(b.Value.LastUse));
            foreach (var pair in candidates)
            {
                if (Bytes <= budget)
                {
                    break;
                }

                entries.Remove(pair.Key);
                Bytes -= pair.Value.Bytes;
                evicted.Add(new KeyValuePair<ChunkKey, T>(pair.Key, pair.Value.Value));
            }

            return evicted;
        }

        private sealed class Entry
        {
            public T Value;
            public long Bytes;
            public long LastUse;
        }
    }
}
