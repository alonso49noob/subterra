using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Radar
{
    /// <summary>All the target sources of a body; queries concatenate their answers.</summary>
    public sealed class TargetRegistry
    {
        private readonly List<ISubsurfaceTargetSource> sources = new List<ISubsurfaceTargetSource>();

        public void Register(ISubsurfaceTargetSource source)
        {
            if (source != null && !sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        public IEnumerable<SubsurfaceTarget> Near(Vec3d bodyPos, double radius)
        {
            foreach (ISubsurfaceTargetSource source in sources)
            {
                foreach (SubsurfaceTarget target in source.TargetsNear(bodyPos, radius))
                {
                    yield return target;
                }
            }
        }
    }
}
