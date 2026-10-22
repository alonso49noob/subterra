using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using Subterra.Core.Radar;
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

        /// <summary>Everything the radar can find on this body: cave networks, plus what parts 4 and 5 register.</summary>
        public TargetRegistry Targets { get; } = new TargetRegistry();

        /// <summary>
        /// The target source of the cave networks registered in <see cref="Targets"/> (null until the loader set it):
        /// lazy detection reads the targets of one network at a time through it.
        /// </summary>
        public NetworkTargetSource NetworkTargets { get; internal set; }
    }
}
