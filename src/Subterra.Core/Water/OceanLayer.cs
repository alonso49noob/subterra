using System;
using System.Collections.Generic;
using Subterra.Core.Environment;
using Subterra.Core.Model;

namespace Subterra.Core.Water
{
    /// <summary>
    /// A body's ocean under ice or rock as a layer of its column (Lucas: Vall's subsurface ocean as a layer). The layers of a
    /// body are set at fixed depths, but the ocean's ceiling is not (thin at the poles, thick at the equator), so it is put
    /// into the column of each place: from its ceiling down to its floor, over whatever layers the cfg has there (they go on
    /// under the floor). Water: no hardness, the ocean's temperature, water to pump, its own science.
    /// </summary>
    public static class OceanLayer
    {
        /// <summary>Name of the ocean's layer (its science subject too).</summary>
        public const string Name = "SubsurfaceOcean";

        /// <summary>Water a well pump draws from a hole into the ocean (units/s). [Claude, revisar]</summary>
        public const double WaterYield = 1.0;

        private static readonly Dictionary<OceanSpec, LayerSpec> Layers = new Dictionary<OceanSpec, LayerSpec>();

        /// <summary>The ocean's layer (one per ocean).</summary>
        public static LayerSpec For(OceanSpec spec)
        {
            lock (Layers)
            {
                if (!Layers.TryGetValue(spec, out LayerSpec layer))
                {
                    float[] c = spec.Colour ?? new[] { 0.05f, 0.12f, 0.20f };
                    layer = new LayerSpec
                    {
                        Name = Name,
                        DisplayName = "#Subterra_Layer_Ocean",
                        Top = 0,
                        Bottom = double.PositiveInfinity,
                        Hardness = 0,
                        Density = spec.Density / 1000,
                        Gradient = 0,
                        Tint = new[] { c[0] * 3, c[1] * 3, c[2] * 3 },
                        Texture = "Ice",
                    };
                    layer.Yields["Water"] = WaterYield;
                    Layers[spec] = layer;
                }

                return layer;
            }
        }

        /// <summary>The ocean's depths (m under the terrain) where the terrain's radius is <paramref name="surfaceRadius"/>.</summary>
        public static (double Top, double Bottom) Depths(OceanShell ocean, double latDeg, double surfaceRadius)
        {
            double top = Math.Max(0, surfaceRadius - ocean.CeilingRadius(latDeg));
            return (top, top + ocean.Spec.Thickness);
        }

        /// <summary>
        /// <paramref name="spans"/> (a column, top first) with the ocean from <paramref name="top"/> to
        /// <paramref name="bottom"/>: the spans it overlaps are cut round it.
        /// </summary>
        public static List<LayerSpan> Insert(IReadOnlyList<LayerSpan> spans, LayerSpec ocean, double top, double bottom)
        {
            var result = new List<LayerSpan>();
            bool placed = false;
            foreach (LayerSpan span in spans)
            {
                if (!placed && span.Top >= top)
                {
                    result.Add(new LayerSpan(top, bottom, ocean));
                    placed = true;
                }

                if (span.Top < top)
                {
                    result.Add(new LayerSpan(span.Top, Math.Min(span.Bottom, top), span.Layer));
                }

                if (span.Bottom > bottom)
                {
                    if (!placed)
                    {
                        result.Add(new LayerSpan(top, bottom, ocean));
                        placed = true;
                    }

                    result.Add(new LayerSpan(Math.Max(span.Top, bottom), span.Bottom, span.Layer));
                }
            }

            if (!placed)
            {
                result.Add(new LayerSpan(top, bottom, ocean));
            }

            return result;
        }

        /// <summary>The layer at <paramref name="depth"/>: the ocean in its range, else what the strata have there.</summary>
        public static LayerSpec LayerAt(Strata strata, string biome, double depth, LayerSpec ocean, double top, double bottom) =>
            depth >= top && depth < bottom ? ocean : strata.LayerAt(depth, biome);
    }
}
