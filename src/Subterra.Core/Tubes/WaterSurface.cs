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

        /// <summary>r, g, b; null = the default water colour.</summary>
        public readonly float[] Colour;

        public WaterSurface(Vec3 centre, Vec3 up, float radius, float[] colour)
        {
            Centre = centre;
            Up = up;
            Radius = radius;
            Colour = colour;
        }
    }
}
