using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Radar
{
    /// <summary>Something that can list the subsurface targets near a point (cave networks, layer boundaries, ...).</summary>
    public interface ISubsurfaceTargetSource
    {
        IEnumerable<SubsurfaceTarget> TargetsNear(Vec3d bodyPos, double radius);
    }
}
