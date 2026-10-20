using System;
using System.Collections.Generic;
using Subterra.Core.Model;

namespace Subterra.Core.Gameplay
{
    /// <summary>Resource abundance underground: the best matching ENRICHMENT rule, or the surface value.</summary>
    public static class Enrichment
    {
        public static double Apply(IEnumerable<EnrichmentSpec> specs, string resource, ZoneKind zone, string layer, double baseAbundance)
        {
            bool matched = false;
            double best = 0;
            foreach (EnrichmentSpec spec in specs)
            {
                if (!string.Equals(spec.Resource, resource, StringComparison.OrdinalIgnoreCase)
                    || spec.Zone.HasValue && spec.Zone.Value != zone
                    || spec.Layer != null && !string.Equals(spec.Layer, layer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double value = Math.Max(baseAbundance * spec.Multiplier, spec.MinAbundance);
                best = matched ? Math.Max(best, value) : value;
                matched = true;
            }

            return matched ? best : baseAbundance;
        }
    }
}
