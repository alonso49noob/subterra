using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using KSP.UI.Screens;
using Subterra.Core.Radar;
using UnityEngine;
using static System.FormattableString;

namespace Subterra
{
    /// <summary>
    /// The radar layer on the map (spec 4.7): a transparent sphere, child of the focused body's <c>scaledBody</c>,
    /// carrying a 1024 x 512 texture that <see cref="OverlayPainter"/> paints from the body's coverage grids and
    /// detections. Shown in the map view (Flight) and in the Tracking Station while <see cref="Settings.RadarOverlay"/>
    /// is on and the toolbar button is pressed.
    ///
    /// Shape (ruling R10, from the M0 overlay spike): a hand-made 128 x 64 UV sphere, not Unity's 24 x 16 primitive,
    /// whose radius is the PQS <c>mapMaxHeight</c> above the body's radius (the scaled mesh itself carries no relief
    /// under Parallax), shader <c>Unlit/Transparent</c> (fallback <c>KSP/Alpha/Unlit Transparent</c>), render queue
    /// 3000, the layer of the scaled body. Scatterer is not in any test profile and was not tried.
    ///
    /// Alignment: the spike never calibrated the texture's longitude. The vertices are not placed by a guessed
    /// offset: the geographic frame (north, longitude 0, longitude 90) is measured on the real body through
    /// <see cref="CelestialBody.GetWorldSurfacePosition"/> and expressed in the scaled body's local frame, and the
    /// mesh is built from that basis, so longitude, north and handedness are those of the body whatever the scaled
    /// mesh's own UV layout is. Texture column x is centred on longitude -180 + (x + 0.5) * 360 / 1024, row y on
    /// latitude -90 + (y + 0.5) * 180 / 512 (<see cref="OverlayPainter"/>).
    ///
    /// Repainting: at most once every <see cref="RepaintSeconds"/>, when the coverage grids (identity or Version)
    /// or the detections of the body changed. The paint runs on <see cref="WorkerPool.Shared"/>; the main thread
    /// only uploads the finished bytes (one SetPixelData of level 0 + Apply with mipmaps), picked up here and not through
    /// <c>PumpMain</c>, which only the Flight scene pumps.
    /// </summary>
    public abstract class MapOverlay : MonoBehaviour
    {
        public const int TextureWidth = 1024;
        public const int TextureHeight = 512;

        /// <summary>Real seconds between two repaints at most.</summary>
        public const float RepaintSeconds = 2f;

        /// <summary>Real seconds to wait before painting again after a paint failed.</summary>
        public const float FailureBackoffSeconds = 10f;

        private const int LonSteps = 128;
        private const int LatSteps = 64;

        /// <summary>The shell sits this much above the highest terrain (R10: the 0.5 % of the spike).</summary>
        private const float ShellMargin = 1.005f;

        /// <summary>
        /// Used when a body reports no PQS height range (Kopernicus bodies may): 2 % of the radius over the sea
        /// level (decidido por Claude, revisar).
        /// </summary>
        private const double FallbackHeightFraction = 0.02;

        private static readonly string[] ShaderNames = { "Unlit/Transparent", "KSP/Alpha/Unlit Transparent" };

        // The button state is the player's choice and outlives the scene (decidido por Claude, revisar).
        private static bool userShown = true;

        private GameObject sphere;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material material;
        private Texture2D texture;
        private ApplicationLauncherButton button;
        private Texture2D buttonIcon;

        private CelestialBody shown;
        private CelestialBody failedBody;
        private float nextCheck;
        private bool painting;
        private static int debugFailPaints;
        private volatile int generation;
        private bool paintFailureLogged;

        // What the texture in the sphere (or the paint under way) was made from.
        private CoverageGrid paintedEco;
        private CoverageGrid paintedPlomada;
        private int paintedEcoVersion = -1;
        private int paintedPlomadaVersion = -1;
        private int paintedSignature;
        private bool paintedAny;

        private readonly object resultGate = new object();
        private PaintResult ready;
        private int uploadsLogged;

        public static MapOverlay Instance { get; private set; }

        /// <summary>The body the overlay is drawn on now, or null while it is hidden.</summary>
        public CelestialBody ShownBody => shown;

        /// <summary>Textures uploaded in this scene (diagnostics).</summary>
        public int Uploads { get; private set; }

        /// <summary>Main-thread milliseconds of the last upload (SetPixelData + Apply).</summary>
        public double LastUploadMs { get; private set; }

        /// <summary>Largest upload so far in this scene (ms).</summary>
        public double MaxUploadMs { get; private set; }

        /// <summary>Makes the next <paramref name="count"/> paints fail on the worker (tests of the failure path).</summary>
        public static void FailNextPaints(int count) => Interlocked.Exchange(ref debugFailPaints, count);

        /// <summary>Paints that failed in this scene (diagnostics).</summary>
        public int PaintFailures { get; private set; }

        /// <summary>Paints finished in this scene (diagnostics).</summary>
        public int Paints { get; private set; }

        /// <summary>The texel the overlay shows at a point (diagnostics for tests; transparent while there is no texture).</summary>
        public Color32 PixelAt(double lat, double lon)
        {
            return texture == null ? default(Color32) : (Color32)texture.GetPixel(OverlayPainter.ColumnOf(TextureWidth, lon), OverlayPainter.RowOf(TextureHeight, lat));
        }

        /// <summary>The sphere is in the scene and enabled (diagnostics).</summary>
        public bool SphereActive => sphere != null && sphere.activeInHierarchy && meshRenderer != null && meshRenderer.enabled;

        /// <summary>The overlay should be visible if the map is (the toolbar button state).</summary>
        public static bool UserShown
        {
            get => userShown;
            set
            {
                userShown = value;
                Log.Info("MapOverlay: UserShown = " + value + (Instance != null && Instance.button != null ? " (button synced)" : " (no button)"));
                if (Instance != null && Instance.button != null)
                {
                    if (value)
                    {
                        Instance.button.SetTrue(false);
                    }
                    else
                    {
                        Instance.button.SetFalse(false);
                    }
                }
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (!Settings.Current.RadarOverlay)
            {
                return;
            }

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

            button = null;
            Teardown();
            if (buttonIcon != null)
            {
                Destroy(buttonIcon);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null)
            {
                return;
            }

            if (buttonIcon == null)
            {
                buttonIcon = MakeIcon();
            }

            button = ApplicationLauncher.Instance.AddModApplication(
                () => SetShown(true), () => SetShown(false), null, null, null, null,
                ApplicationLauncher.AppScenes.MAPVIEW | ApplicationLauncher.AppScenes.TRACKSTATION, buttonIcon);
            if (userShown)
            {
                button.SetTrue(false);
            }
        }

        private static void SetShown(bool value)
        {
            userShown = value;
            Log.Info("MapOverlay: toolbar button " + (value ? "pressed" : "released"));
        }

        private void OnLauncherDestroyed()
        {
            button = null;
        }

        private void Update()
        {
            using (SubterraClock.Measure("MapOverlay"))
            {
                CelestialBody wanted = Settings.Current.RadarOverlay && userShown && !LayerMap.Active ? FocusedBody() : null;
                if (wanted != failedBody)
                {
                    failedBody = null;
                }

                if (wanted != shown && (wanted == null || wanted != failedBody))
                {
                    Show(wanted);
                }

                if (shown == null)
                {
                    return;
                }

                TakeResult();
                if (!painting && Time.realtimeSinceStartup >= nextCheck)
                {
                    CheckRepaint();
                }
            }
        }

        /// <summary>The body the map camera is on (the main body of a focused vessel or node), or null outside the map.</summary>
        private static CelestialBody FocusedBody()
        {
            bool inMap = HighLogic.LoadedScene == GameScenes.TRACKSTATION || (HighLogic.LoadedScene == GameScenes.FLIGHT && MapView.MapIsEnabled);
            if (!inMap || PlanetariumCamera.fetch == null)
            {
                return null;
            }

            MapObject target = PlanetariumCamera.fetch.target;
            if (target == null)
            {
                return null;
            }

            CelestialBody body = target.celestialBody;
            if (body == null && target.vessel != null)
            {
                body = target.vessel.mainBody;
            }

            if (body == null && target.maneuverNode != null && target.maneuverNode.patch != null)
            {
                body = target.maneuverNode.patch.referenceBody;
            }

            // A body without a surface (the Sun, gas giants) has no coverage to show.
            return body != null && body.pqsController != null && body.scaledBody != null ? body : null;
        }

        private void Show(CelestialBody body)
        {
            if (body == null)
            {
                // Hidden, not destroyed: coming back to the same body costs no rebuild.
                if (sphere != null)
                {
                    sphere.SetActive(false);
                }

                shown = null;
                return;
            }

            if (sphere == null || sphere.transform.parent != body.scaledBody.transform)
            {
                bool built;
                try
                {
                    built = Build(body);
                }
                catch (Exception e)
                {
                    Log.Error($"MapOverlay: could not build the overlay of {body.bodyName}: {e}");
                    built = false;
                }

                if (!built)
                {
                    // Not retried every frame: the body is remembered as failed until the focus moves elsewhere.
                    Teardown();
                    failedBody = body;
                    return;
                }
            }

            sphere.SetActive(true);
            shown = body;
            nextCheck = 0;
        }

        private bool Build(CelestialBody body)
        {
            Teardown();
            Shader shader = ShaderNames.Select(Shader.Find).FirstOrDefault(s => s != null);
            if (shader == null)
            {
                Log.Warn("MapOverlay: no transparent shader found (" + string.Join(", ", ShaderNames) + "); the overlay is off");
                return false;
            }

            Transform parent = body.scaledBody.transform;
            var watch = Stopwatch.StartNew();
            mesh = BuildSphere(body, parent, out string report);
            texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, true)
            {
                name = "SubterraRadarOverlay",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            Clear();

            material = new Material(shader) { name = "SubterraRadarOverlay", mainTexture = texture, color = Color.white };
            material.renderQueue = 3000;

            sphere = new GameObject("SubterraRadarOverlay") { layer = parent.gameObject.layer };
            sphere.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = sphere.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            sphere.transform.SetParent(parent, false);

            double height = body.pqsController.mapMaxHeight;
            if (!(height > 0))
            {
                height = body.Radius * FallbackHeightFraction;
            }

            // The scaled body has radius 1000 local units at body.Radius; the sphere mesh has radius 0.5.
            float radius = (float)(1000.0 * (1.0 + height / body.Radius) * ShellMargin);
            sphere.transform.localScale = Vector3.one * (2f * radius);

            paintedEco = null;
            paintedPlomada = null;
            paintedEcoVersion = -1;
            paintedPlomadaVersion = -1;
            paintedSignature = 0;
            paintedAny = false;
            generation++;
            painting = false;
            lock (resultGate)
            {
                ready = null;
            }

            Log.Info(Invariant($"MapOverlay: sphere on {body.bodyName} (scaled radius {radius:F1}, shader '{shader.name}', queue {material.renderQueue}) built in {watch.Elapsed.TotalMilliseconds:F1} ms; {report}"));
            return true;
        }

        private void Teardown()
        {
            if (sphere != null)
            {
                Destroy(sphere);
            }

            if (mesh != null)
            {
                Destroy(mesh);
            }

            if (material != null)
            {
                Destroy(material);
            }

            if (texture != null)
            {
                Destroy(texture);
            }

            sphere = null;
            mesh = null;
            material = null;
            texture = null;
            meshRenderer = null;
            shown = null;
            generation++;
        }

        // ------------------------------------------------------------------ painting

        private void CheckRepaint()
        {
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null)
            {
                return;
            }

            string name = shown.bodyName;
            // TryCoverage: a reader must not create (and keep) a 1 MB empty grid for every body it looks at. The
            // grids are fetched again every check: a reload replaces the objects, so identity counts as well as Version.
            scenario.TryCoverage(name, InstrumentKind.OrbitalRadar, out CoverageGrid eco);
            scenario.TryCoverage(name, InstrumentKind.Gravimeter, out CoverageGrid plomada);
            List<NetworkDetection> detections = scenario.Detections.All.Where(d => d.Body == name && d.Level != DetectionLevel.None).ToList();

            // Versions first, then the cells: a change after this read bumps Version again and is painted next time.
            int ecoVersion = eco != null ? eco.Version : -1;
            int plomadaVersion = plomada != null ? plomada.Version : -1;
            int signature = Signature(detections);
            bool any = eco != null || plomada != null || detections.Count > 0;
            bool unchanged = eco == paintedEco && plomada == paintedPlomada && ecoVersion == paintedEcoVersion
                && plomadaVersion == paintedPlomadaVersion && signature == paintedSignature;
            nextCheck = Time.realtimeSinceStartup + RepaintSeconds;
            if (unchanged || !any && !paintedAny)
            {
                return;
            }

            paintedEco = eco;
            paintedPlomada = plomada;
            paintedEcoVersion = ecoVersion;
            paintedPlomadaVersion = plomadaVersion;
            paintedSignature = signature;
            paintedAny = any;
            painting = true;
            int forGeneration = generation;
            double radius = shown.Radius;
            WorkerPool.Shared.Enqueue(() =>
            {
                // WorkerPool swallows exceptions, so this always posts a result (null pixels on failure): the main
                // thread must learn that the paint is over or the overlay would wait for it forever.
                var clock = Stopwatch.StartNew();
                byte[] pixels = null;
                bool injected = false;
                try
                {
                    if (Interlocked.Decrement(ref debugFailPaints) >= 0)
                    {
                        injected = true;
                        throw new InvalidOperationException("paint failure requested by a test");
                    }

                    Interlocked.Exchange(ref debugFailPaints, 0);
                    pixels = OverlayPainter.Paint(TextureWidth, TextureHeight, eco, plomada, detections, radius);
                }
                catch (Exception e)
                {
                    // A failure a test asked for is not an error (the scenario checks the log for errors).
                    string text = $"MapOverlay: painting {name} failed: {e.Message}";
                    if (injected)
                    {
                        Log.Warn(text);
                    }
                    else
                    {
                        Log.Error($"MapOverlay: painting {name} failed: {e}");
                    }
                }
                finally
                {
                    lock (resultGate)
                    {
                        // A result for a sphere that is gone must not take the slot of the paint now in flight.
                        if (forGeneration == generation)
                        {
                            ready = new PaintResult { Pixels = pixels, Generation = forGeneration, PaintMs = clock.Elapsed.TotalMilliseconds };
                        }
                    }
                }
            });
        }

        private static int Signature(List<NetworkDetection> detections)
        {
            unchecked
            {
                int hash = detections.Count;
                foreach (NetworkDetection d in detections)
                {
                    hash = hash * 31 + (d.NetworkId != null ? d.NetworkId.GetHashCode() : 0);
                    hash = hash * 31 + (int)d.Level;
                    hash = hash * 31 + d.Uncertainty.GetHashCode();
                    hash = hash * 31 + d.Lat.GetHashCode();
                    hash = hash * 31 + d.Lon.GetHashCode();
                    hash = hash * 31 + d.DepthMin.GetHashCode();
                    hash = hash * 31 + d.DepthMax.GetHashCode();
                }

                return hash;
            }
        }

        private void TakeResult()
        {
            PaintResult result;
            lock (resultGate)
            {
                result = ready;
                ready = null;
            }

            if (result == null)
            {
                return;
            }

            if (result.Generation != generation || texture == null)
            {
                return; // painted for a sphere that no longer exists; the paint in flight (if any) is not this one
            }

            painting = false;
            if (result.Pixels == null)
            {
                // The paint failed: forget what was "painted" so the next check paints again, after a longer wait.
                paintedEco = null;
                paintedPlomada = null;
                paintedEcoVersion = -1;
                paintedPlomadaVersion = -1;
                paintedSignature = 0;
                paintedAny = true;
                PaintFailures++;
                nextCheck = Time.realtimeSinceStartup + FailureBackoffSeconds;
                if (!paintFailureLogged)
                {
                    paintFailureLogged = true;
                    Log.Warn("MapOverlay: a paint failed; it is tried again in " + FailureBackoffSeconds + " s (logged once)");
                }

                return;
            }

            using (SubterraClock.Measure("MapOverlay.Upload"))
            {
                var watch = Stopwatch.StartNew();
                texture.SetPixelData(result.Pixels, 0);
                double loadMs = watch.Elapsed.TotalMilliseconds;
                texture.Apply(true, false);
                LastUploadMs = watch.Elapsed.TotalMilliseconds;
                MaxUploadMs = Math.Max(MaxUploadMs, LastUploadMs);
                Uploads++;
                Paints++;
                if (Settings.Current.Debug || uploadsLogged < 8)
                {
                    uploadsLogged++;
                    Log.Info(Invariant($"MapOverlay: {shown?.bodyName} painted in {result.PaintMs:F1} ms on a worker, uploaded in {LastUploadMs:F2} ms (SetPixelData {loadMs:F2} ms, Apply with mipmaps {LastUploadMs - loadMs:F2} ms)"));
                }
            }
        }

        private void Clear()
        {
            var clear = new byte[TextureWidth * TextureHeight * 4];
            texture.SetPixelData(clear, 0);
            texture.Apply(true, false);
        }

        private sealed class PaintResult
        {
            public byte[] Pixels;
            public int Generation;
            public double PaintMs;
        }

        // ------------------------------------------------------------------ the sphere

        /// <summary>
        /// A unit-diameter UV sphere whose vertex for (latitude, longitude) sits where that point of the real body is
        /// in the scaled body's local frame, so texture column x is longitude -180 + 360 * x / width and row y is
        /// latitude -90 + 180 * y / height whatever the scaled mesh's UVs do.
        /// </summary>
        private static Mesh BuildSphere(CelestialBody body, Transform parent, out string report)
        {
            // Where the geographic axes are in the scaled body's frame: longitude 0 on the equator, longitude 90 on
            // the equator, the north pole.
            Vector3 east0 = LocalDirection(body, parent, 0, 0);
            Vector3 east90 = LocalDirection(body, parent, 0, 90);
            Vector3 north = LocalDirection(body, parent, 90, 0);

            var vertices = new Vector3[(LonSteps + 1) * (LatSteps + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int j = 0; j <= LatSteps; j++)
            {
                float lat = Mathf.PI * j / LatSteps - Mathf.PI / 2f;
                float cosLat = Mathf.Cos(lat), sinLat = Mathf.Sin(lat);
                for (int i = 0; i <= LonSteps; i++)
                {
                    // Longitude -180 at the left edge of the texture, +180 at the right edge.
                    float lon = 2f * Mathf.PI * i / LonSteps - Mathf.PI;
                    int index = j * (LonSteps + 1) + i;
                    vertices[index] = 0.5f * (cosLat * Mathf.Cos(lon) * east0 + sinLat * north + cosLat * Mathf.Sin(lon) * east90);
                    uvs[index] = new Vector2((float)i / LonSteps, (float)j / LatSteps);
                }
            }

            // Which way round is outward depends on the handedness of the measured basis.
            int mid = (LatSteps / 2) * (LonSteps + 1) + LonSteps / 4;
            Vector3 a = vertices[mid], b = vertices[mid + LonSteps + 1], c = vertices[mid + 1];
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0;

            var triangles = new int[LonSteps * LatSteps * 6];
            int t = 0;
            for (int j = 0; j < LatSteps; j++)
            {
                for (int i = 0; i < LonSteps; i++)
                {
                    int p = j * (LonSteps + 1) + i;
                    int q = p + LonSteps + 1;
                    Tri(triangles, ref t, p, q, p + 1, flip);
                    Tri(triangles, ref t, p + 1, q, q + 1, flip);
                }
            }

            var result = new Mesh { name = "SubterraRadarOverlaySphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            result.vertices = vertices;
            result.uv = uvs;
            result.triangles = triangles;
            result.RecalculateBounds();

            // A fourth point away from the three that built the basis: the basis must predict it (it is a rotation or
            // a reflection of the scaled frame; anything else means the scaled body is not a plain turned sphere).
            Vector3 predicted = Predict(east0, north, east90, 30, 40);
            Vector3 measured = LocalDirection(body, parent, 30, 40);
            float error = Vector3.Angle(predicted, measured);
            float det = Vector3.Dot(Vector3.Cross(east0, north), east90);
            report = Invariant($"basis lon0 {Fmt(east0)} north {Fmt(north)} lon90 {Fmt(east90)}, handedness {(det > 0 ? "+" : "-")}, check point error {error:F3} deg, triangles {(flip ? "flipped" : "as built")}");
            if (error > 0.5f)
            {
                Log.Warn(Invariant($"MapOverlay: the geographic frame of {body.bodyName} does not match its scaled body (error {error:F2} deg); the overlay may be misplaced"));
            }

            return result;
        }

        private static void Tri(int[] triangles, ref int t, int a, int b, int c, bool flip)
        {
            triangles[t++] = a;
            triangles[t++] = flip ? c : b;
            triangles[t++] = flip ? b : c;
        }

        private static Vector3 Predict(Vector3 east0, Vector3 north, Vector3 east90, double lat, double lon)
        {
            double la = lat * Math.PI / 180, lo = lon * Math.PI / 180;
            return ((float)(Math.Cos(la) * Math.Cos(lo)) * east0 + (float)Math.Sin(la) * north + (float)(Math.Cos(la) * Math.Sin(lo)) * east90).normalized;
        }

        /// <summary>The direction of a surface point from the body's centre, in the local frame of the scaled body.</summary>
        private static Vector3 LocalDirection(CelestialBody body, Transform parent, double lat, double lon)
        {
            Vector3d world = (body.GetWorldSurfacePosition(lat, lon, 0) - body.position).normalized;
            return parent.InverseTransformDirection(new Vector3((float)world.x, (float)world.y, (float)world.z)).normalized;
        }

        private static string Fmt(Vector3 v) => Invariant($"({v.x:F3},{v.y:F3},{v.z:F3})");

        // ------------------------------------------------------------------ the toolbar icon

        // 38 x 38, white on clear like the stock toolbar icons: a ring (the body) with a scanned band across it.
        private static Texture2D MakeIcon()
        {
            const int size = 38;
            var pixels = new Color32[size * size];
            float centre = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre, dy = y - centre;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = r >= 13f && r <= 15.5f;
                    bool band = r < 13f && Mathf.Abs(dy - dx * 0.35f) < 2.5f;
                    bool dot = (dx - 5) * (dx - 5) + (dy + 4) * (dy + 4) < 6f;
                    pixels[y * size + x] = ring || band || dot ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }

            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SubterraRadarOverlayIcon" };
            icon.SetPixels32(pixels);
            icon.Apply(false, false);
            return icon;
        }
    }

    /// <summary>The map overlay in the Flight scene (it only draws while the map view is open).</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class MapOverlayFlight : MapOverlay
    {
    }

    /// <summary>The map overlay in the Tracking Station.</summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public sealed class MapOverlayTrackingStation : MapOverlay
    {
    }
}
