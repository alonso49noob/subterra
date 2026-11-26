using System;

namespace Subterra.Core.Environment
{
    /// <summary>
    /// The air below a body's datum, in caves, shafts and pits open to its atmosphere (Lucas: the pressure must grow going
    /// down, and gravity fall). Hydrostatic: <c>d ln p / dz = g(z) / (R_specific · T(z))</c>, written with the scale height at
    /// the datum <c>H₀ = R_specific · T₀ / g₀</c> as <c>d ln p / dz = (g(z)/g₀) · (T₀/T(z)) / H₀</c>. Gravity falls linearly to 0
    /// at the centre (a uniform body) and the air takes the rock's temperature (the geotherm), so the pressure grows ever
    /// more slowly: Kerbin's air is about 17 atm 35 km down and some 500 atm at the core, not the 10²⁷ atm of an isothermal
    /// column. Tabulated once per body.
    /// </summary>
    public sealed class AirColumn
    {
        /// <summary>Metres between table entries.</summary>
        public const double Step = 250;

        /// <summary>Rock temperature gradient (K/m) used where a body has no geotherm.</summary>
        public const double DefaultGradient = 0.02;

        private readonly double[] lnRatio, temperature;

        /// <param name="radius">Body radius (m): depth of the centre.</param>
        /// <param name="scaleHeight">Scale height of the air at the datum (m).</param>
        /// <param name="datumTemp">Air temperature at the datum (K).</param>
        /// <param name="rockTemp">Rock temperature at a depth (K; null where unknown).</param>
        public AirColumn(double radius, double scaleHeight, double datumTemp, Func<double, double?> rockTemp = null)
        {
            Radius = Math.Max(1, radius);
            ScaleHeight = Math.Max(1, scaleHeight);
            DatumTemp = Math.Max(1, datumTemp);
            int n = (int)Math.Ceiling(Radius / Step) + 2;
            lnRatio = new double[n];
            temperature = new double[n];
            double rock0 = rockTemp?.Invoke(2) ?? double.NaN;
            for (int k = 0; k < n; k++)
            {
                double z = Math.Min(Radius, k * Step);
                double? rock = rockTemp?.Invoke(Math.Max(2, z));
                double rise = rock.HasValue && !double.IsNaN(rock0) ? rock.Value - rock0 : DefaultGradient * z;
                temperature[k] = Math.Max(DatumTemp * 0.5, DatumTemp + rise);
                if (k > 0)
                {
                    double zPrev = Math.Min(Radius, (k - 1) * Step);
                    lnRatio[k] = lnRatio[k - 1] + (z - zPrev) * 0.5 * (Integrand(zPrev, temperature[k - 1]) + Integrand(z, temperature[k]));
                }
            }
        }

        public double Radius { get; }

        public double ScaleHeight { get; }

        public double DatumTemp { get; }

        /// <summary>Pressure at <paramref name="depth"/> metres below the datum over the pressure at the datum (1 at 0).</summary>
        public double PressureFactor(double depth) => Math.Exp(Lerp(lnRatio, depth));

        /// <summary>Air temperature (K) at <paramref name="depth"/> metres below the datum.</summary>
        public double Temperature(double depth) => Lerp(temperature, depth);

        /// <summary>Gravity at <paramref name="depth"/> below the datum over the gravity at the datum (uniform body).</summary>
        public static double GravityFactor(double depth, double radius) => Math.Max(0, Math.Min(1, 1 - depth / Math.Max(1, radius)));

        private double Integrand(double z, double t) => GravityFactor(z, Radius) * (DatumTemp / t) / ScaleHeight;

        private double Lerp(double[] table, double depth)
        {
            double d = Math.Max(0, Math.Min(Radius, depth)) / Step;
            int i = Math.Min(table.Length - 2, (int)d);
            double f = d - i;
            return table[i] + (table[i + 1] - table[i]) * f;
        }
    }
}
