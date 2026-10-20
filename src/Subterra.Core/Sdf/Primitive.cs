using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>One void shape of a network. Distances are approximate but never overestimate much near the surface.</summary>
    public abstract class Primitive
    {
        /// <summary>Extra margin around the void in <see cref="Bounds"/> (plus the roughness).</summary>
        public const float BoundsMargin = 8f;

        private Aabb core;
        private float roughness;

        /// <summary>Box around the void plus margin and roughness; outside it the primitive counts as rock.</summary>
        public Aabb Bounds { get; private set; }

        public ZoneKind Zone { get; set; }

        /// <summary>Wall noise amplitude (m); grows <see cref="Bounds"/> by the same amount.</summary>
        public float Roughness
        {
            get => roughness;
            set
            {
                roughness = value;
                Bounds = core.Expand(value);
            }
        }

        /// <summary>Wall noise feature size (m).</summary>
        public float NoiseScale { get; set; } = 20f;

        /// <summary>Typical size of the cross-section (m): sets the smooth-union blend with neighbours.</summary>
        public float Width { get; protected set; }

        /// <summary>Index of the path point (or node) it came from; -1 if none.</summary>
        public int Source { get; set; } = -1;

        public abstract float Distance(Vec3 p);

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
