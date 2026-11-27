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

            if (sample == null || !sample.Value.Under || MapView.MapIsEnabled)
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
            // Light: the veil is drawn over everything, the craft's lit surroundings too (Lucas: the lights did not make it easier
            // to see). The dark is the haze's, with distance; the veil only tints, and nearly not at all with lamps on.
            float veilAlpha = LampsOn(camera) ? Mathf.Lerp(0.05f, 0.15f, deep) : Mathf.Lerp(0.15f, 0.4f, deep);
            material.color = new Color(tint.r, tint.g, tint.b, veilAlpha);
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
                SkyCameras.Hide(this, camera);

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
            // The haze takes the water's own colour, not the depth's near-black (Lucas: no fog in the lakes; it faded to black,
            // which looked like none), brighter where the craft's lamps light the water.
            Color haze = colour * (LampsOn(camera) ? 0.75f : 0.4f);
            Shells(camera, haze, camera.farClipPlane);
            Under = true;
        }

        private float nextLamps;
        private bool lamps;

        // Whether a loaded craft near the camera has a lamp on (looked up twice a second).
        private bool LampsOn(Camera camera)
        {
            if (Time.time < nextLamps)
            {
                return lamps;
            }

            nextLamps = Time.time + 0.5f;
            lamps = false;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || (v.CoMD - (Vector3d)camera.transform.position).sqrMagnitude > 300 * 300)
                {
                    continue;
                }

                foreach (Light light in v.GetComponentsInChildren<Light>())
                {
                    if (light.enabled && light.intensity > 0 && light.type != LightType.Directional)
                    {
                        lamps = true;
                        return true;
                    }
                }
            }

            return false;
        }

        // Fog that KSP's shaders do not draw themselves (they ignore Unity's fog: the floor of Minmus's ocean ended in a hard
        // edge at the far plane, Lucas): shells of the water's colour round the camera, each veiling a little of what lies
        // past it, so things fade into the water with distance and are gone before the far plane.
        private static readonly float[] ShellRadii = { 0.16f, 0.26f, 0.37f, 0.5f, 0.64f, 0.78f, 0.92f };
        private const float ShellAlpha = 0.24f;
        private readonly List<GameObject> shells = new List<GameObject>();
        private Material shellMaterial;

        private void Shells(Camera camera, Color tint, float far)
        {
            if (shellMaterial == null)
            {
                shellMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "Subterra underwater fog", renderQueue = 2900 };
                shellMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                shellMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                shellMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
                shellMaterial.SetInt("_ZWrite", 0);
                shellMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            }

            while (shells.Count < ShellRadii.Length)
            {
                GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shell.name = "SubterraUnderwaterFog";
                Destroy(shell.GetComponent<Collider>());
                var renderer = shell.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = shellMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                shells.Add(shell);
            }

            shellMaterial.color = new Color(tint.r, tint.g, tint.b, ShellAlpha);
            for (int i = 0; i < shells.Count; i++)
            {
                GameObject shell = shells[i];
                shell.transform.SetParent(camera.transform, false);
                shell.transform.localPosition = Vector3.zero;
                shell.transform.localScale = Vector3.one * (2f * far * ShellRadii[i]);
                shell.layer = camera.gameObject.layer;
                shell.SetActive(true);
            }
        }

        private void HideShells()
        {
            foreach (GameObject shell in shells)
            {
                if (shell != null)
                {
                    shell.SetActive(false);
                }
            }
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
            HideShells();
            if (veil != null && veil.activeSelf)
            {
                veil.SetActive(false);
            }

            if (skyHidden != null)
            {
                SkyCameras.Show(this);

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

            foreach (GameObject shell in shells)
            {
                if (shell != null)
                {
                    Destroy(shell);
                }
            }

            if (shellMaterial != null)
            {
                Destroy(shellMaterial);
            }
        }
    }
}
