using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Procedural
{
    /// <summary>
    /// The low-priority cell requests of the cell scheduler (radar lazy detection), in serving order: cells of the
    /// active body first, nearest to a given direction (the active vessel) first; cells of other bodies after them,
    /// in the order they were asked for. A request is kept once per (body, cell). Picking scans the list, which is
    /// fine at these sizes (a few hundred cells, a pick a second at most). Not thread-safe.
    /// </summary>
    public sealed class CellRequestQueue<T>
    {
        private readonly List<Entry> entries = new List<Entry>();
        private readonly HashSet<(string, CellId)> keys = new HashSet<(string, CellId)>();
        private long sequence;

        public int Count => entries.Count;

        /// <summary>Queues a request; false if that cell of that body is already waiting.</summary>
        public bool Add(string body, CellId cell, CellGrid grid, T payload)
        {
            if (body == null || grid == null || !keys.Add((body, cell)))
            {
                return false;
            }

            entries.Add(new Entry(body, cell, grid.Center(cell), payload, sequence++));
            return true;
        }

        public bool Contains(string body, CellId cell) => keys.Contains((body, cell));

        /// <summary>
        /// Takes the request to serve next: among cells of <paramref name="activeBody"/>, the one whose centre is
        /// nearest the direction <paramref name="direction"/> (body frame; any length, zero means "no preference":
        /// first asked); otherwise the first asked for. False if the queue is empty.
        /// </summary>
        public bool TryTake(string activeBody, Vec3d direction, out string body, out CellId cell, out T payload)
        {
            int best = -1;
            bool bestActive = false;
            double bestDot = double.NegativeInfinity;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                bool active = activeBody != null && e.Body == activeBody;
                if (best >= 0 && bestActive && !active)
                {
                    continue;
                }

                double dot = active ? Vec3d.Dot(e.Center, direction) : 0;
                bool better = best < 0
                    || active && !bestActive
                    || active == bestActive && (dot > bestDot || dot == bestDot && e.Sequence < entries[best].Sequence);
                if (better)
                {
                    best = i;
                    bestActive = active;
                    bestDot = dot;
                }
            }

            if (best < 0)
            {
                body = null;
                cell = default;
                payload = default;
                return false;
            }

            Entry taken = entries[best];
            entries[best] = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            keys.Remove((taken.Body, taken.Cell));
            body = taken.Body;
            cell = taken.Cell;
            payload = taken.Payload;
            return true;
        }

        private readonly struct Entry
        {
            public Entry(string body, CellId cell, Vec3d center, T payload, long sequence)
            {
                Body = body;
                Cell = cell;
                Center = center;
                Payload = payload;
                Sequence = sequence;
            }

            public string Body { get; }

            public CellId Cell { get; }

            public Vec3d Center { get; }

            public T Payload { get; }

            public long Sequence { get; }
        }
    }
}
