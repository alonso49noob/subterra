using System;
using System.Collections.Generic;

namespace Subterra.Core.Environment
{
    /// <summary>
    /// The weight of the rock over a point (Lucas: the PresMat should count the three states, gas, liquid and solid, and read
    /// the rock's pressure even on airless bodies, the Mun's core for one). Each layer's density times the gravity at its
    /// depth (falling to nothing at the centre), added up from the surface down.
    /// </summary>
    public static class Lithostatic
    {
        /// <summary>Density (kg/m³) of a layer that gives none.</summary>
        public const double DefaultDensity = 2700;

        /// <summary>Most metres per integration step.</summary>
        public const double MaxStep = 2000;

        /// <summary>
        /// Pressure (kPa) of the rock over a point <paramref name="depth"/> metres under the surface, at full scale.
        /// <paramref name="spans"/> are the column's layers from the top (their depths in metres); past the last, its rock.
        /// </summary>
        public static double PressureKPa(IReadOnlyList<LayerSpan> spans, double depth, double surfaceGravity, double radius)
        {
            if (depth <= 0 || surfaceGravity <= 0 || radius <= 0)
            {
                return 0;
            }

            depth = Math.Min(depth, radius);
            int steps = Math.Max(1, (int)Math.Ceiling(depth / MaxStep));
            double dz = depth / steps, pa = 0;
            for (int i = 0; i < steps; i++)
            {
                double z = (i + 0.5) * dz;
                double g = surfaceGravity * Math.Max(0, 1 - z / radius);
                pa += DensityAt(spans, z) * g * dz;
            }

            return pa / 1000;
        }

        private static double DensityAt(IReadOnlyList<LayerSpan> spans, double z)
        {
            if (spans == null || spans.Count == 0)
            {
                return DefaultDensity;
            }

            LayerSpan last = spans[0];
            foreach (LayerSpan s in spans)
            {
                if (z < s.Top)
                {
                    break;
                }

                last = s;
                if (z < s.Bottom)
                {
                    break;
                }
            }

            // Layer densities are in t/m³.
            return last.Layer != null && last.Layer.Density > 0 ? last.Layer.Density * 1000 : DefaultDensity;
        }
    }
}
