using System.Collections.Generic;
using KSP.Localization;
using KSP.UI.Screens;
using Subterra.Core.Model;
using UnityEngine;
using static System.FormattableString;

namespace Subterra
{
    /// <summary>
    /// The layers under the ground on the map, at their true scale (Lucas: a Subterra toolbar button in the map that shows the
    /// planet's layers graphically, at real scale). With the button on, the focused body is drawn cut in half: a half disc
    /// through its centre, turned to face the camera, each rock layer a band of its colour from its top to its bottom depth,
    /// the ocean under the ice or rock in blue, drawn over the planet; names on the bands thick enough on screen, and a key
    /// with every layer's depths and temperature. Map view and Tracking Station. [Claude, revisar]
    /// </summary>
    public abstract class LayerMap : MonoBehaviour
    {
        private const int Segments = 96;

        private static bool userShown;
        private static Texture2D buttonIcon;
        private ApplicationLauncherButton button;
        private GameObject slice;
        private CelestialBody shown;
        private Material material;
        private readonly List<Band> bands = new List<Band>();
        private Rect keyRect = new Rect(Screen.width - 380, 120, 340, 10);
        private static readonly Dictionary<Color, Texture2D> Swatches = new Dictionary<Color, Texture2D>();

        private sealed class Band
        {
            public string Name;
            public double Top, Bottom;
            public double? Temperature;
            public Color Colour;
        }

        private void Start()
        {
            if (ApplicationLauncher.Ready)
            {
                AddButton();
            }

            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            if (button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(button);
            }

            Teardown();
            if (material != null)
            {
                Destroy(material);
            }
        }

        private void OnLauncherDestroyed() => button = null;

        private void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null)
            {
                return;
            }

            buttonIcon = buttonIcon ?? MakeIcon();
            button = ApplicationLauncher.Instance.AddModApplication(() => userShown = true, () => userShown = false, null, null, null, null,
                ApplicationLauncher.AppScenes.MAPVIEW | ApplicationLauncher.AppScenes.TRACKSTATION, buttonIcon);
            if (userShown)
            {
                button.SetTrue(false);
            }
        }

        private void LateUpdate()
        {
            CelestialBody body = userShown ? FocusedBody() : null;
            if (body != shown)
            {
                Teardown();
                if (body != null)
                {
                    Build(body);
                }
            }

            if (slice == null || PlanetariumCamera.Camera == null)
            {
                return;
            }

            // Faces the camera, its cut edge upright on screen: the right half of the body shows its insides.
            Transform cam = PlanetariumCamera.Camera.transform;
            slice.transform.rotation = Quaternion.LookRotation(cam.forward, cam.up);
        }

        private static CelestialBody FocusedBody()
        {
            bool inMap = HighLogic.LoadedScene == GameScenes.TRACKSTATION || (HighLogic.LoadedScene == GameScenes.FLIGHT && MapView.MapIsEnabled);
            if (!inMap || PlanetariumCamera.fetch == null || PlanetariumCamera.fetch.target == null)
            {
                return null;
            }

            MapObject target = PlanetariumCamera.fetch.target;
            CelestialBody body = target.celestialBody ?? target.vessel?.mainBody;
            return body != null && body.scaledBody != null && BodyRegistry.Instance.TryGet(body, out _) ? body : null;
        }

        private void Build(CelestialBody body)
        {
            if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                return;
            }

            shown = body;
            bands.Clear();
            foreach (LayerSpec layer in runtime.Strata.Column(null))
            {
                float[] t = layer.Tint ?? new[] { 0.5f, 0.5f, 0.5f };
                double bottom = System.Math.Min(layer.Bottom, body.Radius);
                bands.Add(new Band
                {
                    Name = string.IsNullOrEmpty(layer.DisplayName) ? layer.Name : Localizer.Format(layer.DisplayName),
                    Top = layer.Top,
                    Bottom = bottom,
                    Temperature = runtime.Geotherm.Temperature((layer.Top + bottom) * 0.5, null),
                    Colour = new Color(Mathf.Clamp01(t[0] * 1.5f), Mathf.Clamp01(t[1] * 1.5f), Mathf.Clamp01(t[2] * 1.5f), 0.97f),
                });
            }

            // The ocean, at the equator (its depths change with latitude), over whatever layer it replaces there.
            if (runtime.Ocean != null)
            {
                double top = body.Radius - runtime.Ocean.CeilingRadius(0), bottom = body.Radius - runtime.Ocean.FloorRadius(0);
                bands.Add(new Band { Name = Localizer.Format("#Subterra_Layer_Ocean"), Top = top, Bottom = bottom, Temperature = runtime.Ocean.Spec.Temperature, Colour = new Color(0.15f, 0.45f, 0.95f, 0.97f) });
            }

            if (material == null)
            {
                material = new Material(Shader.Find("Hidden/Internal-Colored"));
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                material.renderQueue = 3200;
            }

            // A child of the scaled body (1000 local units at body.Radius): drawn by the map camera, at the body's true scale.
            Transform parent = body.scaledBody.transform;
            slice = new GameObject("SubterraLayerMap") { layer = parent.gameObject.layer };
            slice.transform.SetParent(parent, false);
            slice.transform.localPosition = Vector3.zero;
            slice.AddComponent<MeshFilter>().sharedMesh = Mesh(body);
            var renderer = slice.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Log.Info(Invariant($"Layer map: {bands.Count} bands of {body.bodyName}"));
        }

        // The half disc (x >= 0 in its plane), a band per layer (then the ocean, drawn last, over its layer), a thin dark line at
        // each boundary, the ground's edge white.
        private Mesh Mesh(CelestialBody body)
        {
            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            float scale = (float)(1000.0 / body.Radius);
            void Annulus(double outer, double inner, Color colour, float z)
            {
                int start = vertices.Count;
                for (int s = 0; s <= Segments; s++)
                {
                    float a = Mathf.PI * (-0.5f + (float)s / Segments);
                    var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), z);
                    vertices.Add(new Vector3(dir.x * (float)outer * scale, dir.y * (float)outer * scale, z));
                    vertices.Add(new Vector3(dir.x * (float)inner * scale, dir.y * (float)inner * scale, z));
                    colours.Add(colour);
                    colours.Add(colour);
                }

                for (int s = 0; s < Segments; s++)
                {
                    int i = start + s * 2;
                    triangles.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                }
            }

            double r = body.Radius;
            foreach (Band b in bands)
            {
                Annulus(r - b.Top, r - b.Bottom, b.Colour, 0);
            }

            var line = new Color(0.05f, 0.05f, 0.05f, 0.9f);
            float px = (float)(r * 0.0015);
            foreach (Band b in bands)
            {
                Annulus(r - b.Bottom + px, r - b.Bottom - px, line, -0.01f);
            }

            Annulus(r + px * 1.5, r - px * 0.5, new Color(1, 1, 1, 0.9f), -0.02f);
            var mesh = new Mesh { name = "SubterraLayerMap", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnGUI()
        {
            if (slice == null || shown == null || PlanetariumCamera.Camera == null)
            {
                return;
            }

            // Names on the bands thick enough on screen, along the cut edge.
            Camera cam = PlanetariumCamera.Camera;
            float scale = (float)(1000.0 / shown.Radius);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            foreach (Band b in bands)
            {
                Vector3 top = cam.WorldToScreenPoint(slice.transform.TransformPoint(new Vector3(0, (float)(shown.Radius - b.Top) * scale, 0)));
                Vector3 bottom = cam.WorldToScreenPoint(slice.transform.TransformPoint(new Vector3(0, (float)(shown.Radius - b.Bottom) * scale, 0)));
                if (top.z < 0 || Mathf.Abs(top.y - bottom.y) < 14)
                {
                    continue;
                }

                float y = Screen.height - (top.y + bottom.y) * 0.5f;
                GUI.Label(new Rect(top.x + 8, y - 9, 320, 20), b.Name, style);
            }

            keyRect = GUILayout.Window(GetInstanceID(), keyRect, Key, Localizer.Format("#Subterra_LayerMap_Title", shown.displayName.LocalizeRemoveGender()));
        }

        private void Key(int id)
        {
            foreach (Band b in bands)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Box(Swatch(b.Colour), GUILayout.Width(18), GUILayout.Height(16));
                GUILayout.Label(Invariant($"{b.Name}: {Km(b.Top)}–{Km(b.Bottom)}{(b.Temperature.HasValue ? $", {b.Temperature.Value:F0} K" : "")}"));
                GUILayout.EndHorizontal();
            }

            GUI.DragWindow();
        }

        private static string Km(double m) => m >= 10000 ? Invariant($"{m / 1000:F0} km") : m >= 1000 ? Invariant($"{m / 1000:F1} km") : Invariant($"{m:F0} m");

        private static Texture2D Swatch(Color c)
        {
            if (!Swatches.TryGetValue(c, out Texture2D t) || t == null)
            {
                t = new Texture2D(1, 1);
                t.SetPixel(0, 0, new Color(c.r, c.g, c.b, 1));
                t.Apply();
                Swatches[c] = t;
            }

            return t;
        }

        private void Teardown()
        {
            if (slice != null)
            {
                Destroy(slice.GetComponent<MeshFilter>().sharedMesh);
                Destroy(slice);
            }

            slice = null;
            shown = null;
        }

        // Concentric half rings: a planet cut open.
        private static Texture2D MakeIcon()
        {
            const int size = 38;
            var pixels = new Color32[size * size];
            float centre = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre, dy = y - centre, r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool outline = r >= 15f && r <= 17f;
                    bool cut = dx >= 0 && r < 15f && ((r > 11f && r < 13f) || (r > 6.5f && r < 8.5f) || r < 3f);
                    bool edge = Mathf.Abs(dx) < 1f && r < 17f;
                    pixels[y * size + x] = outline || cut || edge ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }

            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SubterraLayerMapIcon" };
            icon.SetPixels32(pixels);
            icon.Apply(false, false);
            return icon;
        }
    }

    /// <summary>The layer map in the Flight scene's map view.</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class LayerMapFlight : LayerMap
    {
    }

    /// <summary>The layer map in the Tracking Station.</summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public sealed class LayerMapTrackingStation : LayerMap
    {
    }
}
