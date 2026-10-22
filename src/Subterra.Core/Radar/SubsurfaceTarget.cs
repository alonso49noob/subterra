using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Radar
{
    public enum TargetKind
    {
        Void,
        Sealed,
        LayerBoundary,
        Gas,
        Water,
    }

    /// <summary>Something under the ground that radar, gravimeter or seismic surveys can find. Immutable.</summary>
    public sealed class SubsurfaceTarget
    {
        public string Id { get; init; }

        public TargetKind Kind { get; init; }

        /// <summary>The network it belongs to; null if it is not part of one.</summary>
        public string NetworkId { get; init; }

        /// <summary>Index of the primitive in its network; -1 if it is not from a network.</summary>
        public int PrimitiveIndex { get; init; } = -1;

        /// <summary>Centre of the void, in body coordinates (PQS frame, metres).</summary>
        public Vec3d Position { get; init; }

        public double Lat { get; init; }

        public double Lon { get; init; }

        /// <summary>Size in metres.</summary>
        public double Width { get; init; }

        public double Height { get; init; }

        public double Length { get; init; }

        /// <summary>Metres from the terrain down to the roof of the void.</summary>
        public double Depth { get; init; }

        /// <summary>g/cm3; for a void, the density of the layer around it.</summary>
        public double DensityContrast { get; init; }

        /// <summary>The layer around the target; null where no layer covers its depth.</summary>
        public LayerSpec Host { get; init; }
    }
}
