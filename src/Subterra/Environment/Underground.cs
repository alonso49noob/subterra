using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra
{
    /// <summary>Point queries against the caves of a body (any thread that may read the catalog).</summary>
    public static class Underground
    {
        /// <summary>The body's ocean water at the world point (under its ice), or null.</summary>
        public static Subterra.Core.Water.WaterSample? OceanAt(CelestialBody body, Vector3d world)
        {
            if (body == null || body.pqsController == null || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) || runtime.Ocean == null)
            {
                return null;
            }

            return runtime.Ocean.At(Frames.ToBody(body, world));
        }

        /// <summary>The network whose void contains the world point, and the point in its frame.</summary>
        public static bool Locate(CelestialBody body, Vector3d world, out TubeNetwork net, out Vec3 local)
        {
            net = null;
            local = default;
            if (body == null || body.pqsController == null || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                return false;
            }

            Vec3d bodyPos = Frames.ToBody(body, world);
            net = runtime.Catalog.FindContaining(bodyPos);
            if (net == null)
            {
                return false;
            }

            local = net.ToLocal(bodyPos);
            return true;
        }
    }
}
