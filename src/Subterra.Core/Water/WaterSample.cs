using Subterra.Core.Numerics;

namespace Subterra.Core.Water
{
    /// <summary>The water at a point: how deep under its surface, how dense, which way is up.</summary>
    public readonly struct WaterSample
    {
        /// <summary>Metres under the surface (negative: above it, but within the water's reach).</summary>
        public readonly double Depth;

        /// <summary>kg/m³.</summary>
        public readonly double Density;

        /// <summary>The surface's normal (network-local frame).</summary>
        public readonly Vec3 Up;

        /// <summary>r, g, b; null = the default water colour.</summary>
        public readonly float[] Colour;

        public WaterSample(double depth, double density, Vec3 up, float[] colour)
        {
            Depth = depth;
            Density = density;
            Up = up;
            Colour = colour;
        }

        public bool Under => Depth > 0;
    }
}
