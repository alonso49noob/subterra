using System.Collections.Generic;
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
        /// <summary>Shaders tried for the veil, in order.</summary>
        private static readonly string[] VeilShaders =
        {
            "KSP/Alpha/Unlit Transparent", "Unlit/Transparent", "Legacy Shaders/Transparent/Diffuse", "Sprites/Default",
        };

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
                && Underground.Locate(FlightGlobals.currentMainBody, camera.transform.position, out TubeNetwork net, out var local))
            {
                // In a cave's or a dug hole's void: only its own water darkens the view. (A shaft dug into Vall's ice counted
                // as "inside the ice over the ocean" and turned the whole view dark blue: Lucas.)
                // A hole down to the ocean: below the ice, its water is the ocean's.
                sample = net.Waters.Count > 0 ? net.WaterAt(local, margin: 1f, above: 0f) : Underground.OceanAt(FlightGlobals.currentMainBody, camera.transform.position);
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
            // Under the ocean's floor, in the rock (Lucas: the camera pulled back from a craft in Vall's ocean showed only the
            // sky, through a planet that cannot be drawn from inside): dark rock.
            bool underFloor = r < runtime.Ocean.FloorRadius(lat) - 2;
            if ((!underFloor && (r < runtime.Ocean.CeilingRadius(lat) || r > KspSampler.SurfaceRadius(body, lat, lon) - 2))
                // By a dug shaft or a cave (the camera pushed into its wall): the hole is what is seen, not solid ice.
                || runtime.Catalog.FindNear(at, 15f) != null)
            {
                return null;
            }

            return new WaterSample(DarkDepth * 2, runtime.Ocean.Spec.Density, at.Normalized.ToVec3(),
                underFloor ? new[] { 0.09f, 0.08f, 0.07f } : new[] { 0.10f, 0.14f, 0.18f });
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
                // The first see-through unlit shader there is (KSP 1.12 has Sprites/Default; Retooled's Unity 6 drew
                // nothing with it), drawn over everything.
                Shader shader = null;
                foreach (string name in VeilShaders)
                {
                    Shader found = Shader.Find(name);
                    if (found != null && found.isSupported)
                    {
                        shader = found;
                        break;
                    }
                }

                material = new Material(shader ?? Shader.Find("Sprites/Default")) { name = "Subterra underwater", renderQueue = 4000 };
                Log.Info($"Underwater view: veil shader {material.shader?.name ?? "none"}");
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

            // Beyond the water there is only more water: no stars or planets behind it. The sky cameras are switched off
            // and the main camera clears to the water's colour (an ocean has no walls to hide the sky; Lucas saw stars
            // under Tylo's rock).
            if (skyHidden == null)
            {
                skyHidden = new List<Camera>();
                foreach (Camera sky in Camera.allCameras)
                {
                    if (sky != camera && sky.enabled && (sky.name == "GalaxyCamera" || sky.name == "Camera ScaledSpace"))
                    {
                        sky.enabled = false;
                        skyHidden.Add(sky);
                    }
                }

                mainCamera = camera;
                clearFlags = camera.clearFlags;
                background = camera.backgroundColor;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = tint * 0.5f;

            // Water hides what is far: the camera sees no farther than a few hundred metres (less the deeper and darker), so
            // the edges of the ceiling and floor patches do not show as islands floating in the dark (Lucas).
            if (!farSaved)
            {
                farSaved = true;
                farClip = camera.farClipPlane;
            }

            camera.farClipPlane = Mathf.Lerp(MaxSight, MinSight, deep);
            Under = true;
        }

        /// <summary>Farthest the camera sees under water (m), in clear shallow water and at its darkest.</summary>
        private const float MaxSight = 250f, MinSight = 120f;

        private bool farSaved;
        private float farClip;

        private List<Camera> skyHidden;
        private Camera mainCamera;
        private CameraClearFlags clearFlags;
        private Color background;

        private void Leave()
        {
            Under = false;
            if (veil != null && veil.activeSelf)
            {
                veil.SetActive(false);
            }

            if (skyHidden != null)
            {
                foreach (Camera sky in skyHidden)
                {
                    if (sky != null)
                    {
                        sky.enabled = true;
                    }
                }

                if (mainCamera != null)
                {
                    mainCamera.clearFlags = clearFlags;
                    mainCamera.backgroundColor = background;
                    if (farSaved)
                    {
                        mainCamera.farClipPlane = farClip;
                    }
                }

                skyHidden = null;
                mainCamera = null;
                farSaved = false;
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
