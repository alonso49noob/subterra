using System.Collections.Generic;

namespace Subterra.Core.Model
{
    /// <summary>Everything Subterra adds to one celestial body (one or more <c>SUBTERRA_BODY</c> nodes).</summary>
    public sealed class BodySpec
    {
        public string Body { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>Prefix of procedural network ids (<c>MN-0427</c>).</summary>
        public string IdPrefix { get; set; }

        public double CaveScienceMultiplier { get; set; } = 1.5;

        public double SiteScienceMultiplier { get; set; } = 2.0;

        public GeothermSpec Geotherm { get; set; } = new GeothermSpec();

        public List<LayerSpec> Strata { get; set; } = new List<LayerSpec>();

        public List<SiteSpec> Sites { get; set; } = new List<SiteSpec>();

        public List<FieldSpec> Fields { get; set; } = new List<FieldSpec>();

        public List<ScatterSpec> Scatters { get; set; } = new List<ScatterSpec>();

        public List<EnrichmentSpec> Enrichment { get; set; } = new List<EnrichmentSpec>();

        public List<ReliefSpec> Reliefs { get; set; } = new List<ReliefSpec>();

        /// <summary>The ocean under the ice, or null (sub-project 4).</summary>
        public OceanSpec Ocean { get; set; }
    }
}
