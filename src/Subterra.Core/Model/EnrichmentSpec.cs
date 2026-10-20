namespace Subterra.Core.Model
{
    /// <summary>A resource abundance rule underground: multiplier and floor, optionally per zone and layer.</summary>
    public sealed class EnrichmentSpec
    {
        public string Resource { get; set; }

        /// <summary>Null = any zone.</summary>
        public ZoneKind? Zone { get; set; }

        /// <summary>Null = any layer.</summary>
        public string Layer { get; set; }

        public double Multiplier { get; set; } = 1;

        public double MinAbundance { get; set; }
    }
}
