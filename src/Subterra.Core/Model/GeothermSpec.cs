namespace Subterra.Core.Model
{
    public sealed class GeothermSpec
    {
        /// <summary>Mean surface temperature (K): the rock temperature just below the day/night cycle.</summary>
        public double SurfaceMeanTemp { get; set; } = 250;

        /// <summary>Depth (m) below which there is no day/night cycle.</summary>
        public double DampingDepth { get; set; } = 1.5;
    }
}
