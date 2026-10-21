using System;
using System.Collections.Generic;
using Subterra.Core.Model;

namespace Subterra.Core.Environment
{
    /// <summary>Rock layers by depth and biome: the first layer (in cfg order) covering the depth and the biome wins.</summary>
    public sealed class Strata
    {
        private readonly IReadOnlyList<LayerSpec> layers;
        private readonly Dictionary<string, Entry> columns = new Dictionary<string, Entry>();
        private readonly object gate = new object();

        public Strata(IReadOnlyList<LayerSpec> layers)
        {
            this.layers = layers ?? new List<LayerSpec>();
        }

        public IReadOnlyList<LayerSpec> Layers => layers;

        public LayerSpec LayerAt(double depth, string biome)
        {
            foreach (LayerSpec layer in layers)
            {
                if (layer.Top <= depth && depth < layer.Bottom && Applies(layer, biome))
                {
                    return layer;
                }
            }

            return null;
        }

        /// <summary>The layers seen going down at a point of <paramref name="biome"/>, top first.</summary>
        public IReadOnlyList<LayerSpec> Column(string biome) => Get(biome).Column;

        /// <summary>
        /// The depth intervals of <paramref name="biome"/>'s column, top first, each with the layer that
        /// wins it (consecutive intervals of one layer merged; gaps without a layer left out). The last
        /// bottom may be infinite.
        /// </summary>
        public IReadOnlyList<LayerSpan> Spans(string biome) => Get(biome).Spans;

        private Entry Get(string biome)
        {
            string key = biome ?? "";
            lock (gate)
            {
                if (columns.TryGetValue(key, out Entry cached))
                {
                    return cached;
                }
            }

            // Walk the layer boundaries; the winner of each interval (by its midpoint) owns it.
            var bounds = new SortedSet<double> { 0 };
            foreach (LayerSpec layer in layers)
            {
                bounds.Add(Math.Max(0, layer.Top));
                if (!double.IsInfinity(layer.Bottom))
                {
                    bounds.Add(layer.Bottom);
                }
            }

            var spans = new List<LayerSpan>();
            var column = new List<LayerSpec>();
            var edges = new List<double>(bounds);
            for (int i = 0; i < edges.Count; i++)
            {
                double top = edges[i];
                bool open = i + 1 == edges.Count;
                double mid = open ? top + 1 : (top + edges[i + 1]) * 0.5;
                LayerSpec winner = LayerAt(mid, biome);
                if (winner == null)
                {
                    continue;
                }

                // Below every finite boundary the winner runs to its own (infinite) bottom.
                double bottom = open ? winner.Bottom : edges[i + 1];
                int last = spans.Count - 1;
                if (last >= 0 && spans[last].Layer == winner && spans[last].Bottom == top)
                {
                    spans[last] = new LayerSpan(spans[last].Top, bottom, winner);
                }
                else
                {
                    spans.Add(new LayerSpan(top, bottom, winner));
                }

                if (column.Count == 0 || column[column.Count - 1] != winner)
                {
                    column.Add(winner);
                }
            }

            var entry = new Entry(column, spans);
            lock (gate)
            {
                columns[key] = entry;
            }

            return entry;
        }

        private static bool Applies(LayerSpec layer, string biome)
        {
            if (layer.Where == null || layer.Where.Count == 0)
            {
                return true;
            }

            foreach (string where in layer.Where)
            {
                if (where == "*" || string.Equals(where, biome, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class Entry
        {
            public Entry(IReadOnlyList<LayerSpec> column, IReadOnlyList<LayerSpan> spans)
            {
                Column = column;
                Spans = spans;
            }

            public IReadOnlyList<LayerSpec> Column { get; }

            public IReadOnlyList<LayerSpan> Spans { get; }
        }
    }

    /// <summary>A depth interval (m) of a column and the layer that wins it.</summary>
    public readonly struct LayerSpan
    {
        public readonly double Top;
        public readonly double Bottom;
        public readonly LayerSpec Layer;

        public LayerSpan(double top, double bottom, LayerSpec layer)
        {
            Top = top;
            Bottom = bottom;
            Layer = layer;
        }
    }
}
