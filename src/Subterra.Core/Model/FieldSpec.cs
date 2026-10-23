using System.Collections.Generic;

namespace Subterra.Core.Model
{
    public enum FieldStyle
    {
        /// <summary>Lava tubes: wide, low trunks that flow downhill, a few branches, skylights where the roof is thin.</summary>
        Lava,

        /// <summary>
        /// Karst (solution caves): narrow, tall, winding passages on two or three levels joined by steep ramps, rooms
        /// where passages meet, now and then a doline (a room open to the sky).
        /// </summary>
        Karst,

        /// <summary>
        /// Tectonic fissures: tall, narrow slots opened along faults (a few straight directions), crossing one another,
        /// rooms where they cross; their tops reach close to the ground, where some open as long thin skylights.
        /// </summary>
        Fissure,

        /// <summary>
        /// Voids in a rubble pile (small moons and comets): small rooms between huge blocks at all depths, linked by
        /// short crooked crawls, many loops, no main passage.
        /// </summary>
        Rubble,

        /// <summary>
        /// Cryovolcanic conduits (icy moons): frozen, hollow pipes winding down from a skylight in a tight spiral to hundreds
        /// of metres, ledges on the way, a big room at the bottom.
        /// </summary>
        Conduit,
    }

    /// <summary>Rules for procedural tube networks spread over some biomes of a body.</summary>
    public sealed class FieldSpec
    {
        public string Name { get; set; }

        public List<string> Biomes { get; set; } = new List<string>();

        /// <summary>Networks per 10 000 km² of allowed biome.</summary>
        public double NetworksPer10k { get; set; } = 1.5;

        public Range Width { get; set; } = new Range(20, 350);

        public Range Length { get; set; } = new Range(2000, 20000);

        public Range RoofDepth { get; set; } = new Range(15, 120);

        public Range SkylightSpacing { get; set; } = new Range(2000, 6000);

        public double BranchChance { get; set; } = 0.25;

        public double ChamberChance { get; set; } = 0.15;

        /// <summary>0–1: fraction of networks with no skylight at all (found by drilling, digging or radar). Chosen per network id.</summary>
        public double HiddenChance { get; set; }

        public FieldStyle Style { get; set; } = FieldStyle.Lava;

        /// <summary>Height of the rooms over the usual (1); more than 1 for vaults (Tylo's cathedrals).</summary>
        public double RoomHeight { get; set; } = 1;

        /// <summary>0–1: share of rooms (chambers) holding still water, a third of their height deep (lakes, pools).</summary>
        public double WaterChance { get; set; }

        /// <summary>Colour of that water (r, g, b; null = clear).</summary>
        public float[] WaterColour { get; set; }

        public ulong Seed { get; set; } = 1;
    }
}
