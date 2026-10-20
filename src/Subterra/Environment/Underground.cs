using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra
{
    /// <summary>Point queries against the caves of a body (any thread that may read the catalog).</summary>
    public static class Underground
    {
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
