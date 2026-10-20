using Subterra.Core.Numerics;

namespace Subterra.Core.Scatter
{
    /// <summary>One placed prop: position, orientation (up and forward, unit, perpendicular) and uniform scale.</summary>
    public readonly struct ScatterInstance
    {
        public readonly Vec3 Position;
        public readonly Vec3 Up;
        public readonly Vec3 Forward;
        public readonly float Scale;

        public ScatterInstance(Vec3 position, Vec3 up, Vec3 forward, float scale)
        {
            Position = position;
            Up = up;
            Forward = forward;
            Scale = scale;
        }

        /// <summary>The prop's X axis (Unity convention: right = up × forward).</summary>
        public Vec3 Right => Vec3.Cross(Up, Forward);

        /// <summary>Maps a point of the prop's mesh into the chunk's frame.</summary>
        public Vec3 Transform(Vec3 v) => Position + (Right * v.X + Up * v.Y + Forward * v.Z) * Scale;

        public Vec3 Rotate(Vec3 n) => Right * n.X + Up * n.Y + Forward * n.Z;
    }
}
