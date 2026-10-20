using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// World space ↔ body coordinates (the PQS frame: origin at the body centre, turning with the body),
    /// the frame of Subterra.Core. In double precision.
    /// </summary>
    public static class Frames
    {
        public static Vec3d ToBody(CelestialBody body, Vector3d world)
        {
            Transform t = body.pqsController.transform;
            QuaternionD inverse = QuaternionD.Inverse(t.rotation);
            return (inverse * (world - t.position)).ToCore();
        }

        public static Vector3d ToWorld(CelestialBody body, Vec3d bodyPos)
        {
            Transform t = body.pqsController.transform;
            return t.position + (QuaternionD)t.rotation * bodyPos.ToKsp();
        }

        public static Vector3 DirectionToWorld(CelestialBody body, Vec3 dir) => body.pqsController.transform.rotation * dir.ToUnity();
    }
}
