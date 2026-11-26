namespace Subterra.Core.Model
{
    /// <summary>Kind of underground space; also the id of its cave biome.</summary>
    public enum ZoneKind
    {
        None,
        SkylightShaft,
        TubeGallery,
        TubeChamber,
        TubeBranch,
        FrozenVault,

        /// <summary>A tunnel dug by the player (sub-project 2).</summary>
        Excavation,

        /// <summary>A sealed room lined with big crystals (sub-project 3): reached only by digging.</summary>
        Geode,

        /// <summary>A lake under the ice or in it (part 4): its water, its own science and radar target.</summary>
        SubglacialLake,

        /// <summary>
        /// Not a cave: the water of the body's ocean under its ice or rock (Lucas: science logs for it as for the caves). Only the
        /// science biome uses it.
        /// </summary>
        SubsurfaceOcean,
    }
}
