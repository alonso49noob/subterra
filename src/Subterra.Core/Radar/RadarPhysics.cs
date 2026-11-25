using System;
using System.Collections.Generic;
using Subterra.Core.Environment;
using Subterra.Core.Model;

namespace Subterra.Core.Radar
{
    /// <summary>Pure radar physics: two-way loss through layers, range, resolution and Fresnel footprints.</summary>
    public static class RadarPhysics
    {
        /// <summary>Speed of light in vacuum, m/s.</summary>
        public const double C = 299792458;

        // decidido por Claude, revisar: depths no span covers use the rock default (0.2 dB/m at 60 MHz, scaled with f).
        private static readonly LayerSpec RockDefault = new LayerSpec { Density = 2.6 };

        /// <summary>Two-way loss in dB down to the given depth: 2 · Σ α(f) · thickness, from 0 to depth.</summary>
        public static double TwoWayLossDb(IReadOnlyList<LayerSpan> spans, double depth, double fMHz)
        {
            double loss = 0;
            foreach (var seg in Segments(spans, fMHz))
            {
                if (seg.Top >= depth)
                {
                    break;
                }

                loss += 2 * seg.Alpha * (Math.Min(seg.Bottom, depth) - seg.Top);
            }

            return loss;
        }

        /// <summary>Depth where the two-way loss reaches the dynamic range (dB), interpolated inside the span.</summary>
        public static double MaxDepth(IReadOnlyList<LayerSpan> spans, double fMHz, double dynamicRangeDb)
        {
            double loss = 0;
            foreach (var seg in Segments(spans, fMHz))
            {
                double rate = 2 * seg.Alpha;
                double segLoss = rate * (seg.Bottom - seg.Top);
                if (rate > 0 && loss + segLoss >= dynamicRangeDb)
                {
                    return seg.Top + (dynamicRangeDb - loss) / rate;
                }

                loss += segLoss;
            }

            return double.PositiveInfinity;
        }

        /// <summary>Vertical resolution in m: C / (2 · f · √ε).</summary>
        public static double VerticalResolution(double fMHz, double eps)
        {
            return C / (2 * fMHz * 1e6 * Math.Sqrt(eps));
        }

        /// <summary>First Fresnel zone radius from orbit: √(2 · λ · h).</summary>
        public static double FresnelOrbital(double fMHz, double altitude)
        {
            double lambda = C / (fMHz * 1e6);
            return Math.Sqrt(2 * lambda * altitude);
        }

        /// <summary>Fresnel zone radius at depth d inside the ground: √(λm · d / 2), λm = C / (f · √ε).</summary>
        public static double FresnelGround(double fMHz, double eps, double depth)
        {
            double lambdaM = C / (fMHz * 1e6 * Math.Sqrt(eps));
            return Math.Sqrt(lambdaM * depth / 2);
        }

        private struct Segment
        {
            public double Top;
            public double Bottom;
            public double Alpha;
        }

        // Contiguous segments from depth 0 to +∞ with their one-way attenuation (dB/m); gaps use the rock default.
        private static IEnumerable<Segment> Segments(IReadOnlyList<LayerSpan> spans, double fMHz)
        {
            double cursor = 0;
            double gapAlpha = Materials.Attenuation(RockDefault, fMHz);
            if (spans != null)
            {
                for (int i = 0; i < spans.Count; i++)
                {
                    var span = spans[i];
                    if (span.Bottom <= cursor)
                    {
                        continue;
                    }

                    if (span.Top > cursor)
                    {
                        yield return new Segment { Top = cursor, Bottom = span.Top, Alpha = gapAlpha };
                        cursor = span.Top;
                    }

                    yield return new Segment { Top = cursor, Bottom = span.Bottom, Alpha = Materials.Attenuation(span.Layer, fMHz) };
                    cursor = span.Bottom;
                }
            }

            if (!double.IsPositiveInfinity(cursor))
            {
                yield return new Segment { Top = cursor, Bottom = double.PositiveInfinity, Alpha = gapAlpha };
            }
        }
    }
}
