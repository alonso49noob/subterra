using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// World space ↔ body coordinates (the PQS frame: origin at the body centre, turning with the body),
    /// the frame of Subterra.Core. Positions use the body's double-precision position.
    /// </summary>
    public static class Frames
    {
        public static Vec3d ToBody(CelestialBody body, Vector3d world)
        {
            QuaternionD inverse = QuaternionD.Inverse(body.pqsController.transform.rotation);
            return (inverse * (world - body.position)).ToCore();
        }

        public static Vector3d ToWorld(CelestialBody body, Vec3d bodyPos) =>
            body.position + (QuaternionD)body.pqsController.transform.rotation * bodyPos.ToKsp();

        public static Vector3 DirectionToWorld(CelestialBody body, Vec3 dir) => body.pqsController.transform.rotation * dir.ToUnity();
    }
}
