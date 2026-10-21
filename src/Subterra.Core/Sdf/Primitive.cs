using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>
    /// One void shape of a network. Distances are approximate but never overestimate much near the surface.
    /// Immutable once built (init-only properties), so worker threads can read it freely.
    /// </summary>
    public abstract class Primitive
    {
        /// <summary>Extra margin around the void in <see cref="Bounds"/> (plus the roughness).</summary>
        public const float BoundsMargin = 8f;

        private Aabb core;
        private float roughness;

        /// <summary>Box around the void plus margin and roughness; outside it the primitive counts as rock.</summary>
        public Aabb Bounds { get; private set; }

        public ZoneKind Zone { get; init; }

        /// <summary>Wall noise amplitude (m); grows <see cref="Bounds"/> by the same amount.</summary>
        public float Roughness
        {
            get => roughness;
            init
            {
                roughness = value;
                Bounds = core.Expand(value);
            }
        }

        /// <summary>Wall noise feature size (m).</summary>
        public float NoiseScale { get; init; } = 20f;

        /// <summary>Typical size of the cross-section (m): sets the smooth-union blend with neighbours.</summary>
        public float Width { get; protected init; }

        /// <summary>Index of the path point (or node) it came from; -1 if none.</summary>
        public int Source { get; init; } = -1;

        public abstract float Distance(Vec3 p);

        /// <summary>Weight of the wall noise at <paramref name="p"/> (0–1); floors return 0 so they stay flat.</summary>
        public virtual float NoiseMask(Vec3 p) => 1f;

        /// <summary>Primitives of the same group join with a plain min (no blend groove between pieces of one tube).</summary>
        public int Group { get; init; } = -1;

        /// <summary>Sets the box around the void (the margin is added here).</summary>
        protected void SetCoreBounds(Aabb voidBox)
        {
            core = voidBox.Expand(BoundsMargin);
            Bounds = core.Expand(roughness);
        }

        protected static Aabb BoxAround(Vec3 a, Vec3 b, float extent) =>
            new Aabb(Vec3.Min(a, b), Vec3.Max(a, b)).Expand(extent);
    }
}
