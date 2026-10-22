using Subterra.Core.Geo;

namespace Subterra
{
    /// <summary>
    /// <see cref="IBiomeSampler"/> over a body's KSP biome map. Target sources may call it from worker threads.
    /// KSP's CBAttributeMapSO.GetAtt keeps scratch state in instance fields (bilinear coordinates and neighbour
    /// colours), so it is not thread-safe: calls made through this class are serialised by one lock. Calls made
    /// directly through <see cref="KspSampler.Biome"/> on the main thread are not covered by that lock.
    /// </summary>
    internal sealed class KspBiomeSampler : IBiomeSampler
    {
        // One lock for all bodies: biome lookups are cheap and rare (decidido por Claude, revisar).
        private static readonly object Gate = new object();

        private readonly CelestialBody body;

        public KspBiomeSampler(CelestialBody body)
        {
            this.body = body;
        }

        public string Biome(double latDeg, double lonDeg)
        {
            lock (Gate)
            {
                return KspSampler.Biome(body, latDeg, lonDeg);
            }
        }
    }
}
