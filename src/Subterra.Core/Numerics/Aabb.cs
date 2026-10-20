using System;

namespace Subterra.Core.Numerics
{
    /// <summary>Axis-aligned bounding box; Min and Max are inclusive.</summary>
    public readonly struct Aabb : IEquatable<Aabb>
    {
        public readonly Vec3 Min;
        public readonly Vec3 Max;

        public Aabb(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public Vec3 Center => (Min + Max) * 0.5f;

        public Vec3 Size => Max - Min;

        /// <summary>Half the length of the box's diagonal: the radius of its bounding sphere.</summary>
        public float HalfDiagonal => (Max - Min).Length * 0.5f;

        public bool Intersects(Aabb other) =>
            Min.X <= other.Max.X && Max.X >= other.Min.X
            && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y
            && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

        public bool Contains(Vec3 p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

        public Aabb Expand(float amount)
        {
            var delta = new Vec3(amount, amount, amount);
            return new Aabb(Min - delta, Max + delta);
        }

        /// <summary>Euclidean distance from <paramref name="p"/> to the box (0 inside).</summary>
        public float DistanceTo(Vec3 p)
        {
            float dx = Math.Max(Math.Max(Min.X - p.X, 0f), p.X - Max.X);
            float dy = Math.Max(Math.Max(Min.Y - p.Y, 0f), p.Y - Max.Y);
            float dz = Math.Max(Math.Max(Min.Z - p.Z, 0f), p.Z - Max.Z);
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static Aabb Union(Aabb a, Aabb b) => new Aabb(Vec3.Min(a.Min, b.Min), Vec3.Max(a.Max, b.Max));

        public bool Equals(Aabb other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        public override bool Equals(object obj) => obj is Aabb other && Equals(other);

        public override int GetHashCode() => unchecked(Min.GetHashCode() * 397 ^ Max.GetHashCode());

        public override string ToString() => $"[{Min} .. {Max}]";
    }
}
