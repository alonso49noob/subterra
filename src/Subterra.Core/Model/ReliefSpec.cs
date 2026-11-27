using System.Collections.Generic;

namespace Subterra.Core.Model
{
    public enum ReliefKind
    {
        /// <summary>A volcano: a cone raised over the terrain with a crater at its top.</summary>
        Volcano,

        /// <summary>A sinuous rille: a trench sunk along a path (a lava tube whose roof fell in), with intact bridges.</summary>
        Rille,

        /// <summary>
        /// Tiger stripes (Lucas: cryovolcanoes like Enceladus's, not a pimple of a dome): a few long, gently curved parallel
        /// fissures, each a trench between two low ridges, jets of ice along them.
        /// </summary>
        Fissures,
    }

    /// <summary>A change to a body's terrain height (cfg node <c>RELIEF</c>).</summary>
    public sealed class ReliefSpec
    {
        public string Name { get; set; }

        public ReliefKind Kind { get; set; }

        // Volcano
        public double Lat { get; set; }

        public double Lon { get; set; }

        /// <summary>Radius (m) of the cone's base.</summary>
        public double Radius { get; set; } = 6000;

        /// <summary>Height (m) the cone adds at its top (before the crater).</summary>
        public double Height { get; set; } = 1500;

        /// <summary>Radius (m) of the crater at the top.</summary>
        public double CraterRadius { get; set; } = 600;

        /// <summary>Depth (m) of the crater under the cone's top.</summary>
        public double CraterDepth { get; set; } = 250;

        // Rille
        /// <summary>Path of the trench (lat, lon), in order.</summary>
        public List<(double lat, double lon)> Path { get; set; } = new List<(double, double)>();

        /// <summary>Width (m) of the trench at the ground.</summary>
        public double Width { get; set; } = 400;

        /// <summary>Depth (m) of the trench at its middle.</summary>
        public double Depth { get; set; } = 60;

        /// <summary>Stretches of intact roof: (fraction of the path's length at their middle, length in m).</summary>
        public List<(double at, double length)> Bridges { get; set; } = new List<(double, double)>();

        // Fissures (also Lat, Lon, Width, Depth; Radius is half their length, Height their ridges')
        /// <summary>How many fissures, and the metres between them.</summary>
        public int Count { get; set; } = 4;

        public double Spacing { get; set; } = 400;

        /// <summary>Compass heading (degrees from north) of the fissures.</summary>
        public double Heading { get; set; } = 30;
    }
}
