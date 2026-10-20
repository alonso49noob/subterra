namespace Subterra.Core.Geo
{
    /// <summary>Surface biome source (the body's biome map, never Subterra's cave biomes).</summary>
    public interface IBiomeSampler
    {
        string Biome(double latDeg, double lonDeg);
    }
}
