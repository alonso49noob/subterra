namespace Subterra.Core.Model
{
    /// <summary>A tube between two nodes of a site.</summary>
    public sealed class EdgeSpec
    {
        public int From { get; set; }

        public int To { get; set; }

        /// <summary>0–1: lateral wander of the path.</summary>
        public double Sinuosity { get; set; } = 0.3;

        /// <summary>Wall noise amplitude relative to the width.</summary>
        public double Roughness { get; set; } = 0.05;

        /// <summary>Height of the side lava benches as a fraction of the tube height (cfg key <c>bench</c>); 0 = none.</summary>
        public double BenchHeight { get; set; }
    }
}
