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

        public Vec3 FloorCenter => floorCenter;

        public override float Distance(Vec3 p)
        {
            Vec3 q = p - floorCenter;
            float v = Vec3.Dot(q, up);
            Vec3 horizontal = q - up * v;
            // Rotationally symmetric: the ellipse (horizontal distance, height) in the vertical plane.
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
