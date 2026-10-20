using System;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>
    /// A straight piece of tube from floor centre a to floor centre b: flat floor, elliptical arch,
    /// optional side benches (a lower central channel), flat caps at the ends.
    /// </summary>
    public sealed class TubeSegment : Primitive
    {
        private readonly Vec3 a, axis, upA, upB;
        private readonly float length, widthA, widthB, heightA, heightB, bench;

        public TubeSegment(Vec3 a, Vec3 b, Vec3 upA, Vec3 upB, float widthA, float widthB, float heightA, float heightB, float bench, ZoneKind zone)
        {
            this.a = a;
            Vec3 ab = b - a;
            length = ab.Length;
            axis = length > 0 ? ab / length : new Vec3(1, 0, 0);
            this.upA = upA.Normalized;
            this.upB = upB.Normalized;
            this.widthA = widthA;
            this.widthB = widthB;
            this.heightA = heightA;
            this.heightB = heightB;
            this.bench = bench;
            Zone = zone;
            Width = Math.Min(widthA, widthB);
            B = b;
            SetCoreBounds(BoxAround(a, b, Math.Max(Math.Max(widthA, widthB) * 0.5f, Math.Max(heightA, heightB))));
        }

        public Vec3 A => a;

        public Vec3 B { get; }

        public override float Distance(Vec3 p)
        {
            Vec3 ap = p - a;
            float along = Vec3.Dot(ap, axis);
            float t = length > 0 ? Math.Max(0f, Math.Min(1f, along / length)) : 0f;
            Vec3 c = a + axis * (t * length);

            // Local vertical, made perpendicular to the axis (the tube may slope).
            Vec3 up = Vec3.Lerp(upA, upB, t);
            up = (up - axis * Vec3.Dot(up, axis)).Normalized;
            Vec3 side = Vec3.Cross(up, axis);

            Vec3 pc = p - c;
            float u = Vec3.Dot(pc, side);
            float v = Vec3.Dot(pc, up);

            // Past an end the cross-section turns around the end point: rounded ends (half-domes
            // reaching half a width beyond the node) and seamless joints between consecutive pieces.
            float w = along < 0 ? -along : along > length ? along - length : 0f;
            if (w > 0)
            {
                u = (float)Math.Sqrt(u * u + w * w);
            }

            float hw = (widthA + (widthB - widthA) * t) * 0.5f;
            float height = heightA + (heightB - heightA) * t;
            float hb = bench * height;

            float d2;
            if (bench > 0)
            {
                float archHeight = height - hb;
                float arch = Math.Max(
                    SdfOps.Ellipse(u, v - hb, hw, archHeight),
                    -(v - hb));
                float chan = Math.Max(Math.Abs(u) - 0.7f * hw, Math.Max(-v, v - hb - 0.01f));
                d2 = Math.Min(arch, chan);
            }
            else
            {
                d2 = Math.Max(SdfOps.Ellipse(u, v, hw, height), -v);
            }

            return d2;
        }

        // No noise on the floor (solidified lava is flat), full noise from a fifth of the height up.
        public override float NoiseMask(Vec3 p)
        {
            Vec3 ap = p - a;
            float t = length > 0 ? Math.Max(0f, Math.Min(1f, Vec3.Dot(ap, axis) / length)) : 0f;
            Vec3 up = Vec3.Lerp(upA, upB, t);
            up = (up - axis * Vec3.Dot(up, axis)).Normalized;
            float v = Vec3.Dot(p - (a + axis * (t * length)), up);
            float height = heightA + (heightB - heightA) * t;
            return Math.Max(0f, Math.Min(1f, v / (0.2f * height)));
        }
    }
}
