using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra
{
    /// <summary>Point queries against the caves of a body (any thread that may read the catalog).</summary>
    public static class Underground
    {
        private static readonly System.Collections.Generic.Dictionary<System.Guid, (float Time, double Depth)> Buried =
            new System.Collections.Generic.Dictionary<System.Guid, (float, double)>();

        /// <summary>
        /// How deep (m) under the terrain the vessel's centre of mass is (negative: above it), looked up once a second per
        /// vessel (main thread). A vessel buried in the rock outside any void (its Set Position pocket smaller than it) is
        /// neither in KSP's sea nor under the sun.
        /// </summary>
        public static double BuriedDepth(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null || vessel.mainBody.pqsController == null)
            {
                return double.NegativeInfinity;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (Buried.TryGetValue(vessel.id, out var cached) && now - cached.Time < 1f)
            {
                return cached.Depth;
            }

            if (Buried.Count > 256)
            {
                Buried.Clear();
            }

            CelestialBody body = vessel.mainBody;
            Vector3d world = vessel.CoMD;
            double depth = KspSampler.SurfaceRadius(body, body.GetLatitude(world), body.GetLongitude(world)) - (world - body.position).magnitude;
            Buried[vessel.id] = (now, depth);
            return depth;
        }

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
