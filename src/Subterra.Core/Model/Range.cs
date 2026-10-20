using System;

namespace Subterra.Core.Model
{
    /// <summary>A closed numeric interval read from <c>min, max</c>.</summary>
    public readonly struct Range : IEquatable<Range>
    {
        public readonly double Min;
        public readonly double Max;

        public Range(double min, double max)
        {
            Min = min;
            Max = max;
        }

        public bool Equals(Range other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        public override bool Equals(object obj) => obj is Range other && Equals(other);

        public override int GetHashCode() => unchecked(Min.GetHashCode() * 397 ^ Max.GetHashCode());

        public override string ToString() => FormattableString.Invariant($"{Min}, {Max}");
    }
}
