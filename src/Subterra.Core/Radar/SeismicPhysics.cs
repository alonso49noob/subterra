using System;

namespace Subterra.Core.Radar
{
    /// <summary>Reach and resolution of the seismic survey.</summary>
    public static class SeismicPhysics
    {
        // decidido por Claude, revisar: values from the spec (§4.3).
        private const double ThumperLimit = 50;
        private const double ChargeBase = 50;
        private const double ChargeCap = 1000;

        /// <summary>Reach in m: min(spread/2, energy limit). chargeKg = 0 means the thumper.</summary>
        public static double Reach(double spreadLength, double chargeKg)
        {
            double limit = chargeKg <= 0
                ? ThumperLimit
                : Math.Min(ChargeBase * Math.Pow(chargeKg, 1.0 / 3.0), ChargeCap);
            return Math.Min(spreadLength / 2, limit);
        }

        /// <summary>Resolution in m: max(mean spacing / 2, depth / 20).</summary>
        public static double Resolution(double meanSpacing, double depth)
        {
            return Math.Max(meanSpacing / 2, depth / 20);
        }
    }
}
