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
    }
}
