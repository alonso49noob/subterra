using System.Collections.Generic;

namespace Subterra.Core.Model
{
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

        public ulong Seed { get; set; } = 1;
    }
}
