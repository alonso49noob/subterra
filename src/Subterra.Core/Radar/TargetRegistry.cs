using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Radar
{
    /// <summary>
    /// All the target sources of a body; queries concatenate their answers. Copy-on-write: Near may run on worker
    /// threads while another thread registers a source.
    /// </summary>
    public sealed class TargetRegistry
    {
        private readonly object gate = new object();
        private volatile ISubsurfaceTargetSource[] sources = new ISubsurfaceTargetSource[0];

        public void Register(ISubsurfaceTargetSource source)
        {
            if (source == null)
            {
                return;
            }

            lock (gate)
            {
                ISubsurfaceTargetSource[] old = sources;
                if (System.Array.IndexOf(old, source) >= 0)
                {
                    return;
                }

                var grown = new ISubsurfaceTargetSource[old.Length + 1];
                old.CopyTo(grown, 0);
                grown[old.Length] = source;
                sources = grown;
            }
        }

        public IEnumerable<SubsurfaceTarget> Near(Vec3d bodyPos, double radius)
        {
            // Snapshot now (not at the first MoveNext): a source registered later is not seen by this query.
            return Enumerate(sources, bodyPos, radius);
        }

        private static IEnumerable<SubsurfaceTarget> Enumerate(ISubsurfaceTargetSource[] snapshot, Vec3d bodyPos, double radius)
        {
            foreach (ISubsurfaceTargetSource source in snapshot)
            {
                foreach (SubsurfaceTarget target in source.TargetsNear(bodyPos, radius))
                {
                    yield return target;
                }
            }
        }
    }
}
