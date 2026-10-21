using Subterra.Core.Numerics;

namespace Subterra.Core.Tubes
{
    /// <summary>An opening from a tube to the surface (network-local coordinates).</summary>
    public sealed class Skylight
    {
        /// <summary>Centre of the hole on the terrain surface.</summary>
        public Vec3 SurfaceCenter { get; set; }

        public Vec3 Up { get; set; }

        public float Radius { get; set; }

        public double Lat { get; set; }

        public double Lon { get; set; }

        /// <summary>Absolute terrain radius at the centre.</summary>
        public double SurfaceRadius { get; set; }

        /// <summary>Outer edge of the debris rim around the hole.</summary>
        public float RimOuterRadius => Radius * 1.6f + 10f;

        /// <summary>Bottom of the shaft (2 m under the tube's roof apex).</summary>
        public Vec3 ShaftBottom { get; set; }

        /// <summary>Index of the skylight within its network.</summary>
        public int Index { get; set; }

        /// <summary>Who the opening belongs to when it is not the network's own (a dug tunnel's mouth): keeps one rim when the tunnel joins a cave.</summary>
        public string Owner { get; set; }

        /// <summary>A smooth rim (a tunnel mouth a vehicle drives over) instead of a debris rim.</summary>
        public bool Smooth { get; set; }
    }
}
