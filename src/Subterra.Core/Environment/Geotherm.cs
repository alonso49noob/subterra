using System;
using System.Collections.Generic;
using Subterra.Core.Model;

namespace Subterra.Core.Environment
{
    /// <summary>
    /// Rock temperature: no day/night cycle below the damping depth, then the surface mean plus each
    /// layer's gradient over the part of it above the point; below the last layer its gradient continues.
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
            IReadOnlyList<LayerSpec> column = strata.Column(biome);
            double reached = 0;
            LayerSpec last = null;
            foreach (LayerSpec layer in column)
            {
                double top = Math.Max(0, layer.Top);
                double bottom = Math.Min(layer.Bottom, depth);
                if (bottom > top)
                {
                    t += layer.Gradient * (bottom - top) / 1000.0;
                    reached = Math.Max(reached, bottom);
                }

                last = layer;
            }

            if (last != null && depth > reached && reached >= last.Bottom - 1e-9)
            {
                t += last.Gradient * (depth - reached) / 1000.0;
            }

            return t;
        }
    }
}
