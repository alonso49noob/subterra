using Subterra.Core.Geo;

namespace Subterra
{
    /// <summary>
    /// Terrain heights and surface biomes from KSP (main thread only). Heights come from the PQS
    /// (Subterra's cuts do not change them); biomes from the biome map, never ScienceUtil (which
    /// Subterra patches to report cave biomes).
    /// </summary>
    public static partial class KspSampler
    {
        /// <summary>Absolute terrain radius at the point (BodyFrame directions are the PQS frame; checked in M0).</summary>
        public static double SurfaceRadius(CelestialBody body, double lat, double lon) =>
            body.pqsController.GetSurfaceHeight(BodyFrame.Direction(lat, lon).ToKsp());

        public static string Biome(CelestialBody body, double lat, double lon)
        {
            if (body.BiomeMap == null)
            {
                return null;
            }

            CBAttributeMapSO.MapAttribute attribute = body.BiomeMap.GetAtt(lat * BodyFrame.Deg2Rad, lon * BodyFrame.Deg2Rad);
            return attribute?.name;
        }
    }
}
