using System;

namespace Subterra.Core.Excavation
{
    /// <summary>How fast rock gives way, and how deep a hole can stay open.</summary>
    public static class DrillMath
    {
        /// <summary>Standard gravity (m/s²), for <see cref="VoidLimit"/> from a body's surface gravity in g.</summary>
        public const double G0 = 9.80665;

        /// <summary>
        /// Metres per second through rock of <paramref name="hardness"/> (0–1) at <paramref name="rockTemp"/> K:
        /// <c>baseRate · (1 − hardness)²</c>, falling linearly to 0 between 80 % and 100 % of the tool's
        /// <paramref name="maxTemp"/>. Unknown rock temperature counts as cool.
        /// </summary>
        public static double Rate(double baseRate, double hardness, double? rockTemp, double maxTemp)
        {
            double h = Math.Max(0, Math.Min(1, hardness));
            double rate = baseRate * (1 - h) * (1 - h);
            if (rockTemp.HasValue && maxTemp > 0)
            {
                double t = (rockTemp.Value - 0.8 * maxTemp) / (0.2 * maxTemp);
                rate *= 1 - Math.Max(0, Math.Min(1, t));
            }

            return Math.Max(0, rate);
        }

        /// <summary>
        /// Depth (m) below which an open void closes: rock strength over the lithostatic gradient,
        /// <c>σ / (ρ g)</c>. <paramref name="densityGcc"/> in g/cm³, <paramref name="gravity"/> in m/s².
        /// Kerbin (2.7 g/cm³, 9.81 m/s², 150 MPa) ≈ 5.7 km; the Mun (1.63 m/s²) ≈ 34 km.
        /// </summary>
        public static double VoidLimit(double strengthMPa, double densityGcc, double gravity)
        {
            if (densityGcc <= 0 || gravity <= 0)
            {
                return double.PositiveInfinity;
            }

            return strengthMPa * 1e6 / (densityGcc * 1000 * gravity);
        }
    }
}
