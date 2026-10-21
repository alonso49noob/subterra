using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>
    /// A field that knows how far its sign is certain, so <see cref="Meshing.SurfaceNets"/> can skip
    /// evaluating blocks of corners. Only that optimisation relies on it (hence internal).
    /// </summary>
    internal interface IBoundedSdf : ISdf
    {
        /// <summary>
        /// A value with the sign of <see cref="ISdf.Distance"/> at <paramref name="p"/> whose magnitude is a
        /// lower bound of the distance to where the field changes sign. Equal to the distance when every
        /// primitive that could matter was evaluated; smaller when some were skipped by their bounds
        /// (their voids may be nearer than the evaluated field says). Never used as a field value.
        /// </summary>
        float SignBound(Vec3 p);
    }
}
