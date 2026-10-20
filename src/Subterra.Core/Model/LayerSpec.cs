using System.Collections.Generic;

namespace Subterra.Core.Model
{
    /// <summary>A rock layer between two depths (m), optionally limited to some biomes.</summary>
    public sealed class LayerSpec
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public double Top { get; set; }

        public double Bottom { get; set; }

        /// <summary>0–1, relative.</summary>
        public double Hardness { get; set; }

        /// <summary>g/cm³.</summary>
        public double Density { get; set; }

        /// <summary>Geothermal gradient in K/km.</summary>
        public double Gradient { get; set; }

        /// <summary>RGB, 0–1.</summary>
        public float[] Tint { get; set; } = { 0.5f, 0.5f, 0.5f };

        /// <summary>Biomes where the layer applies; empty or <c>*</c> = everywhere.</summary>
        public List<string> Where { get; set; } = new List<string>();
    }
}
