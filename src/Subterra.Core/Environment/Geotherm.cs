using System;
using System.Collections.Generic;
using Subterra.Core.Model;

namespace Subterra.Core.Environment
{
    /// <summary>
    /// Rock temperature: no day/night cycle below the damping depth, then the surface mean plus each
    /// layer's gradient over the depths above the point where it wins (see <see cref="Strata.Spans"/>);
    /// below the last layer its gradient continues.
    /// </summary>
    public sealed class Geotherm
    {
        private readonly GeothermSpec spec;
        private readonly Strata strata;

        public Geotherm(GeothermSpec spec, Strata strata)
        {
            this.spec = spec ?? new GeothermSpec();
            this.strata = strata;
        }

        /// <summary>Kelvin at <paramref name="depth"/> metres under the terrain; null above the damping depth.</summary>
        public double? Temperature(double depth, string biome)
        {
            if (depth < spec.DampingDepth)
            {
                return null;
            }

            double t = spec.SurfaceMeanTemp;
            LayerSpec last = null;
            double lastBottom = 0;
            // Each layer only over the depths it wins, so overlapping layers are not counted twice.
            foreach (LayerSpan span in strata.Spans(biome))
            {
                double bottom = Math.Min(span.Bottom, depth);
                if (bottom > span.Top)
                {
                    t += span.Layer.Gradient * (bottom - span.Top) / 1000.0;
                }

                last = span.Layer;
                lastBottom = span.Bottom;
            }

            if (last != null && depth > lastBottom)
            {
                t += last.Gradient * (depth - lastBottom) / 1000.0;
            }

            return t;
        }
    }
}
