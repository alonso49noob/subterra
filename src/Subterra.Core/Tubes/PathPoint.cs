using Subterra.Core.Numerics;

namespace Subterra.Core.Tubes
{
    /// <summary>A sample along a tube's centre line.</summary>
    public sealed class PathPoint
    {
        public double Lat { get; set; }

        public double Lon { get; set; }

        /// <summary>Roof depth under the terrain (m).</summary>
        public double Depth { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>Centre of the flat floor, in body coordinates (PQS frame, metres).</summary>
        public Vec3d FloorCenter { get; set; }

        /// <summary>Local vertical (unit).</summary>
        public Vec3d Up { get; set; }

        /// <summary>Absolute terrain radius above the point.</summary>
        public double SurfaceRadius { get; set; }
    }
}
