using System;
using System.Collections.Generic;

namespace Subterra.Core.Geo
{
    /// <summary>
    /// Heights and biomes sampled beforehand (KSP's samplers only work on the main thread), looked
    /// up by latitude/longitude quantized to 1e-7°. Reading from several threads is safe once filled.
    /// </summary>
    public sealed class PrecomputedSamples : IHeightSampler, IBiomeSampler
    {
        private const double Quantum = 1e7;
        private readonly Dictionary<(long, long), (double radius, string biome)> samples = new Dictionary<(long, long), (double, string)>();

        public int Count => samples.Count;

        public void Add(double lat, double lon, double radius, string biome)
        {
            samples[Key(lat, lon)] = (radius, biome);
        }

        public bool Has(double lat, double lon) => samples.ContainsKey(Key(lat, lon));

        public double SurfaceRadius(double latDeg, double lonDeg) => Get(latDeg, lonDeg).radius;

        public string Biome(double latDeg, double lonDeg) => Get(latDeg, lonDeg).biome;

        private (double radius, string biome) Get(double lat, double lon)
        {
            if (samples.TryGetValue(Key(lat, lon), out var sample))
            {
                return sample;
            }

            throw new KeyNotFoundException(FormattableString.Invariant($"no sample at lat {lat}, lon {lon}"));
        }

        private static (long, long) Key(double lat, double lon)
        {
            double wrapped = BodyFrame.WrapLon(lon);
            long latKey = (long)Math.Round(lat * Quantum);
            long lonKey = (long)Math.Round(wrapped * Quantum);
            if (lonKey == -180L * (long)Quantum)
            {
                lonKey = 180L * (long)Quantum;
            }

            return (latKey, lonKey);
        }
    }
}
