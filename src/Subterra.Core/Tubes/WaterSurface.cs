using Subterra.Core.Numerics;

namespace Subterra.Core.Tubes
{
    /// <summary>A still water level in a cave (network-local frame): the plane through <see cref="Centre"/> normal to
    /// <see cref="Up"/>, out to <see cref="Radius"/>, wherever the cave is open.</summary>
    public readonly struct WaterSurface
    {
        public readonly Vec3 Centre;
        public readonly Vec3 Up;
        public readonly float Radius;

        /// <summary>How deep the water can be under this level (m): below that, a passage under the pool is not in it.</summary>
        public readonly float MaxDepth;

        /// <summary>r, g, b; null = the default water colour.</summary>
        public readonly float[] Colour;

        public WaterSurface(Vec3 centre, Vec3 up, float radius, float[] colour, float maxDepth = float.PositiveInfinity)
        {
            MaxDepth = maxDepth;
            Centre = centre;
            Up = up;
            Radius = radius;
            Colour = colour;
        }
    }
}
