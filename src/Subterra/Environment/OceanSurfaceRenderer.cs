using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The ocean seen from above, down a hole into it (Lucas: from the mouth of the shaft the ocean did not show; stars did,
    /// through the bottom of the hole, as nothing was drawn under the ice). While the camera is in a cave or a shaft over a
    /// body's ocean, a disc of water lies at the ocean's ceiling under it: the rock round the hole hides it everywhere but
    /// through the hole. In the water the underwater view takes over.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class OceanSurfaceRenderer : MonoBehaviour
    {
        /// <summary>Radius (m) of the disc, and how far over the ceiling (m) the camera may be.</summary>
        private const float Radius = 150f, Reach = 6000f;

        private GameObject disc;
        private Material material;
        private CelestialBody discBody;
        private Vec3d discCentre;
        private float nextLook;

        private void LateUpdate()
        {
            if (disc != null && disc.activeSelf && discBody != null)
            {
                // Kept in the body's frame (the floating origin moves the world under it).
                disc.transform.position = Frames.ToWorld(discBody, discCentre);
                disc.transform.rotation = Quaternion.FromToRotation(Vector3.up, ((Vector3)(disc.transform.position - discBody.position)).normalized);
            }

            if (Time.time < nextLook)
            {
                return;
            }

            nextLook = Time.time + 0.25f;
            CelestialBody body = FlightGlobals.currentMainBody;
            Camera camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (camera == null || body == null || !UndergroundTracker.Camera.Inside || UnderwaterView.Under
                || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) || runtime.Ocean == null)
            {
                Hide();
                return;
            }

            Vec3d at = Frames.ToBody(body, camera.transform.position);
            BodyFrame.ToLatLon(at.Normalized, out double lat, out _);
            double ceiling = runtime.Ocean.CeilingRadius(lat);
            if (at.Length < ceiling || at.Length > ceiling + Reach)
            {
                Hide();
                return;
            }

            if (disc == null)
            {
                disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "SubterraOceanSurface";
                Destroy(disc.GetComponent<Collider>());
                float[] c = runtime.Ocean.Spec.Colour;
                var colour = c == null ? new Color(0.06f, 0.16f, 0.2f) : new Color(c[0], c[1], c[2]);
                material = MaterialFactory.Create("Ice_d", "Ice_n", colour * 0.7f);
                material.SetColor("_SpecColor", new Color(0.5f, 0.55f, 0.6f));
                material.SetFloat("_Shininess", 0.85f);
                material.name = "Subterra ocean surface";
                var renderer = disc.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                disc.transform.localScale = new Vector3(Radius * 2, 0.05f, Radius * 2);
            }

            discBody = body;
            discCentre = at.Normalized * ceiling;
            disc.layer = camera.gameObject.layer;
            disc.SetActive(true);
        }

        private void Hide()
        {
            if (disc != null && disc.activeSelf)
            {
                disc.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (disc != null)
            {
                Destroy(disc);
            }

            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
