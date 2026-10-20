namespace Subterra.Core.Geo
{
    /// <summary>Terrain height source. In KSP: <c>pqsController.GetSurfaceHeight</c> (without Subterra's cuts).</summary>
    public interface IHeightSampler
    {
        /// <summary>Absolute radius (m from the body centre) of the terrain at the point.</summary>
        double SurfaceRadius(double latDeg, double lonDeg);
    }
}
