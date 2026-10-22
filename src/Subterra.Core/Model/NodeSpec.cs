namespace Subterra.Core.Model
{
    public enum NodeFeature
    {
        None,
        Skylight,
        Chamber,
        End,

        /// <summary>A sealed crystal-lined room (no skylight; reached by digging).</summary>
        Geode,
    }

    /// <summary>A tube graph node: position, roof depth (m under the local terrain), cross-section and feature.</summary>
    public sealed class NodeSpec
    {
        public double Lat { get; set; }

        public double Lon { get; set; }

        /// <summary>Metres from the terrain surface down to the tube roof.</summary>
        public double Depth { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public NodeFeature Feature { get; set; }

        public double SkylightRadius { get; set; }

        /// <summary>
        /// Roof altitude over the body's datum (m), instead of a depth under the local terrain (NaN: unset). For deep
        /// networks under a feature whose terrain changes a lot (the Mohole): the tube keeps its level whatever is above.
        /// </summary>
        public double Altitude { get; set; } = double.NaN;
    }
}
