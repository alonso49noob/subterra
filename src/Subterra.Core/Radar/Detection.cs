namespace Subterra.Core.Radar
{
    public enum DetectionLevel : byte
    {
        None = 0,
        Hint = 1,
        Detected = 2,
    }

    /// <summary>What an instrument makes of one target: how sure, and the depth range it gives. Always finite.</summary>
    public readonly struct Detection
    {
        public Detection(DetectionLevel level, double uncertainty, double depthMin, double depthMax)
        {
            Level = level;
            Uncertainty = uncertainty;
            DepthMin = depthMin;
            DepthMax = depthMax;
        }

        public DetectionLevel Level { get; }

        /// <summary>Horizontal uncertainty in metres (footprint, or depth for the gravimeter).</summary>
        public double Uncertainty { get; }

        public double DepthMin { get; }

        public double DepthMax { get; }

        public static Detection None => default;
    }
}
