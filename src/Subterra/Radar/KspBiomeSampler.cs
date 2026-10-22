using Subterra.Core.Geo;

namespace Subterra
{
    /// <summary>
    /// <see cref="IBiomeSampler"/> over a body's KSP biome map. Target sources may call it from worker threads;
    /// <see cref="KspSampler.Biome"/> takes the lock that makes that safe.
    /// </summary>
    internal sealed class KspBiomeSampler : IBiomeSampler
    {
        private readonly CelestialBody body;

        public KspBiomeSampler(CelestialBody body)
        {
            this.body = body;
        }

        public string Biome(double latDeg, double lonDeg)
        {
            return KspSampler.Biome(body, latDeg, lonDeg);
        }
    }
}
