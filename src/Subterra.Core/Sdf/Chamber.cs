using System;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>A flattened dome: half-ellipsoid (R, H, R) over a flat floor.</summary>
    public sealed class Chamber : Primitive
    {
        private readonly Vec3 floorCenter, up;
        private readonly float radius, height;

        public Chamber(Vec3 floorCenter, Vec3 up, float radius, float height)
        {
            this.floorCenter = floorCenter;
            this.up = up.Normalized;
            this.radius = radius;
            this.height = height;
            Zone = ZoneKind.TubeChamber;
            Width = radius * 2f;
            SetCoreBounds(new Aabb(floorCenter, floorCenter).Expand(Math.Max(radius, height)));
        }

        /// <summary>
        /// Depth (m) of a rounded bowl under the floor (0: a flat floor). Lucas: the lakes looked square, their flat floors
        /// stacked in terraces; a lake's floor is a bowl, deepest in its middle, and its water's edge follows it round.
        /// </summary>
        public float Bowl { get; init; }

        public Vec3 FloorCenter => floorCenter;

        public Vec3 Up => up;

        public float Radius => radius;

        public float Height => height;

        public override float Distance(Vec3 p)
        {
            Vec3 q = p - floorCenter;
            float v = Vec3.Dot(q, up);
            Vec3 horizontal = q - up * v;
            // Rotationally symmetric: the ellipse (horizontal distance, height) in the vertical plane.
            if (Bowl > 0)
            {
                // The dome over the floor's level, a shallower half-ellipsoid under it: one smooth lens.
                return SdfOps.Ellipse(horizontal.Length, v, radius, v >= 0 ? height : Bowl);
            }

            float ellipsoid = SdfOps.Ellipse(horizontal.Length, v, radius, height);
            return Math.Max(ellipsoid, -v);
        }

        public override float NoiseMask(Vec3 p)
        {
            float v = Vec3.Dot(p - floorCenter, up);
            return Math.Max(0f, Math.Min(1f, v / (0.2f * height)));
        }
    }
}
