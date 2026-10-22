using System.Collections.Generic;

namespace Subterra.Core.Model
{
    /// <summary>A tube network as a graph: a named site from cfg or a generated procedural network.</summary>
    public sealed class SiteSpec
    {
        public string Name { get; set; }

        /// <summary>Literal text or a localization key (<c>#Subterra_…</c>); null for procedural networks.</summary>
        public string DisplayName { get; set; }

        public bool IsNamed { get; set; }

        /// <summary>
        /// Depths are measured from the ground as it was before the body's relief (a tube under a rille keeps its level
        /// where the trench sinks the ground over it). Clipping still uses the ground as it is.
        /// </summary>
        public bool UnderRelief { get; set; }

        /// <summary>The TUBE_FIELD a procedural network came from; null for named sites.</summary>
        public string Field { get; set; }

        public List<NodeSpec> Nodes { get; set; } = new List<NodeSpec>();

        public List<EdgeSpec> Edges { get; set; } = new List<EdgeSpec>();
    }
}
