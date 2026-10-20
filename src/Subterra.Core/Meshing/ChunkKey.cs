using System;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>A mesh chunk of a network: 32³ cells of 2^Lod metres at integer chunk coordinates.</summary>
    public readonly struct ChunkKey : IEquatable<ChunkKey>
    {
        public readonly string NetworkId;
        public readonly int Lod;
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public ChunkKey(string networkId, int lod, int x, int y, int z)
        {
            NetworkId = networkId;
            Lod = lod;
            X = x;
            Y = y;
            Z = z;
        }

        public float CellSize => 1 << Lod;

        public float Size => SurfaceNets.N * CellSize;

        public Vec3 Origin => new Vec3(X * Size, Y * Size, Z * Size);

        public Aabb Bounds => new Aabb(Origin, Origin + new Vec3(Size, Size, Size));

        public bool Equals(ChunkKey other) =>
            Lod == other.Lod && X == other.X && Y == other.Y && Z == other.Z && string.Equals(NetworkId, other.NetworkId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ChunkKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = NetworkId == null ? 0 : NetworkId.GetHashCode();
                hash = hash * 397 ^ Lod;
                hash = hash * 397 ^ X;
                hash = hash * 397 ^ Y;
                return hash * 397 ^ Z;
            }
        }

        public static bool operator ==(ChunkKey a, ChunkKey b) => a.Equals(b);

        public static bool operator !=(ChunkKey a, ChunkKey b) => !a.Equals(b);

        public override string ToString() => $"{NetworkId} L{Lod} ({X}, {Y}, {Z})";
    }
}
