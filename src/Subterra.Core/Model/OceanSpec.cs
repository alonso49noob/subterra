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
    }
}
