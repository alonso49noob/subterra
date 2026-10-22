using System.Collections.Generic;

namespace Subterra.Core.Model
{
    public enum ScatterPlacement
    {
        Floor,
        Wall,
        Ceiling,
        SkylightRubble,
    }

    /// <summary>Props scattered over the cave surfaces.</summary>
    public sealed class ScatterSpec
    {
        public string Name { get; set; }

        /// <summary>GameDatabase model urls (without the <c>_lod1</c> suffix).</summary>
        public List<string> Models { get; set; } = new List<string>();

        public ScatterPlacement Placement { get; set; }

        /// <summary>Zones where it appears; empty = all.</summary>
        public List<ZoneKind> Zones { get; set; } = new List<ZoneKind>();

        /// <summary>Instances per 100 m².</summary>
        public double Density { get; set; } = 0.5;

        public Range Scale { get; set; } = new Range(1, 1);

        /// <summary>Bias of sizes towards the small end: scale = min + (max − min)·u^power (1 = even; 3 = mostly small, a few big).</summary>
        public double ScalePower { get; set; } = 1;

        /// <summary>How far a prop sits sunk into its surface, as a fraction of its scale (rocks half buried, not perched).</summary>
        public double Sink { get; set; }

        /// <summary>0 = upright, 1 = follows the surface normal.</summary>
        public double AlignToSurface { get; set; }

        /// <summary>Noise mask frequency (1/m); 0 = no mask.</summary>
        public double NoiseFrequency { get; set; }

        public double NoiseThreshold { get; set; }

        public double Lod0Distance { get; set; } = 100;

        public double Lod1Distance { get; set; } = 400;

        public bool Collide { get; set; }

        /// <summary>Light the props give off (r, g, b; null = none): they are drawn unlit in this colour, as light itself.</summary>
        public float[] Glow { get; set; }

        /// <summary>Range (m) of the point lights placed among the nearest glowing props (0 = no lights, the props only glow).</summary>
        public double GlowLight { get; set; }

        /// <summary>Only where the rock over the cave is at most this thick (m; 0 = anywhere): light through a thin roof.</summary>
        public double MaxRoof { get; set; }

        /// <summary>The glow follows the daylight on the surface above (sunlight through the roof), not a light of its own.</summary>
        public bool Daylight { get; set; }
    }
}
