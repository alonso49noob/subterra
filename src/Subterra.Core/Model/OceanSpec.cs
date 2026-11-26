namespace Subterra.Core.Model
{
    /// <summary>
    /// A body's ocean under its ice (sub-project 4): a shell of water whose top (the underside of the ice) lies
    /// <see cref="CeilingPole"/> m under the datum at the poles and <see cref="CeilingEquator"/> m at the equator.
    /// </summary>
    public sealed class OceanSpec
    {
        public double CeilingPole { get; set; } = 2000;

        public double CeilingEquator { get; set; } = 10000;

        /// <summary>Metres of water from the ceiling down to the sea floor.</summary>
        public double Thickness { get; set; } = 30000;

        /// <summary>kg/m³ (salt water about 1030).</summary>
        public double Density { get; set; } = 1030;

        /// <summary>Kelvin.</summary>
        public double Temperature { get; set; } = 272;

        /// <summary>r, g, b; null = the default water colour.</summary>
        public float[] Colour { get; set; }

        /// <summary>The ceiling is rock, not ice (Tylo's ocean lies under its crust).</summary>
        public bool CeilingRock { get; set; }

        /// <summary>0–1: chance that an ice tower (a giant icicle, like a brinicle) hangs from the ceiling in each
        /// <see cref="Water.OceanFeatures.TowerSpacing"/>-metre cell; 0 = none.</summary>
        public double TowerChance { get; set; }

        /// <summary>Longest tower (m) hanging from the ceiling.</summary>
        public double TowerLength { get; set; } = 250;

        /// <summary>0–1: chance of a pillar from the ceiling down to the sea floor in each
        /// <see cref="Water.OceanFeatures.PillarSpacing"/>-metre cell (Pol's shallow ocean); 0 = none.</summary>
        public double PillarChance { get; set; }

        /// <summary>Widest pillar radius (m) at its middle (they flare where they meet the ceiling and the floor).</summary>
        public double PillarRadius { get; set; } = 120;

        /// <summary>r, g, b tint of the ceiling's underside (Pol's sulphur-yellow ice); null = plain ice.</summary>
        public float[] CeilingTint { get; set; }
    }
}
