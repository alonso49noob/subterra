using System;
using Subterra.Core.Environment;
using Subterra.Core.Model;

namespace Subterra.Core.Excavation
{
    /// <summary>
    /// A narrow vertical hole bored by a drill: only its depth is kept (no geometry). Advancing it walks
    /// through the strata column of the biome, each layer at its own speed.
    /// </summary>
    public static class Borehole
    {
        /// <summary>Longest stretch (m) drilled at one speed before the temperature is looked up again.</summary>
        public const double StepLimit = 25;

        /// <summary>One step of drilling: where it ended and what it is in.</summary>
        public readonly struct Progress
        {
            public Progress(double depth, LayerSpec layer, double? rockTemp, double rate, bool blocked)
            {
                Depth = depth;
                Layer = layer;
                RockTemp = rockTemp;
                Rate = rate;
                Blocked = blocked;
            }

            public double Depth { get; }

            /// <summary>Layer at <see cref="Depth"/>; null outside every layer.</summary>
            public LayerSpec Layer { get; }

            public double? RockTemp { get; }

            /// <summary>Speed (m/s) at <see cref="Depth"/>.</summary>
            public double Rate { get; }

            /// <summary>The drill cannot go deeper: too hot, too hard or at its maximum depth.</summary>
            public bool Blocked { get; }
        }

        /// <summary>
        /// Drills for <paramref name="seconds"/> from <paramref name="depth"/>, crossing layer boundaries on the
        /// way (each part of the step at its layer's speed), never past <paramref name="maxDepth"/>.
        /// </summary>
        public static Progress Advance(double depth, double seconds, double baseRate, double maxTemp, double maxDepth,
            Strata strata, Geotherm geotherm, string biome)
        {
            double left = Math.Max(0, seconds);
            for (int guard = 0; guard < 4096; guard++)
            {
                LayerSpec layer = strata.LayerAt(depth, biome);
                double? temp = geotherm?.Temperature(depth, biome);
                double rate = Rate(layer, temp, baseRate, maxTemp);
                if (depth >= maxDepth || rate <= 0)
                {
                    return new Progress(Math.Min(depth, maxDepth), layer, temp, rate, true);
                }

                if (left <= 0)
                {
                    return new Progress(depth, layer, temp, rate, false);
                }

                // Up to the next layer boundary (or the maximum depth) at this layer's speed, at most StepLimit at
                // a time: the rock warms up with depth inside a layer too.
                double boundary = Math.Min(maxDepth, layer != null && layer.Bottom > depth ? layer.Bottom : maxDepth);
                boundary = Math.Min(boundary, depth + StepLimit);
                double reach = rate * left;
                if (depth + reach < boundary)
                {
                    depth += reach;
                    left = 0;
                }
                else
                {
                    left -= (boundary - depth) / rate;
                    depth = boundary;
                }
            }

            LayerSpec last = strata.LayerAt(depth, biome);
            double? lastTemp = geotherm?.Temperature(depth, biome);
            return new Progress(depth, last, lastTemp, Rate(last, lastTemp, baseRate, maxTemp), false);
        }

        private static double Rate(LayerSpec layer, double? temp, double baseRate, double maxTemp) =>
            DrillMath.Rate(baseRate, layer?.Hardness ?? 0.5, temp, maxTemp);
    }
}
