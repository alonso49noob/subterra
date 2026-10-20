using System;

namespace Subterra.Core.Numerics
{
    /// <summary>Double-precision 3D vector used for body-centred coordinates.</summary>
    public readonly struct Vec3d : IEquatable<Vec3d>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3d Zero => default;

        public double LengthSquared => X * X + Y * Y + Z * Z;

        public double Length => Math.Sqrt(LengthSquared);

        public Vec3d Normalized
        {
            get
            {
                double length = Length;
                return length > 0 ? this / length : Zero;
            }
        }

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.X, -a.Y, -a.Z);

        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.X * s, a.Y * s, a.Z * s);

        public static Vec3d operator *(double s, Vec3d a) => new Vec3d(a.X * s, a.Y * s, a.Z * s);

        public static Vec3d operator /(Vec3d a, double s) => new Vec3d(a.X / s, a.Y / s, a.Z / s);

        public static bool operator ==(Vec3d a, Vec3d b) => a.Equals(b);

        public static bool operator !=(Vec3d a, Vec3d b) => !a.Equals(b);

        public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3d Cross(Vec3d a, Vec3d b) =>
            new Vec3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => a + (b - a) * t;

        public static double Distance(Vec3d a, Vec3d b) => (a - b).Length;

        public Vec3 ToVec3() => new Vec3((float)X, (float)Y, (float)Z);

        public static Vec3d FromVec3(Vec3 v) => new Vec3d(v.X, v.Y, v.Z);

        public bool Equals(Vec3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is Vec3d other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = hash * 397 ^ Y.GetHashCode();
                return hash * 397 ^ Z.GetHashCode();
            }
        }

        public override string ToString() => FormattableString.Invariant($"({X}, {Y}, {Z})");
    }
}
