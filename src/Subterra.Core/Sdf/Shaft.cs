using System;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>A vertical cylinder (skylight shaft) from its bottom centre along up.</summary>
    public sealed class Shaft : Primitive
    {
        private readonly Vec3 bottomCenter, up;
        private readonly float radius, length;

        public Shaft(Vec3 bottomCenter, Vec3 up, float radius, float length)
        {
            this.bottomCenter = bottomCenter;
            this.up = up.Normalized;
            this.radius = radius;
            this.length = length;
            Zone = ZoneKind.SkylightShaft;
            Width = radius * 2f;
            SetCoreBounds(BoxAround(bottomCenter, bottomCenter + this.up * length, radius));
        }

        public Vec3 BottomCenter => bottomCenter;

        public Vec3 Up => up;

        public float Radius => radius;

        public float Length => length;

        public override float Distance(Vec3 p)
        {
            Vec3 q = p - bottomCenter;
            float h = Vec3.Dot(q, up);
            float radial = (q - up * h).Length;
            return Math.Max(radial - radius, Math.Max(-h, h - length));
        }
    }
}
