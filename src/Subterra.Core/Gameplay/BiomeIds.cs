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

        /// <summary>True for zone ids and for the names of the body's named sites (null spec: zones only).</summary>
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

            return body != null && body.Sites.Exists(s => s.Name == biomeId);
        }

        public static double Multiplier(string biomeId, BodySpec b) =>
            IsZone(biomeId) ? b.CaveScienceMultiplier : b.SiteScienceMultiplier;

        private static bool IsZone(string id) => id != nameof(ZoneKind.None) && Array.IndexOf(Zones, id) >= 0;
    }
}
