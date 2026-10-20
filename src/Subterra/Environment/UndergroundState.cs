using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra
{
    /// <summary>Where a vessel (or the camera) is relative to Subterra's caves.</summary>
    public struct UndergroundState
    {
        public bool Inside;
        public TubeNetwork Net;
        public Vec3 Local;

        /// <summary>Metres under the terrain surface.</summary>
        public double Depth;

        /// <summary>0–1 sky visibility.</summary>
        public float Sky;

        public ZoneInfo Zone;

        /// <summary>Surface biome above the point.</summary>
        public string Biome;

        /// <summary>Rock layer name at the point.</summary>
        public string Layer;

        public double? RockTemp;

        public static UndergroundState Outside => default;

        public override string ToString() =>
            Inside ? $"inside {Net?.Id}, {Zone.Kind}, depth {Depth:F0} m, sky {Sky:F2}, layer {Layer}, rock {RockTemp:F1} K" : "outside";
    }
}
