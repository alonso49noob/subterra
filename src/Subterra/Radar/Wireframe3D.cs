using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Subterra.Core.Numerics;
using Subterra.Core.Radar;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The 3D outline of the voids the radar has detected (spec 4.5): line segments drawn with GL on the flight camera,
    /// through the terrain, for the networks within <see cref="Range"/> of the camera that have detected primitives.
    /// Visible for <see cref="VisibleSeconds"/> after the last sample of the Zahori (<see cref="Touch"/>) or after a
    /// network was shown by name (<see cref="Show"/>: the seismic survey uses it), or always while pinned
    /// (<see cref="Pin"/>). The lines are built by <see cref="WireframeBuilder"/> on a worker thread, only when the set
    /// of detected primitives of a network changes, and kept in the network's local frame: every frame a matrix puts that
    /// frame in world space again (KSP moves the world origin as the vessel moves), so nothing is recomputed per vertex.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Wireframe3D : MonoBehaviour
    {
        /// <summary>Networks whose bounds are farther than this (m) from the camera are not drawn.</summary>
        public const double Range = 2000;

        /// <summary>Seconds (real time) the lines stay after the last sample or <see cref="Show"/>.</summary>
        public const float VisibleSeconds = 60f;

        /// <summary>
        /// Most segments drawn in one frame (decidido por Claude, revisar): GL immediate mode costs a call per vertex,
        /// so 4000 segments (8000 vertices) stay well under the 2 ms budget. The nearest networks are drawn first.
        /// </summary>
        public const int MaxSegments = 4000;

        // The candidate list is rebuilt this often (real seconds) and whenever the detections or the networks change.
        private const float RefreshSeconds = 0.5f;

        // Frames a change waits before the candidates are rebuilt, and frames a new plan waits before it is drawn: the
        // frame that took a network to Detected also ran the radar sample, and the first refresh and the first draw are
        // slow (code and shader set-up), so they go in frames of their own (decidido por Claude, revisar).
        private const int SettleFrames = 2;

        private sealed class Built
        {
            public string Key;
            public TubeNetwork Net;
            public Vector3[] Verts; // pairs: segment i is Verts[2i] to Verts[2i + 1], in the network's local frame
        }

        private sealed class Entry
        {
            public volatile Built Built;
            public string BuildingKey;
            public TubeNetwork BuildingNet;
            public float Alpha;
            public float Distance;
        }

        private static readonly HashSet<object> Pinners = new HashSet<object>();
        private static readonly Dictionary<string, float> ShownUntil = new Dictionary<string, float>();
        private static float lastTouch = -1000f;
        private static volatile bool dirty;

        /// <summary>Segments the last frame drew (for the in-game tests).</summary>
        public static int VisibleSegments { get; private set; }

        /// <summary>Frames in which the lines were drawn so far (for the in-game tests).</summary>
        public static int DrawnFrames { get; private set; }

        /// <summary>The camera the lines were first drawn on, with its clip planes (for the log and the in-game tests).</summary>
        public static string LastCamera { get; private set; } = "";

        public static bool IsPinned => Pinners.Count > 0;

        /// <summary>A sample of the Zahori was taken: the lines show (or stay) for <see cref="VisibleSeconds"/>.</summary>
        public static void Touch() => lastTouch = Time.realtimeSinceStartup;

        /// <summary>Shows the outline of one network for <see cref="VisibleSeconds"/> (a seismic survey of it was taken).</summary>
        public static void Show(string networkId)
        {
            if (!string.IsNullOrEmpty(networkId))
            {
                ShownUntil[networkId] = Time.realtimeSinceStartup + VisibleSeconds;
            }
        }

        /// <summary>Keeps the lines on (pinned by <paramref name="owner"/>, an instrument part) or lets the owner go.</summary>
        public static void Pin(object owner, bool on)
        {
            if (owner == null)
            {
                return;
            }

            if (on)
            {
                Pinners.Add(owner);
            }
            else
            {
                Pinners.Remove(owner);
            }
        }

        /// <summary>The opacity of the outline: clear when the position is known to a few metres, faint when it is vague.</summary>
        public static float Alpha(double uncertaintyMetres) => (float)Math.Max(0.3, Math.Min(1.0, 1.0 - uncertaintyMetres / 50.0));

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly List<Entry> plan = new List<Entry>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private Material material;
        private float nextRefresh;
        private BodyRuntime refreshedBody;
        private int storeVersion = -1;
        private int catalogVersion = -1;
        private DetectionStore store;
        private int errors;
        private int settleSince = -1;
        private int planFrame = -1;

        private void Start()
        {
            Camera.onPostRender += OnPostRender;
            // Shader lookup and material creation now, not in the first frame the lines show.
            MakeMaterial();
        }

        private void OnDestroy()
        {
            Camera.onPostRender -= OnPostRender;
            if (material != null)
            {
                Destroy(material);
            }

            VisibleSegments = 0;
        }

        private void Update()
        {
            try
            {
                using (SubterraClock.Measure("Wireframe3D"))
                {
                    Refresh();
                }
            }
            catch (Exception e)
            {
                plan.Clear();
                VisibleSegments = 0;
                if (errors++ < 5)
                {
                    Log.Error($"Wireframe3D update failed: {e}");
                }
            }
        }

        // Works out which networks to draw: only when something changed or every RefreshSeconds.
        private void Refresh()
        {
            RadarScenario scenario = RadarScenario.Instance;
            FlightCamera flightCamera = FlightCamera.fetch;
            CelestialBody body = FlightGlobals.currentMainBody;
            if (scenario == null || !FlightGlobals.ready || flightCamera == null || body == null
                || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            bool all = IsPinned || now - lastTouch < VisibleSeconds;
            bool any = all;
            if (!all)
            {
                foreach (KeyValuePair<string, float> pair in ShownUntil)
                {
                    any |= pair.Value > now;
                }
            }

            if (!any)
            {
                Clear();
                return;
            }

            // The store is recreated with every scene's scenario: follow the current one.
            if (!ReferenceEquals(store, scenario.Detections))
            {
                store = scenario.Detections;
                storeVersion = -1;
            }

            bool changed = dirty || !ReferenceEquals(refreshedBody, runtime) || store.Version != storeVersion
                || runtime.Catalog.Version != catalogVersion || now >= nextRefresh;
            if (!changed)
            {
                settleSince = -1;
                return;
            }

            if (settleSince < 0)
            {
                settleSince = Time.frameCount;
            }

            if (Time.frameCount - settleSince < SettleFrames)
            {
                return;
            }

            settleSince = -1;
            planFrame = Time.frameCount;
            dirty = false;
            nextRefresh = now + RefreshSeconds;
            refreshedBody = runtime;
            storeVersion = store.Version;
            catalogVersion = runtime.Catalog.Version;

            Vec3d cameraBody = Frames.ToBody(body, flightCamera.transform.position);
            plan.Clear();
            seen.Clear();
            foreach (TubeNetwork net in runtime.Catalog.NetworksNear(cameraBody, Range))
            {
                if (!all && !(ShownUntil.TryGetValue(net.Id, out float until) && until > now))
                {
                    continue;
                }

                NetworkDetection detection = store.Get(net.Id);
                if (detection == null || !AnyPrimitive(detection))
                {
                    continue;
                }

                seen.Add(net.Id);
                if (!entries.TryGetValue(net.Id, out Entry entry))
                {
                    entry = new Entry();
                    entries[net.Id] = entry;
                }

                entry.Alpha = Alpha(detection.Uncertainty);
                entry.Distance = net.Bounds.DistanceTo(net.ToLocal(cameraBody));

                // Rebuild when the detected primitives changed or the network was replaced (a tunnel dug into it).
                string key = detection.PrimitivesHex;
                Built built = entry.Built;
                if (built == null || built.Key != key || !ReferenceEquals(built.Net, net))
                {
                    StartBuild(entry, net, key, detection);
                }

                if (entry.Built != null)
                {
                    plan.Add(entry);
                }
            }

            plan.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            foreach (string id in entries.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                entries.Remove(id);
            }

            int total = 0;
            foreach (Entry e in plan)
            {
                total += e.Built.Verts.Length / 2;
            }

            VisibleSegments = Math.Min(total, MaxSegments);
        }

        private void Clear()
        {
            plan.Clear();
            VisibleSegments = 0;
        }

        private static bool AnyPrimitive(NetworkDetection d)
        {
            System.Collections.BitArray bits = d.Primitives;
            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i])
                {
                    return true;
                }
            }

            return false;
        }

        // Builds the lines on a worker thread; the old lines keep being drawn until the new ones are there.
        private static void StartBuild(Entry entry, TubeNetwork net, string key, NetworkDetection detection)
        {
            if (entry.BuildingKey == key && ReferenceEquals(entry.BuildingNet, net))
            {
                return;
            }

            entry.BuildingKey = key;
            entry.BuildingNet = net;
            System.Collections.BitArray bits = detection.Primitives;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var lines = WireframeBuilder.Build(net, i => i < bits.Length && bits[i]);
                    var verts = new Vector3[lines.Count * 2];
                    for (int i = 0; i < lines.Count; i++)
                    {
                        verts[2 * i] = lines[i].a.ToUnity();
                        verts[2 * i + 1] = lines[i].b.ToUnity();
                    }

                    entry.Built = new Built { Key = key, Net = net, Verts = verts };
                }
                catch (Exception e)
                {
                    Log.Warn($"Wireframe3D: building the outline of {net.Id} failed: {e.Message}");
                }
                finally
                {
                    dirty = true;
                }
            });
        }

        private void OnPostRender(Camera camera)
        {
            if (plan.Count == 0 || Time.frameCount <= planFrame || FlightCamera.fetch == null || camera != FlightCamera.fetch.mainCamera)
            {
                return;
            }

            try
            {
                using (SubterraClock.Measure("Wireframe3D"))
                {
                    Draw(camera);
                }
            }
            catch (Exception e)
            {
                plan.Clear();
                VisibleSegments = 0;
                if (errors++ < 5)
                {
                    Log.Error($"Wireframe3D draw failed: {e}");
                }
            }
        }

        private void Draw(Camera camera)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null || body.pqsController == null)
            {
                return;
            }

            if (material == null)
            {
                return;
            }

            Quaternion rotation = body.pqsController.transform.rotation;
            int left = MaxSegments;
            int drawn = 0;
            material.SetPass(0);
            GL.PushMatrix();
            foreach (Entry entry in plan)
            {
                Built built = entry.Built;
                if (left <= 0 || built == null)
                {
                    break;
                }

                // Local frame -> world: the PQS axes moved to the anchor. The anchor is near the floating origin, so
                // its single-precision position is exact enough; the vertices are relative to it.
                Vector3 anchor = (Vector3)Frames.ToWorld(body, built.Net.Anchor);
                GL.PushMatrix();
                GL.MultMatrix(Matrix4x4.TRS(anchor, rotation, Vector3.one));
                GL.Begin(GL.LINES);
                GL.Color(new Color(0.35f, 1f, 0.85f, entry.Alpha));
                Vector3[] verts = built.Verts;
                int count = Math.Min(left, verts.Length / 2);
                for (int i = 0; i < count; i++)
                {
                    GL.Vertex(verts[2 * i]);
                    GL.Vertex(verts[2 * i + 1]);
                }

                GL.End();
                GL.PopMatrix();
                left -= count;
                drawn += count;
            }

            GL.PopMatrix();
            VisibleSegments = drawn;
            DrawnFrames++;
            if (LastCamera.Length == 0)
            {
                LastCamera = $"{camera.name} (near {camera.nearClipPlane:F1}, far {camera.farClipPlane:F0}, depth {camera.depth:F0})";
                Log.Info($"Wireframe3D: drawing on camera {LastCamera}, {drawn} segment(s)");
            }
        }

        // Alpha-blended, no culling, no depth test or write, so the lines show through the terrain. Hidden/Internal-Colored
        // is the shader of Unity's own GL examples; the others are fallbacks.
        private bool MakeMaterial()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ?? Shader.Find("KSP/Particles/Alpha Blended");
            if (shader == null)
            {
                Log.Warn("Wireframe3D: no shader for the lines (Hidden/Internal-Colored not found); the outline will not be drawn");
                return false;
            }

            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            Log.Info($"Wireframe3D: lines use shader {shader.name}");
            return true;
        }
    }
}
