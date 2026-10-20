using System;

namespace Subterra.Core.Procedural
{
    /// <summary>A cell of the equiangular cube-sphere: face 0–5 and position (I, J) on the face.</summary>
    public readonly struct CellId : IEquatable<CellId>
    {
        public readonly int Face;
        public readonly int I;
        public readonly int J;

        public CellId(int face, int i, int j)
        {
            Face = face;
            I = i;
            J = j;
        }

        public int Index(int n) => (Face * n + I) * n + J;

        public static CellId FromIndex(int index, int n) => new CellId(index / (n * n), index / n % n, index % n);

        public bool Equals(CellId other) => Face == other.Face && I == other.I && J == other.J;

        public override bool Equals(object obj) => obj is CellId other && Equals(other);

        public override int GetHashCode() => (Face * 397 + I) * 397 + J;

        public static bool operator ==(CellId a, CellId b) => a.Equals(b);

        public static bool operator !=(CellId a, CellId b) => !a.Equals(b);

        public override string ToString() => $"cell {Face}/{I}/{J}";
    }
}
