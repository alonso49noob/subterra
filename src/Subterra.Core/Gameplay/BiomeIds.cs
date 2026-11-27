using System;
using Subterra.Core.Model;
using Subterra.Core.Tubes;

namespace Subterra.Core.Gameplay
{
    /// <summary>
    /// Cave biome ids: a named site's name (its own biome), otherwise the zone kind. Science subjects
    /// use the id; display names come from <c>#Subterra_Biome_&lt;id&gt;</c> or the site's display name.
    /// </summary>
    public static class BiomeIds
    {
        private static readonly string[] Zones = Array.ConvertAll((ZoneKind[])Enum.GetValues(typeof(ZoneKind)), z => z.ToString());

        public static string For(ZoneInfo z) => z.SiteName ?? (z.Kind == ZoneKind.None ? null : z.Kind.ToString());

        public static string LocKey(string biomeId) => IsZone(biomeId) ? "#Subterra_Biome_" + biomeId : "#Subterra_Site_" + biomeId;

        /// <summary>Prefix of the biomes of dug holes: the rock layer they are in (Lucas: not "Dug Tunnel" at a body's core).</summary>
        public const string LayerPrefix = "Layer_";

        /// <summary>The biome id of a hole dug in <paramref name="layerName"/>.</summary>
        public static string ForLayer(string layerName) => LayerPrefix + layerName;

        /// <summary>The layer of a dug hole's biome id, or null.</summary>
        public static LayerSpec LayerOf(string biomeId, BodySpec body) =>
            body != null && biomeId != null && biomeId.StartsWith(LayerPrefix, StringComparison.Ordinal)
                ? body.Strata.Find(l => LayerPrefix + l.Name == biomeId)
                    ?? body.Inclusions.Find(i => i.Layer != null && LayerPrefix + i.Layer.Name == biomeId)?.Layer
                : null;

        /// <summary>True for zone ids, the names of the body's named sites and its layers' dug-hole biomes (null spec: zones only).</summary>
        public static bool IsSubterra(string biomeId, BodySpec body = null)
        {
            if (string.IsNullOrEmpty(biomeId))
            {
                return false;
            }

            if (IsZone(biomeId))
            {
                return true;
            }

            return body != null && (body.Sites.Exists(s => s.Name == biomeId) || LayerOf(biomeId, body) != null);
        }

        public static double Multiplier(string biomeId, BodySpec b) =>
            IsZone(biomeId) || biomeId.StartsWith(LayerPrefix, StringComparison.Ordinal) ? b.CaveScienceMultiplier : b.SiteScienceMultiplier;

        private static bool IsZone(string id) => id != nameof(ZoneKind.None) && Array.IndexOf(Zones, id) >= 0;
    }
}
