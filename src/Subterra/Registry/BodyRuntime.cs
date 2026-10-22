using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
namespace Subterra
{
    /// <summary>Everything Subterra keeps for one body at run time.</summary>
    public sealed class BodyRuntime
    {
        public BodyRuntime(BodySpec spec, CelestialBody body)
        {
            Spec = spec;
            Body = body;
            Catalog = new NetworkCatalog(spec, body.Radius);
            Strata = new Strata(spec.Strata);
            Geotherm = new Geotherm(spec.Geotherm, Strata);
        }

        public BodySpec Spec { get; }

        public CelestialBody Body { get; }

        public NetworkCatalog Catalog { get; }

        public Strata Strata { get; }

        /// <summary>The body's terrain relief (volcanoes, rilles); null if it has none.</summary>
        public Subterra.Core.Geo.ReliefField Relief { get; set; }

        public Geotherm Geotherm { get; }
    }
}
