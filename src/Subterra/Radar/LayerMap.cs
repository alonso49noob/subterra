using System;
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

            if (ReferenceEquals(lastButton, button))
            {
                lastButton = null;
            }

            Teardown();
            if (material != null)
            {
                Destroy(material);
            }
        }

        // The launcher outlives the scenes: a button only forgotten here stayed in it, and the next scene added another (Lucas:
        // two layer map buttons). Taken out of the launcher, not only forgotten.
        private void OnLauncherDestroyed()
        {
            if (button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(button);
            }

            if (ReferenceEquals(lastButton, button))
            {
                lastButton = null;
            }

            button = null;
        }

        // The one button of the layer map, whichever scene's addon made it.
        private static ApplicationLauncherButton lastButton;

        private void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null)
            {
                return;
            }

            if (lastButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(lastButton);
                lastButton = null;
            }

            buttonIcon = buttonIcon ?? MakeIcon();
            button = lastButton = ApplicationLauncher.Instance.AddModApplication(() => userShown = true, () => userShown = false, null, null, null, null,
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
        private double heightScale = 1000.0 / 600000;

        // Scaled units per metre: the planet's mesh against the terrain's real radius under its vertices (a median of a few
        // hundred), so the faces' rims meet the scaled surface; the mean radius over the body's if the terrain cannot be read.
        private static double HeightScale(CelestialBody body, Mesh mesh, float meshRadius)
        {
            double fallback = meshRadius / body.Radius;
            if (body.pqsController == null)
            {
                return fallback;
            }

            Vector3[] v = mesh.vertices;
            int step = Math.Max(1, v.Length / 300);
            var ratios = new List<double>();
            for (int i = 0; i < v.Length; i += step)
            {
                Vector3 d = v[i].normalized;
                double lat = Math.Asin(Mathf.Clamp(d.y, -1f, 1f)) * 180 / Math.PI, lon = Math.Atan2(d.z, d.x) * 180 / Math.PI;
                double ground = KspSampler.SurfaceRadius(body, lat, lon);
                if (ground > 0)
                {
                    ratios.Add(v[i].magnitude / ground);
                }
            }

            if (ratios.Count == 0)
            {
                return fallback;
            }

            ratios.Sort();
            return ratios[ratios.Count / 2];
        }

        /// <summary>Whether a cutaway is shown (the radar layer steps aside: its sphere showed through the cut as striped rings).</summary>
        public static bool Active { get; private set; }

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

            HideOthers();

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

        // Whatever else is drawn round the planet (KSP's resource overlay, the atmosphere's halo, other mods' spheres) is hidden
        // while the cutaway shows (Lucas: striped heat-map bands showed through the cut: the far inside of the resource
        // overlay's sphere). Every frame, as KSP may turn them back on; put back after.
        private readonly List<Renderer> othersHidden = new List<Renderer>();

        private void HideOthers()
        {
            if (shown == null || shown.scaledBody == null)
            {
                return;
            }

            foreach (Renderer r in shown.scaledBody.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || r.gameObject == shown.scaledBody || (root != null && r.transform.IsChildOf(root.transform)))
                {
                    continue;
                }

                r.enabled = false;
                if (!othersHidden.Contains(r))
                {
                    othersHidden.Add(r);
                }
            }
        }

        private void ShowOthers()
        {
            foreach (Renderer r in othersHidden)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }

            othersHidden.Clear();
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

            // Blocks of other rock off the centre (Gilly's iron and olivine): drawn where the cut goes through them, and in the key.
            blocks.Clear();
            foreach (Core.Model.InclusionSpec inclusion in runtime.Strata.Inclusions)
            {
                float[] t = inclusion.Layer.Tint ?? new[] { 0.5f, 0.5f, 0.5f };
                blocks.Add(new Block
                {
                    Spec = inclusion,
                    Name = string.IsNullOrEmpty(inclusion.Layer.DisplayName) ? inclusion.Name : Localizer.Format(inclusion.Layer.DisplayName),
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
            heightScale = HeightScale(body, planetMesh, localRadius);
            Cut(body, CameraLongitude() - 45f);
            Active = true;
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
                faces[k].AddComponent<MeshFilter>().sharedMesh = Face(body, k == 0, start + 90f * k);
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

        // The planet's mesh without the quarter (start, start + 90) round its Y axis. Triangles wholly outside are kept as they
        // are; those across one side of the cut are clipped exactly at its plane (Lucas: teeth of the surface stuck out over
        // the faces' edges), with every vertex attribute interpolated; the rare ones across both (at the poles) are kept.
        private static Mesh Shell(Mesh source, float start)
        {
            Vector3[] v = source.vertices;
            Vector3[] n = source.normals;
            Vector2[] uv = source.uv;
            Vector2[] uv2 = source.uv2;
            Vector4[] tg = source.tangents;
            Color[] col = source.colors;
            bool hasN = n != null && n.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
            bool hasUv2 = uv2 != null && uv2.Length == v.Length, hasTg = tg != null && tg.Length == v.Length;
            bool hasCol = col != null && col.Length == v.Length;

            // The cut's planes through the axis: a1 at `start`, a2 at `start` + 90; the inside of the cut is d1 > 0 and d2 < 0.
            float s0 = start * Mathf.Deg2Rad, s1 = (start + 90f) * Mathf.Deg2Rad;
            var n1 = new Vector3(-Mathf.Sin(s0), 0, Mathf.Cos(s0));
            var n2 = new Vector3(-Mathf.Sin(s1), 0, Mathf.Cos(s1));

            var verts = new List<Vector3>(v);
            var norms = hasN ? new List<Vector3>(n) : null;
            var uvs = hasUv ? new List<Vector2>(uv) : null;
            var uvs2 = hasUv2 ? new List<Vector2>(uv2) : null;
            var tans = hasTg ? new List<Vector4>(tg) : null;
            var cols = hasCol ? new List<Color>(col) : null;

            int Lerp(int a, int b, float t)
            {
                verts.Add(Vector3.Lerp(verts[a], verts[b], t));
                norms?.Add(Vector3.Lerp(norms[a], norms[b], t).normalized);
                uvs?.Add(Vector2.Lerp(uvs[a], uvs[b], t));
                uvs2?.Add(Vector2.Lerp(uvs2[a], uvs2[b], t));
                tans?.Add(Vector4.Lerp(tans[a], tans[b], t));
                cols?.Add(Color.Lerp(cols[a], cols[b], t));
                return verts.Count - 1;
            }

            bool Inside(int i)
            {
                Vector3 p = verts[i];
                return Vector3.Dot(p, n1) > 0 && Vector3.Dot(p, n2) < 0;
            }

            var mesh = new Mesh { name = "SubterraLayerMapSurface", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var subs = new List<List<int>>();
            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] tris = source.GetTriangles(s);
                var kept = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    bool ia = Inside(a), ib = Inside(b), ic = Inside(c);
                    if (!ia && !ib && !ic)
                    {
                        kept.Add(a);
                        kept.Add(b);
                        kept.Add(c);
                        continue;
                    }

                    if (ia && ib && ic)
                    {
                        continue;
                    }

                    // Which side it crosses: the plane the inside vertices are nearer to.
                    Vector3 mid = (verts[a] + verts[b] + verts[c]) / 3f;
                    float m1 = Vector3.Dot(mid, n1), m2 = Vector3.Dot(mid, n2);
                    Vector3 plane = Mathf.Abs(m1) < Mathf.Abs(m2) ? n1 : -n2;
                    // Keep the part with Dot(p, plane) <= 0 (outside the cut on that side).
                    var poly = new List<int> { a, b, c };
                    var clipped = new List<int>(4);
                    for (int k = 0; k < 3; k++)
                    {
                        int p = poly[k], q = poly[(k + 1) % 3];
                        float dp = Vector3.Dot(verts[p], plane), dq = Vector3.Dot(verts[q], plane);
                        if (dp <= 0)
                        {
                            clipped.Add(p);
                        }

                        if ((dp <= 0) != (dq <= 0))
                        {
                            clipped.Add(Lerp(p, q, dp / (dp - dq)));
                        }
                    }

                    for (int k = 1; k + 1 < clipped.Count; k++)
                    {
                        kept.Add(clipped[0]);
                        kept.Add(clipped[k]);
                        kept.Add(clipped[k + 1]);
                    }
                }

                subs.Add(kept);
            }

            mesh.SetVertices(verts);
            if (norms != null)
            {
                mesh.SetNormals(norms);
            }

            if (uvs != null)
            {
                mesh.SetUVs(0, uvs);
            }

            if (uvs2 != null)
            {
                mesh.SetUVs(1, uvs2);
            }

            if (tans != null)
            {
                mesh.SetTangents(tans);
            }

            if (cols != null)
            {
                mesh.SetColors(cols);
            }

            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++)
            {
                mesh.SetTriangles(subs[s], s);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        // One face: a half disc in its plane (x out from the axis, y along it), a band per layer (the ocean last, over its
        // layer), a thin dark line at each contact and a white rim, each a hair nearer the viewer than the one under it.
        private Mesh Face(CelestialBody body, bool first, float longitude)
        {
            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            float scale = (float)heightScale;
            float toward = first ? 1f : -1f;
            double r = body.Radius;

            // The ground along the cut (Lucas: the outer layers should meet the terrain; on Minmus its relief is large beside
            // its radius): the terrain's radius at each step of the face, from pole to pole at the face's longitude (the
            // scaled body's frame is the geographic one: x longitude 0, y north, z longitude 90).
            var ground = new double[Segments + 1];
            for (int s = 0; s <= Segments; s++)
            {
                double lat = -90 + 180.0 * s / Segments;
                ground[s] = body.pqsController != null ? KspSampler.SurfaceRadius(body, lat, longitude) : r;
            }

            // A depth's radius at step s: the relief carried down, fading out by a quarter of the radius (the core stays round).
            double At(double depth, int s)
            {
                if (depth >= r - 1)
                {
                    return 0;
                }

                double fade = Math.Max(0, 1 - depth / (0.25 * r));
                return Math.Max(0, r - depth + (ground[s] - r) * fade);
            }

            void Annulus(double outerDepth, double innerDepth, Color colour, float lift)
            {
                int start = vertices.Count;
                for (int s = 0; s <= Segments; s++)
                {
                    float a = Mathf.PI * (-0.5f + (float)s / Segments);
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    double outer = At(outerDepth, s), inner = At(innerDepth, s);
                    // A little darker deep down: the bands read as rock going in, not paint.
                    float shadeOuter = 0.82f + 0.18f * (float)(outer / r), shadeInner = 0.82f + 0.18f * (float)(inner / r);
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

            foreach (Band b in bands)
            {
                Annulus(b.Top, b.Bottom, b.Colour, b.Name == Localizer.Format("#Subterra_Layer_Ocean") ? 0.02f : 0f);
            }

            var line = new Color(0.06f, 0.06f, 0.06f, 1);
            double px = r * 0.0015;
            foreach (Band b in bands)
            {
                if (b.Bottom < r - 1)
                {
                    Annulus(b.Bottom - px, b.Bottom + px, line, 0.04f);
                }
            }

            Annulus(-px * 0.5, px * 1.5, new Color(0.95f, 0.95f, 0.95f, 1), 0.05f);

            // The blocks the face goes through: a lumpy disc where its plane cuts each (the face's x along its longitude, y north).
            var axisX = new Vector3(Mathf.Cos(longitude * Mathf.Deg2Rad), 0, Mathf.Sin(longitude * Mathf.Deg2Rad));
            var normal = new Vector3(-axisX.z, 0, axisX.x);
            foreach (Block block in blocks)
            {
                Core.Model.InclusionSpec inc = block.Spec;
                double la = inc.Lat * Math.PI / 180, lo = inc.Lon * Math.PI / 180, cr = r - inc.Depth;
                var c = new Vector3d(Math.Cos(la) * Math.Cos(lo), Math.Sin(la), Math.Cos(la) * Math.Sin(lo)) * cr;
                double d = Vector3d.Dot(c, (Vector3d)normal);
                double most = inc.Radius * (1 + inc.Lumpiness);
                if (Math.Abs(d) >= most)
                {
                    continue;
                }

                Vector3d p = c - (Vector3d)normal * d;
                double fx = Vector3d.Dot(p, (Vector3d)axisX), fy = p.y;
                if (fx < -most)
                {
                    continue;
                }

                const int sides = 40;
                int centreIndex = vertices.Count;
                vertices.Add(new Vector3((float)(fx * scale), (float)(fy * scale), toward * 0.03f));
                colours.Add(block.Colour);
                for (int k = 0; k <= sides; k++)
                {
                    double a = 2 * Math.PI * k / sides;
                    // The block's own lumpy radius in that direction, cut by the face's plane.
                    Vector3d inPlane = (Vector3d)axisX * Math.Cos(a) + new Vector3d(0, Math.Sin(a), 0);
                    double rs = inc.SurfaceRadius(new Core.Numerics.Vec3d(inPlane.x, inPlane.y, inPlane.z));
                    double rho = rs * rs > d * d ? Math.Sqrt(rs * rs - d * d) : 0;
                    double x = fx + Math.Cos(a) * rho, y = fy + Math.Sin(a) * rho;
                    vertices.Add(new Vector3((float)(Math.Max(0, x) * scale), (float)(y * scale), toward * 0.03f));
                    colours.Add(new Color(block.Colour.r * 0.85f, block.Colour.g * 0.85f, block.Colour.b * 0.85f, 1));
                    if (k > 0)
                    {
                        triangles.AddRange(new[] { centreIndex, centreIndex + k, centreIndex + k + 1 });
                    }
                }
            }
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
            float scale = (float)heightScale;
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

                // As wide as the name (Lucas: "Asthenosphere (partly" was cut short).
                float w = style.CalcSize(new GUIContent(b.Name)).x + 6;
                var rect = new Rect(at.x - w * 0.5f, Screen.height - at.y - 9, w, 20);
                if (drawn.Exists(r => r.Overlaps(rect)))
                {
                    continue;
                }

                drawn.Add(rect);
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), b.Name, shadow);
                GUI.Label(rect, b.Name, style);
            }

            keyRect.width = 380;
            keyRect.height = KeyTop + (bands.Count + blocks.Count) * KeyRow + 8;
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

            // The blocks: where their centre is and how big they are.
            for (int i = 0; i < blocks.Count; i++)
            {
                Block k = blocks[i];
                float y = KeyTop + (bands.Count + i) * KeyRow;
                GUI.DrawTexture(new Rect(10, y + 3, 18, 14), Swatch(k.Colour), ScaleMode.StretchToFill);
                GUI.Label(new Rect(34, y, keyRect.width - 42, KeyRow), Localizer.Format("#Subterra_LayerMap_Block", k.Name,
                    Km(k.Spec.Depth), Km(2 * k.Spec.Radius), k.Spec.Lat.ToString("F0"), k.Spec.Lon.ToString("F0")));
            }

            GUI.DragWindow();
        }

        private sealed class Block
        {
            public Core.Model.InclusionSpec Spec;
            public string Name;
            public Color Colour;
        }

        private readonly List<Block> blocks = new List<Block>();

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

            ShowOthers();

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
            Active = false;
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
