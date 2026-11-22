using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The body's sea is not drawn while the camera is under the ground below sea level (Lucas: holes dug on land below
    /// sea level showed sea water in them). KSP's ocean is a sphere at sea level drawn from both sides: from a shaft or
    /// a cave under the land it showed as a blue surface across the shaft, or as a blue sky around a cave. Its quads'
    /// renderers are switched off while the camera is there and on again when it leaves. Scatterer's ocean mesh is
    /// handled the same way by <see cref="ScattererPatch"/>.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class OceanHider : MonoBehaviour
    {
        private PQS ocean;
        private CelestialBody oceanBody;
        private bool hidden;
        private readonly List<PQ> stack = new List<PQ>();

        /// <summary>True while the flight camera is under the ground below the sea level of a body with a sea.</summary>
        public static bool CameraUnderSeaLevelInRock { get; private set; }

        private void LateUpdate()
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            bool hide = body != null && body.ocean && camera != null && body.pqsController != null && Under(body, camera.transform.position);
            CameraUnderSeaLevelInRock = hide;
            if (oceanBody != body)
            {
                Show();
                ocean = body != null && body.ocean ? FindOcean(body) : null;
                oceanBody = body;
            }

            if (hide && ocean != null)
            {
                // Every frame: quads the ocean builds or shows meanwhile come up with their renderer on.
                SetRenderers(false);
                hidden = true;
            }
            else
            {
                Show();
            }
        }

        // Under the terrain (or in a cave's void) and below sea level.
        private static bool Under(CelestialBody body, Vector3d world)
        {
            Core.Numerics.Vec3d at = Frames.ToBody(body, world);
            if (at.Length >= body.Radius)
            {
                return false;
            }

            Core.Geo.BodyFrame.ToLatLon(at, out double lat, out double lon);
            return at.Length < KspSampler.SurfaceRadius(body, lat, lon) - 1 || Underground.Locate(body, world, out _, out _);
        }

        private static PQS FindOcean(CelestialBody body)
        {
            foreach (PQS child in body.pqsController.ChildSpheres)
            {
                if (child != null && (child.GetComponentInChildren<PQSMod_OceanFX>(true) != null || child.name.IndexOf("Ocean", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return child;
                }
            }

            return null;
        }

        private void Show()
        {
            if (!hidden)
            {
                return;
            }

            hidden = false;
            if (ocean != null)
            {
                SetRenderers(true);
            }
        }

        // Shown: only the quads KSP itself shows (visible, not forced invisible).
        private void SetRenderers(bool on)
        {
            if (ocean.quads == null)
            {
                return;
            }

            stack.Clear();
            stack.AddRange(ocean.quads);
            while (stack.Count > 0)
            {
                PQ quad = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (quad == null)
                {
                    continue;
                }

                if (quad.meshRenderer != null)
                {
                    quad.meshRenderer.enabled = on && quad.isVisible && !quad.isForcedInvisible;
                }

                if (quad.subNodes != null)
                {
                    foreach (PQ child in quad.subNodes)
                    {
                        stack.Add(child);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            Show();
            CameraUnderSeaLevelInRock = false;
        }
    }
}
