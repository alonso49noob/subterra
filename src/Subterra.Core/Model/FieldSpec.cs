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

        public ulong Seed { get; set; } = 1;
    }
}
