using System;

namespace Subterra.Core.Radar
{
    /// <summary>Gravity anomaly of simple buried bodies, straight above them.</summary>
    public static class GravityPhysics
    {
        /// <summary>Gravitational constant, m³ kg⁻¹ s⁻².</summary>
        public const double G = 6.674e-11;

        private const double MGalPerSi = 1e5; // 1 mGal = 1e-5 m/s²
        private const double KgM3PerGcm3 = 1000;

        /// <summary>Anomaly (mGal) over a horizontal cylinder of radius R (m) with its axis at depth z (m): 2πGΔρR²/z.</summary>
        public static double CylinderMGal(double drho, double radius, double z)
        {
            return 2 * Math.PI * G * drho * KgM3PerGcm3 * radius * radius / z * MGalPerSi;
        }

        /// <summary>Anomaly (mGal) over a sphere of radius R (m) with its centre at depth z (m): (4/3)πGΔρR³/z².</summary>
        public static double SphereMGal(double drho, double radius, double z)
        {
            return 4.0 / 3.0 * Math.PI * G * drho * KgM3PerGcm3 * radius * radius * radius / (z * z) * MGalPerSi;
        }
    }
}
