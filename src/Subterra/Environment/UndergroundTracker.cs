using System;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>Underground state of the camera and of vessels (5 Hz), with depth, sky, zone, biome, layer and rock temperature.</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class UndergroundTracker : MonoBehaviour
    {
        private static UndergroundState camera;
        private float nextUpdate;

        public static UndergroundState Camera => camera;

        public static UndergroundState For(Vessel v)
        {
            SubterraVesselModule module = SubterraVesselModule.Of(v);
            return module != null ? module.State : Compute(v.mainBody, v.GetWorldPos3D(), true);
        }

        /// <summary>The state at a world point; <paramref name="full"/> = also sky, zone, biome, layer, temperature (main thread).</summary>
        public static UndergroundState Compute(CelestialBody body, Vector3d world, bool full = true)
        {
            if (!Underground.Locate(body, world, out TubeNetwork net, out Vec3 local))
            {
                return UndergroundState.Outside;
            }

            var state = new UndergroundState { Inside = true, Net = net, Local = local };
            Vec3d bodyPos = net.ToBody(local);
            state.Depth = Math.Max(0, net.SurfaceRadiusNear(local) - bodyPos.Length);
            if (full && BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                BodyFrame.ToLatLon(bodyPos, out double lat, out double lon);
                state.Sky = net.SkyVisibility(local);
                state.Zone = net.ZoneAt(local);
                state.Biome = KspSampler.Biome(body, lat, lon);
                state.Layer = runtime.Strata.LayerAt(state.Depth, state.Biome)?.Name;
                state.RockTemp = runtime.Geotherm.Temperature(state.Depth, state.Biome);
            }

            return state;
        }

        private void Update()
        {
            if (!FlightGlobals.ready || FlightCamera.fetch == null || Time.realtimeSinceStartup < nextUpdate)
            {
                return;
            }

            using (SubterraClock.Measure("UndergroundTracker"))
            {
                nextUpdate = Time.realtimeSinceStartup + 0.2f;
                camera = Compute(FlightGlobals.currentMainBody, FlightCamera.fetch.mainCamera.transform.position, true);
            }
        }

        private void OnDestroy()
        {
            camera = UndergroundState.Outside;
        }
    }
}
