using System;
using System.Collections.Generic;
using Subterra.Core.Model;

namespace Subterra.Core.Environment
{
    /// <summary>Rock layers by depth and biome: the first layer (in cfg order) covering the depth and the biome wins.</summary>
    public sealed class Strata
    {
        private readonly IReadOnlyList<LayerSpec> layers;
        private readonly Dictionary<string, IReadOnlyList<LayerSpec>> columns = new Dictionary<string, IReadOnlyList<LayerSpec>>();
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
        public IReadOnlyList<LayerSpec> Column(string biome)
        {
            string key = biome ?? "";
            lock (gate)
            {
                if (columns.TryGetValue(key, out IReadOnlyList<LayerSpec> cached))
                {
                    return cached;
                }
            }

            // Walk the layer boundaries; the winner of each interval (by its midpoint) joins the column.
            var bounds = new SortedSet<double> { 0 };
            foreach (LayerSpec layer in layers)
            {
                bounds.Add(Math.Max(0, layer.Top));
                if (!double.IsInfinity(layer.Bottom))
                {
                    bounds.Add(layer.Bottom);
                }
            }

            var column = new List<LayerSpec>();
            var edges = new List<double>(bounds);
            for (int i = 0; i < edges.Count; i++)
            {
                double mid = i + 1 < edges.Count ? (edges[i] + edges[i + 1]) * 0.5 : edges[i] + 1;
                LayerSpec winner = LayerAt(mid, biome);
                if (winner != null && (column.Count == 0 || column[column.Count - 1] != winner))
                {
                    column.Add(winner);
                }
            }

            lock (gate)
            {
                columns[key] = column;
            }

            return column;
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
    }
}
