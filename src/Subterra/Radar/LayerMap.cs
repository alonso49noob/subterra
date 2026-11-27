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
    /// planet's layers graphically, at real scale; then: more 3D). With the button on, the focused body is drawn as a cutaway:
    /// its surface with a quarter taken out towards the camera, each rock layer a band of its colour on the two faces of the
    /// cut from its top to its bottom depth, the ocean under the ice or rock in blue, the faces lit by the sun; names on the bands thick enough on screen, and a key
    /// with every layer's depths and temperature. Map view and Tracking Station. [Claude, revisar]
    /// </summary>
    public abstract class LayerMap : MonoBehaviour
    {
        private const int Segments = 96;

        private static bool userShown;
        private static Texture2D buttonIcon;
        private ApplicationLauncherButton button;
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

        // Lucas: "something more 3D". A cutaway, as the Earth is drawn in textbooks: the planet's own surface with a quarter
        // taken out towards the camera, and on the two faces of the cut each layer a band of its colour, the faces lit by the
        // sun as solid rock would be. The scaled planet draws the cut mesh while it shows.
        private GameObject root;
        private GameObject[] faces;
        private Material shellSource;
        private MeshRenderer hidden;
        private MeshFilter planetFilter;
        private Mesh planetMesh, cutMesh;
        private float localRadius = 1000;

        // The mean distance of a mesh's vertices from its centre.
        private static float MeshRadius(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            if (v.Length == 0)
            {
                return 1000;
            }

            double sum = 0;
            foreach (Vector3 p in v)
            {
                sum += p.magnitude;
            }

            return (float)(sum / v.Length);
        }
        private float wedgeStart;

        /// <summary>Degrees the camera may swing from the cut's middle before the cut turns to face it again.</summary>
        private const float Refacing = 50f;

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

            if (root == null || PlanetariumCamera.Camera == null)
            {
                return;
            }

            // The cut turns to face the camera when it has swung round the planet.
            float facing = CameraLongitude();
            if (Mathf.Abs(Mathf.DeltaAngle(facing, wedgeStart + 45f)) > Refacing)
            {
                Cut(shown, facing - 45f);
            }

            // Each face lit as rock in sunlight would be (darker turned away from the sun).
            Vector3 toSun = (ScaledSpace.LocalToScaledSpace(FlightGlobals.Bodies[0].position) - root.transform.position).normalized;
            for (int k = 0; k < faces.Length; k++)
            {
                Vector3 normal = faces[k].transform.TransformDirection(FaceNormal(k)).normalized;
                float light = 0.45f + 0.55f * Mathf.Max(0, Vector3.Dot(normal, toSun));
                faces[k].GetComponent<MeshRenderer>().sharedMaterial.color = new Color(light, light, light, 1);
            }
        }

        // The camera's longitude round the planet's axis, in the scaled body's frame (degrees).
        private float CameraLongitude()
        {
            Vector3 cam = shown.scaledBody.transform.InverseTransformPoint(PlanetariumCamera.Camera.transform.position);
            return Mathf.Atan2(cam.z, cam.x) * Mathf.Rad2Deg;
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

            hidden = body.scaledBody.GetComponent<MeshRenderer>();
            MeshFilter source = body.scaledBody.GetComponent<MeshFilter>();
            if (hidden == null || source == null || source.sharedMesh == null)
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
                    Colour = new Color(Mathf.Clamp01(t[0] * 1.5f), Mathf.Clamp01(t[1] * 1.5f), Mathf.Clamp01(t[2] * 1.5f), 1f),
                });
            }

            // The ocean, at the equator (its depths change with latitude), over whatever layer it replaces there.
            if (runtime.Ocean != null)
            {
                double top = body.Radius - runtime.Ocean.CeilingRadius(0), bottom = body.Radius - runtime.Ocean.FloorRadius(0);
                bands.Add(new Band { Name = Localizer.Format("#Subterra_Layer_Ocean"), Top = top, Bottom = bottom, Temperature = runtime.Ocean.Spec.Temperature, Colour = new Color(0.15f, 0.45f, 0.95f, 1f) });
            }

            // A child of the scaled body (1000 local units at body.Radius): drawn by the map camera, at the body's true scale.
            Transform parent = body.scaledBody.transform;
            root = new GameObject("SubterraLayerMap") { layer = parent.gameObject.layer };
            root.transform.SetParent(parent, false);
            shellSource = hidden.sharedMaterial;
            // The planet's own mesh is swapped for the cut one (and put back after). Turning its renderer off did nothing:
            // KSP turns it back on every frame for its fading (Lucas saw the planet whole, the cut hidden inside it).
            planetFilter = source;
            planetMesh = source.sharedMesh;
            // The scaled mesh's own radius (not always 1000 units: on Jool's moons the cut was ten times the planet).
            localRadius = MeshRadius(planetMesh);
            Cut(body, CameraLongitude() - 45f);
            Log.Info(Invariant($"Layer map: {bands.Count} bands of {body.bodyName}, cut open"));
        }

        // (Re)builds the cut: the planet's surface without the quarter from `start` to `start` + 90 degrees, and its two faces.
        private void Cut(CelestialBody body, float start)
        {
            wedgeStart = start;
            foreach (Transform child in root.transform)
            {
                MeshFilter f = child.GetComponent<MeshFilter>();
                if (f != null)
                {
                    Destroy(f.sharedMesh);
                }

                MeshRenderer r = child.GetComponent<MeshRenderer>();
                if (r != null && r.sharedMaterial != shellSource)
                {
                    Destroy(r.sharedMaterial);
                }

                Destroy(child.gameObject);
            }

            // The surface: the planet's own mesh, less the triangles wholly inside the quarter, drawn by its own renderer.
            if (cutMesh != null)
            {
                Destroy(cutMesh);
            }

            cutMesh = Shell(planetMesh, start);
            planetFilter.sharedMesh = cutMesh;

            // The two faces of the cut, in the meridian planes at its two sides.
            faces = new GameObject[2];
            for (int k = 0; k < 2; k++)
            {
                faces[k] = new GameObject("face" + k) { layer = root.layer };
                faces[k].transform.SetParent(root.transform, false);
                faces[k].transform.localRotation = Quaternion.Euler(0, -(start + 90f * k), 0);
                faces[k].AddComponent<MeshFilter>().sharedMesh = Face(body, k == 0);
                var fr = faces[k].AddComponent<MeshRenderer>();
                var m = new Material(Shader.Find("Hidden/Internal-Colored"));
                m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                m.SetInt("_ZWrite", 1);
                m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                m.renderQueue = 2000;
                fr.sharedMaterial = m;
                fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // Face k's normal in its own frame, pointing out of the rock into the cut.
        // (Face 0 bounds the cut at its lower longitude: the rock lies below it and the face looks up the longitudes, +Z.)
        private static Vector3 FaceNormal(int k) => k == 0 ? Vector3.forward : Vector3.back;

        // The triangles of the planet's mesh not wholly inside the quarter (start, start + 90) round its Y axis. The few that
        // straddle its sides are kept, overlapping the faces a little, so no gap shows between the surface and the cut.
        private static Mesh Shell(Mesh source, float start)
        {
            Vector3[] v = source.vertices;
            var inside = new bool[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float lon = Mathf.Atan2(v[i].z, v[i].x) * Mathf.Rad2Deg;
                float d = Mathf.DeltaAngle(start, lon);
                inside[i] = d > 0 && d < 90;
            }

            var mesh = new Mesh { name = "SubterraLayerMapSurface", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = v;
            mesh.normals = source.normals;
            mesh.uv = source.uv;
            if (source.tangents != null && source.tangents.Length == v.Length)
            {
                mesh.tangents = source.tangents;
            }

            if (source.colors != null && source.colors.Length == v.Length)
            {
                mesh.colors = source.colors;
            }

            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] tris = source.GetTriangles(s);
                var kept = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    if (!(inside[tris[t]] && inside[tris[t + 1]] && inside[tris[t + 2]]))
                    {
                        kept.Add(tris[t]);
                        kept.Add(tris[t + 1]);
                        kept.Add(tris[t + 2]);
                    }
                }

                mesh.SetTriangles(kept, s);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        // One face: a half disc in its plane (x out from the axis, y along it), a band per layer (the ocean last, over its
        // layer), a thin dark line at each contact and a white rim, each a hair nearer the viewer than the one under it.
        private Mesh Face(CelestialBody body, bool first)
        {
            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            float scale = (float)(localRadius / body.Radius);
            float toward = first ? 1f : -1f;
            void Annulus(double outer, double inner, Color colour, float lift)
            {
                int start = vertices.Count;
                for (int s = 0; s <= Segments; s++)
                {
                    float a = Mathf.PI * (-0.5f + (float)s / Segments);
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    // A little darker deep down: the bands read as rock going in, not paint.
                    float shadeOuter = 0.82f + 0.18f * (float)(outer / body.Radius), shadeInner = 0.82f + 0.18f * (float)(inner / body.Radius);
                    vertices.Add(new Vector3(c * (float)outer * scale, sn * (float)outer * scale, toward * lift));
                    vertices.Add(new Vector3(c * (float)inner * scale, sn * (float)inner * scale, toward * lift));
                    colours.Add(new Color(colour.r * shadeOuter, colour.g * shadeOuter, colour.b * shadeOuter, 1));
                    colours.Add(new Color(colour.r * shadeInner, colour.g * shadeInner, colour.b * shadeInner, 1));
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
                Annulus(r - b.Top, r - b.Bottom, b.Colour, b.Name == Localizer.Format("#Subterra_Layer_Ocean") ? 0.02f : 0f);
            }

            var line = new Color(0.06f, 0.06f, 0.06f, 1);
            float px = (float)(r * 0.0015);
            foreach (Band b in bands)
            {
                Annulus(r - b.Bottom + px, r - b.Bottom - px, line, 0.04f);
            }

            Annulus(r + px * 0.5, r - px * 1.5, new Color(0.95f, 0.95f, 0.95f, 1), 0.05f);
            var mesh = new Mesh { name = "SubterraLayerMapFace", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnGUI()
        {
            if (root == null || shown == null || PlanetariumCamera.Camera == null)
            {
                return;
            }

            // Names on the bands thick enough on screen, at the middle of the face nearer the camera.
            Camera cam = PlanetariumCamera.Camera;
            float scale = (float)(localRadius / shown.Radius);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            var shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0, 0, 0, 0.8f);
            Transform face = NearerFace(cam);
            // No two names on top of each other (Lucas: thin bands' names overlapped): a name whose box would touch one
            // already drawn is left to the key.
            var drawn = new List<Rect>();
            foreach (Band b in bands)
            {
                float mid = (float)(shown.Radius - (b.Top + b.Bottom) * 0.5) * scale;
                Vector3 at = cam.WorldToScreenPoint(face.TransformPoint(new Vector3(mid, 0, 0)));
                Vector3 top = cam.WorldToScreenPoint(face.TransformPoint(new Vector3((float)(shown.Radius - b.Top) * scale, 0, 0)));
                Vector3 bottom = cam.WorldToScreenPoint(face.TransformPoint(new Vector3((float)(shown.Radius - b.Bottom) * scale, 0, 0)));
                if (at.z < 0 || Vector2.Distance(top, bottom) < 16)
                {
                    continue;
                }

                var rect = new Rect(at.x - 80, Screen.height - at.y - 9, 160, 20);
                if (drawn.Exists(r => r.Overlaps(rect)))
                {
                    continue;
                }

                drawn.Add(rect);
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), b.Name, shadow);
                GUI.Label(rect, b.Name, style);
            }

            keyRect.width = 380;
            keyRect.height = KeyTop + bands.Count * KeyRow + 8;
            keyRect = GUI.Window(GetInstanceID(), keyRect, Key, Localizer.Format("#Subterra_LayerMap_Title", shown.displayName.LocalizeRemoveGender()));
        }

        private Transform NearerFace(Camera cam)
        {
            float best = float.MaxValue;
            Transform nearest = faces[0].transform;
            foreach (GameObject f in faces)
            {
                float d = (f.transform.TransformPoint(new Vector3(localRadius * 0.5f, 0, 0)) - cam.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = f.transform;
                }
            }

            return nearest;
        }

        // Laid out by hand, a row per band, the window as tall as its rows (Lucas: the key came out an empty bar; and before,
        // its colours as tiny pixels: a 1x1 texture drawn at its own size).
        private const float KeyRow = 21, KeyTop = 26;

        private void Key(int id)
        {
            for (int i = 0; i < bands.Count; i++)
            {
                Band b = bands[i];
                float y = KeyTop + i * KeyRow;
                GUI.DrawTexture(new Rect(10, y + 3, 18, 14), Swatch(b.Colour), ScaleMode.StretchToFill);
                GUI.Label(new Rect(34, y, keyRect.width - 42, KeyRow),
                    Invariant($"{b.Name}: {Km(b.Top)}–{Km(b.Bottom)}{(b.Temperature.HasValue ? $", {b.Temperature.Value:F0} K" : "")}"));
            }

            GUI.DragWindow();
        }

        private static string Km(double m) => m >= 10000 ? Invariant($"{m / 1000:F0} km") : m >= 1000 ? Invariant($"{m / 1000:F1} km") : Invariant($"{m:F0} m");

        private static Texture2D Swatch(Color c)
        {
            if (!Swatches.TryGetValue(c, out Texture2D t) || t == null)
            {
                t = new Texture2D(2, 2) { filterMode = FilterMode.Point };
                var px = new Color(c.r, c.g, c.b, 1);
                t.SetPixels(new[] { px, px, px, px });
                t.Apply();
                Swatches[c] = t;
            }

            return t;
        }

        private void Teardown()
        {
            if (root != null)
            {
                foreach (Transform child in root.transform)
                {
                    MeshFilter f = child.GetComponent<MeshFilter>();
                    if (f != null)
                    {
                        Destroy(f.sharedMesh);
                    }

                    MeshRenderer r = child.GetComponent<MeshRenderer>();
                    if (r != null && r.sharedMaterial != shellSource)
                    {
                        Destroy(r.sharedMaterial);
                    }
                }

                Destroy(root);
            }

            // The planet's own mesh back.
            if (planetFilter != null && planetMesh != null)
            {
                planetFilter.sharedMesh = planetMesh;
            }

            if (cutMesh != null)
            {
                Destroy(cutMesh);
            }

            cutMesh = null;
            planetFilter = null;
            planetMesh = null;
            root = null;
            faces = null;
            hidden = null;
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
