using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>Core ↔ Unity/KSP vector conversions (same axes: the PQS frame).</summary>
    public static class Convert
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static Vector3d ToKsp(this Vec3d v) => new Vector3d(v.X, v.Y, v.Z);

        public static Vec3 ToCore(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static Vec3d ToCore(this Vector3d v) => new Vec3d(v.x, v.y, v.z);
    }
}
