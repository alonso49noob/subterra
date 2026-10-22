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

        /// <summary>
        /// Where the tube comes out into a trench or a pit (under a relief): the ground is opened along the tube's own
        /// shape, without a shaft (radius: how far the opening may reach).
        /// </summary>
        Portal,
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

        /// <summary>A skylight shaped like a cenote: lobed rim, bell-shaped (overhanging) walls, the ground cut along it.</summary>
        public bool Cenote { get; set; }

        /// <summary>
        /// Roof altitude over the body's datum (m), instead of a depth under the local terrain (NaN: unset). For deep
        /// networks under a feature whose terrain changes a lot (the Mohole): the tube keeps its level whatever is above.
        /// </summary>
        public double Altitude { get; set; } = double.NaN;

        /// <summary>Still water filling the cave at the node up to this height over its floor (m; 0 = dry): a cenote, an
        /// underground lake. Visual (swimming comes with part 4); it spreads through the cave within 1.5 widths.</summary>
        public double Water { get; set; }

        /// <summary>How far that water spreads from the node (m; 0 = 1.5 widths): one level over a whole lake of rooms.</summary>
        public double WaterReach { get; set; }

        /// <summary>Colour of that water (r, g, b; null = clear dark green-blue).</summary>
        public float[] WaterColour { get; set; }
    }
}
