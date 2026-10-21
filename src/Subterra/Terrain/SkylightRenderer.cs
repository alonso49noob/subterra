using System.Collections;
using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using UnityEngine;
using UnityEngine.Rendering;

namespace Subterra
{
    /// <summary>
    /// Per skylight: the debris rim (mesh + collider over the jagged terrain cut, built near the
    /// camera from sampled terrain heights) and the far cap (a dark disc shown while the terrain over
    /// the hole is not cut, up to CapRange). Objects are placed in world space each frame in double
    /// precision.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class SkylightRenderer : MonoBehaviour
    {
        private const int Segments = 48;
        private const int Rings = 8;
        private readonly Dictionary<(string, int), Entry> entries = new Dictionary<(string, int), Entry>();
        private CelestialBody body;
        private Mesh capMesh;
        private Material capMaterial;
        private float nextScan;

        public static SkylightRenderer Instance { get; private set; }

        /// <summary>Whether the cap of the skylight nearest to <paramref name="world"/> is shown (null if none is tracked).</summary>
        public bool? CapVisibleNear(Vector3d world)
        {
            Entry best = null;
            double bestDistance = double.MaxValue;
            foreach (Entry e in entries.Values)
            {
                double d = (e.World - world).magnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = e;
                }
            }

            return best == null ? (bool?)null : best.Cap != null && best.Cap.activeSelf;
        }

        public int RimCount
        {
            get
            {
                int n = 0;
                foreach (Entry e in entries.Values)
                {
                    if (e.Rim != null)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private void Awake()
        {
            Instance = this;
            capMesh = Disc(32);
            capMaterial = new Material(Shader.Find("KSP/Unlit")) { color = new Color(0.01f, 0.01f, 0.012f) };
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Clear();
            Destroy(capMesh);
            Destroy(capMaterial);
        }

        private void Clear()
        {
            foreach (Entry e in entries.Values)
            {
                // A rim still being built sees this and throws its result away.
                e.Cancelled = true;
                if (e.Rim != null)
                {
                    Destroy(e.Rim.GetComponent<MeshFilter>().sharedMesh);
                    Destroy(e.Rim);
                }

                if (e.Cap != null)
                {
                    Destroy(e.Cap);
                }
            }

            entries.Clear();
        }

        private void LateUpdate()
        {
            if (!FlightGlobals.ready || FlightCamera.fetch == null)
            {
                return;
            }

            using (SubterraClock.Measure("SkylightRenderer"))
            {
                if (body != FlightGlobals.currentMainBody)
                {
                    Clear();
                    body = FlightGlobals.currentMainBody;
                }

                if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
                {
                    return;
                }

                Settings settings = Settings.Current;
                Vector3d camera = FlightCamera.fetch.mainCamera.transform.position;
                if (Time.realtimeSinceStartup >= nextScan)
                {
                    nextScan = Time.realtimeSinceStartup + 1f;
                    Scan(runtime, Frames.ToBody(body, camera), settings.CapRange);
                }

                Quaternion pqsRotation = body.pqsController.transform.rotation;
                foreach (Entry e in entries.Values)
                {
                    e.World = Frames.ToWorld(body, e.Net.ToBody(e.Skylight.SurfaceCenter));
                    double distance = (e.World - camera).magnitude;
                    Quaternion rotation = pqsRotation * e.LocalRotation;
                    if (distance < settings.VisualRange && e.Rim == null && !e.Building)
                    {
                        StartCoroutine(BuildRim(runtime, e));
                    }

                    bool open = SkylightInjector.IsOpen(body, e.Net, e.Skylight);
                    bool showCap = distance < settings.CapRange && (!open || distance > settings.CapMinDistance);
                    if (showCap && e.Cap == null)
                    {
                        e.Cap = CreateCap(e);
                    }

                    if (e.Cap != null)
                    {
                        e.Cap.SetActive(showCap);
                        if (showCap)
                        {
                            float lift = e.CapHeight + 0.5f + 0.0005f * (float)distance;
                            e.Cap.transform.SetPositionAndRotation(e.World + (Vector3d)(rotation * Vector3.up * lift), rotation);
                        }
                    }
                }
            }
        }

        private void Scan(BodyRuntime runtime, Vec3d camera, double range)
        {
            foreach (TubeNetwork net in runtime.Catalog.NetworksNear(camera, range))
            {
                foreach (Skylight s in net.Skylights)
                {
                    var key = (net.Id, s.Index);
                    if (!entries.ContainsKey(key))
                    {
                        Vec3 up = s.Up;
                        Vec3 forward = Vec3.Cross(up, Mathf.Abs(up.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized;
                        entries[key] = new Entry
                        {
                            Net = net,
                            Skylight = s,
                            Up = up,
                            Forward = forward,
                            Right = Vec3.Cross(up, forward),
                            LocalRotation = Quaternion.LookRotation(forward.ToUnity(), up.ToUnity()),
                        };
                    }
                }
            }
        }

        private IEnumerator BuildRim(BodyRuntime runtime, Entry e)
        {
            e.Building = true;
            PQSMod_SubterraSkylight mod = SkylightInjector.ModFor(body.pqsController);
            float inner = e.Skylight.Radius;
            float outer = inner + (mod != null ? mod.CutReach : 60f) + 5f;
            var points = new List<(double lat, double lon)>();
            var planar = new List<Vec3>();
            for (int r = 0; r < Rings; r++)
            {
                float radius = inner + (outer - inner) * r / (Rings - 1);
                for (int s = 0; s < Segments; s++)
                {
                    float a = 2 * Mathf.PI * s / Segments;
                    Vec3 offset = e.Right * (Mathf.Cos(a) * radius) + e.Forward * (Mathf.Sin(a) * radius);
                    Vec3d bodyPos = e.Net.ToBody(e.Skylight.SurfaceCenter + offset);
                    Core.Geo.BodyFrame.ToLatLon(bodyPos, out double lat, out double lon);
                    points.Add((lat, lon));
                    planar.Add(offset);
                }
            }

            var heights = new float[points.Count];
            Vec3d centre = e.Net.ToBody(e.Skylight.SurfaceCenter);
            Vec3d up = Vec3d.FromVec3(e.Up);
            yield return KspSampler.Fill(body, points, (i, radius, biome) =>
            {
                Vec3d terrain = Core.Geo.BodyFrame.Direction(points[i].lat, points[i].lon) * radius;
                heights[i] = (float)Vec3d.Dot(terrain - centre, up);
            }, 2f);

            MeshData data = RimBuilder.Build(inner, outer, Segments, Rings, heights, Hash.Combine(Hash.Of(e.Net.Id), (ulong)e.Skylight.Index));
            float max = 0;
            foreach (float h in heights)
            {
                max = Mathf.Max(max, h);
            }

            e.CapHeight = max;
            if (e.Cancelled || body == null || body.pqsController == null)
            {
                yield break;
            }

            // A child of the PQS: it moves with the terrain, in step with physics (it has a collider).
            var go = new GameObject($"SubterraRim {e.Net.Id} {e.Skylight.Index}") { layer = ChunkObject.Layer };
            go.transform.SetParent(body.pqsController.transform, false);
            go.transform.localPosition = e.Net.ToBody(e.Skylight.SurfaceCenter).ToKsp();
            go.transform.localRotation = e.LocalRotation;
            Mesh mesh = MeshUploader.Upload(data, go.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFactory.ForLayer(runtime.Strata.LayerAt(0.5, null));
            renderer.shadowCastingMode = ShadowCastingMode.On;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            e.Rim = go;
            e.Building = false;
        }

        private GameObject CreateCap(Entry e)
        {
            var go = new GameObject($"SubterraCap {e.Net.Id} {e.Skylight.Index}") { layer = ChunkObject.Layer };
            go.AddComponent<MeshFilter>().sharedMesh = capMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = capMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * e.Skylight.Radius * 1.1f;
            return go;
        }

        // Unit disc in the X/Z plane facing up.
        private static Mesh Disc(int segments)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int s = 0; s < segments; s++)
            {
                float a = 2 * Mathf.PI * s / segments;
                vertices.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
            }

            for (int s = 0; s < segments; s++)
            {
                int a = s + 1, b = (s + 1) % segments + 1;
                tris.AddRange(new[] { 0, b, a });
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class Entry
        {
            public TubeNetwork Net;
            public Skylight Skylight;
            public Vec3 Up, Forward, Right;
            public Quaternion LocalRotation;
            public Vector3d World;
            public GameObject Rim;
            public GameObject Cap;
            public float CapHeight;
            public bool Building;
            public bool Cancelled;
        }
    }
}
