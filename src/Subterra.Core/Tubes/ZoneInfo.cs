using Subterra.Core.Model;

namespace Subterra.Core.Tubes
{
    /// <summary>Where in the underground a point is: zone kind, named site (if any) and network id.</summary>
    public readonly struct ZoneInfo
    {
        public readonly ZoneKind Kind;
        public readonly string SiteName;
        public readonly string NetworkId;

        public ZoneInfo(ZoneKind kind, string siteName, string networkId)
        {
            Kind = kind;
            SiteName = siteName;
            NetworkId = networkId;
        }

        public static ZoneInfo None => default;

        public override string ToString() => SiteName == null ? $"{Kind} ({NetworkId})" : $"{Kind} at {SiteName} ({NetworkId})";
    }
}
