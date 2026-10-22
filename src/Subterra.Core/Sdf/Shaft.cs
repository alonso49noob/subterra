using System;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Sdf
{
    /// <summary>
    /// A vertical shaft from <see cref="BottomCenter"/> up <see cref="Length"/> metres. Round by default; a cenote's
    /// shaft is lobed (its rim not a circle) and bell-shaped (wider below, the rock overhanging the water), both in its
    /// smooth distance so the ground can be cut along it exactly, and its wall noise fades out towards the top.
    /// </summary>
    public sealed class Shaft : Primitive
    {
        private readonly Vec3 bottomCenter, up, e1, e2;
        private readonly float radius, length;

        public Shaft(Vec3 bottomCenter, Vec3 up, float radius, float length)
        {
            this.bottomCenter = bottomCenter;
            this.up = up.Normalized;
            this.radius = radius;
            this.length = length;
            Vec3 helper = Math.Abs(this.up.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            e1 = Vec3.Cross(this.up, helper).Normalized;
            e2 = Vec3.Cross(e1, this.up);
            Zone = ZoneKind.SkylightShaft;
            Width = radius * 2f;
            SetCoreBounds(BoxAround(bottomCenter, bottomCenter + this.up * length, radius));
        }

        public Vec3 BottomCenter => bottomCenter;

        public Vec3 Up => up;

        public float Radius => radius;

        public float Length => length;

        /// <summary>Radius at the bottom over the radius at the top (1 = straight; 1.8 = a bell).</summary>
        public float Flare
        {
            get => flare;
            init
            {
                flare = Math.Max(1, value);
                SetCoreBounds(BoxAround(bottomCenter, bottomCenter + up * length, radius * flare * (1 + lobes)));
            }
        }

        /// <summary>How far the rim wanders in and out of the circle (fraction of the radius; 0 = round).</summary>
        public float Lobes
        {
            get => lobes;
            init
            {
                lobes = Math.Max(0, Math.Min(0.4f, value));
                SetCoreBounds(BoxAround(bottomCenter, bottomCenter + up * length, radius * flare * (1 + lobes)));
            }
        }

        /// <summary>Phase of the lobes (any number: two shafts with different seeds differ).</summary>
        public float LobeSeed { get; init; }

        /// <summary>Metres from the top down to the ground (0 = unknown): the bell flares under it, and the wall noise is
        /// gone above 1 m under it (fading in over one radius below), where the ground is cut along the smooth shape.</summary>
        public float Ground { get; init; }

        private readonly float flare = 1, lobes;

        public override float Distance(Vec3 p)
        {
            Vec3 q = p - bottomCenter;
            float h = Vec3.Dot(q, up);
            Vec3 across = q - up * h;
            float radial = across.Length;
            float r = radius;
            if (flare > 1)
            {
                // Widest at the bottom, narrowing over the lower three quarters of the part under the ground, straight
                // under the rim.
                float t = Math.Max(0, Math.Min(1, h / (0.75f * (length - Ground))));
                r *= 1 + (flare - 1) * (1 - t * t * (3 - 2 * t));
            }

            if (lobes > 0 && radial > 1e-4f)
            {
                r *= RimRadius(across) / radius;
            }

            return Math.Max(radial - r, Math.Max(-h, h - length));
        }

        /// <summary>Radius of the shaft at the ground (or at its top when the ground is unknown) in the horizontal
        /// <paramref name="direction"/>, lobes included.</summary>
        public float RimRadius(Vec3 direction)
        {
            Vec3 across = direction - up * Vec3.Dot(direction, up);
            if (lobes <= 0 || across.LengthSquared < 1e-8f)
            {
                return radius;
            }

            double a = Math.Atan2(Vec3.Dot(across, e2), Vec3.Dot(across, e1));
            return radius * (1 + lobes * (float)(0.5 * Math.Sin(2 * a + LobeSeed) + 0.3 * Math.Sin(3 * a + 2.1 * LobeSeed) + 0.2 * Math.Sin(5 * a + 3.7 * LobeSeed)));
        }

        public override float NoiseMask(Vec3 p)
        {
            if (Ground <= 0)
            {
                return 1;
            }

            float below = length - Ground - 1 - Vec3.Dot(p - bottomCenter, up);
            return Math.Max(0, Math.Min(1, below / radius));
        }
    }
}
