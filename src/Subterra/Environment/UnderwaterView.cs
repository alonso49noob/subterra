using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using Subterra.Core.Water;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The camera under a cave's water (sub-project 4): the view takes the water's colour, thicker and darker the deeper
    /// it goes (a tinted veil in front of the camera, and Unity's fog for the shaders that use it). Off out of the water.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class UnderwaterView : MonoBehaviour
    {
        /// <summary>Depth (m) at which the view is at its darkest. [Claude, revisar]</summary>
        private const float DarkDepth = 40f;

        private GameObject veil;
        private Material material;
        private bool fogSaved, fog;
        private Color fogColour;
        private float fogDensity;
        private FogMode fogMode;

        /// <summary>True while the camera is under a cave's water (for tests).</summary>
        public static bool Under { get; private set; }

        private void LateUpdate()
        {
            Camera camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            WaterSample? sample = null;
            if (camera != null && Settings.Current.WaterPhysics && FlightGlobals.currentMainBody != null
                && Underground.Locate(FlightGlobals.currentMainBody, camera.transform.position, out TubeNetwork net, out var local) && net.Waters.Count > 0)
            {
                sample = net.WaterAt(local, margin: 1f, above: 0f);
            }
            else if (camera != null && Settings.Current.WaterPhysics && FlightGlobals.currentMainBody != null)
            {
                sample = Underground.OceanAt(FlightGlobals.currentMainBody, camera.transform.position) ?? InIce(FlightGlobals.currentMainBody, camera.transform.position);
            }

            if (sample == null || !sample.Value.Under)
            {
                Leave();
                return;
            }

            Enter(camera, sample.Value);
        }

        // The camera inside the ice over an ocean (pulled back through it from a vessel under it): nothing to see but ice,
        // so a dark, near-opaque view (deep as the darkest water) instead of the open sky through the unmeshed ice.
        private static WaterSample? InIce(CelestialBody body, Vector3d world)
        {
            if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) || runtime.Ocean == null)
            {
                return null;
            }

            Vec3d at = Frames.ToBody(body, world);
            Core.Geo.BodyFrame.ToLatLon(at, out double lat, out double lon);
            double r = at.Length;
            if (r < runtime.Ocean.CeilingRadius(lat) || r > KspSampler.SurfaceRadius(body, lat, lon) - 2)
            {
                return null;
            }

            return new WaterSample(DarkDepth * 2, runtime.Ocean.Spec.Density, at.Normalized.ToVec3(), new[] { 0.10f, 0.14f, 0.18f });
        }

        private void Enter(Camera camera, WaterSample sample)
        {
            float[] c = sample.Colour;
            var colour = c == null ? new Color(0.10f, 0.20f, 0.22f) : new Color(c[0], c[1], c[2]);
            float deep = Mathf.Clamp01((float)sample.Depth / DarkDepth);
            Color tint = Color.Lerp(colour, colour * 0.25f, deep);

            if (veil == null)
            {
                veil = GameObject.CreatePrimitive(PrimitiveType.Quad);
                veil.name = "SubterraUnderwater";
                Destroy(veil.GetComponent<Collider>());
                material = new Material(Shader.Find("Sprites/Default")) { name = "Subterra underwater" };
                var renderer = veil.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // A quad just past the near plane, filling the view.
            float distance = camera.nearClipPlane + 0.05f;
            float height = 2.2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            veil.transform.SetParent(camera.transform, false);
            veil.transform.localPosition = new Vector3(0, 0, distance);
            veil.transform.localRotation = Quaternion.identity;
            veil.transform.localScale = new Vector3(height * camera.aspect, height, 1);
            veil.layer = camera.gameObject.layer;
            material.color = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(0.35f, 0.8f, deep));
            veil.SetActive(true);

            if (!fogSaved)
            {
                fogSaved = true;
                fog = RenderSettings.fog;
                fogColour = RenderSettings.fogColor;
                fogDensity = RenderSettings.fogDensity;
                fogMode = RenderSettings.fogMode;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = tint;
            RenderSettings.fogDensity = Mathf.Lerp(0.03f, 0.08f, deep);
            Under = true;
        }

        private void Leave()
        {
            Under = false;
            if (veil != null && veil.activeSelf)
            {
                veil.SetActive(false);
            }

            if (fogSaved)
            {
                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColour;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogMode = fogMode;
                fogSaved = false;
            }
        }

        private void OnDestroy()
        {
            Leave();
            if (veil != null)
            {
                Destroy(veil);
            }
        }
    }
}
