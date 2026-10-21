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

        // A dug mouth's disc is opened triangle by triangle over the tunnel: rings every FineStep m (and this many
        // segments) over the opening give a clean edge; Rings coarser ones from there to the outer edge.
        private const int SmoothSegments = 160;
        private const float FineStep = 0.75f;

        // Ring radii from inner to outer: every FineStep m up to fineTo, then Rings - 1 even steps.
        private static float[] RingRadii(float inner, float outer, float fineTo)
        {
            var radii = new List<float>();
            float r = inner;
            for (; r < fineTo && r < outer; r += FineStep)
            {
                radii.Add(r);
            }

            float from = radii.Count > 0 ? r : inner;
            for (int i = 0; i < Rings; i++)
            {
                if (radii.Count > 0 && i == 0)
                {
                    continue;
                }

                radii.Add(from + (outer - from) * i / (Rings - 1));
            }

            return radii.ToArray();
        }
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
                    EntryFor(net, s);
                }
            }
        }

        // A tunnel mouth's rim opened over the tunnel: cut along the line where the tunnel meets the ground (the
        // triangles crossing it are clipped there, marching-triangles style), so the edge is smooth, and a short wall
        // hangs from that edge. The rest of the cut ground stays closed (no sky through it).
        private static MeshData OpenOverTunnel(MeshData rim, Entry e)
        {
            var d = new float[rim.Vertices.Count];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = UnderRim(rim.Vertices[i], e);
            }

            var open = new MeshData();
            open.Vertices.AddRange(rim.Vertices);
            open.Normals.AddRange(rim.Normals);
            open.Uvs.AddRange(rim.Uvs);
            var cuts = new Dictionary<(int, int), int>();
            var edge = new List<(Vec3 a, Vec3 b, Vec3 hole)>();
            var kept = new List<int>(4);
            for (int i = 0; i + 2 < rim.Triangles.Count; i += 3)
            {
                int a = rim.Triangles[i], b = rim.Triangles[i + 1], c = rim.Triangles[i + 2];
                int over = (d[a] < 0 ? 1 : 0) + (d[b] < 0 ? 1 : 0) + (d[c] < 0 ? 1 : 0);
                if (over == 0)
                {
                    open.Triangles.AddRange(new[] { a, b, c });
                    continue;
                }

                if (over == 3)
                {
                    continue;
                }

                // The part of the triangle off the tunnel, in the same winding, and the two points where it is cut.
                kept.Clear();
                var cutPoints = new List<int>(2);
                Vec3 hole = new Vec3(0, 0, 0);
                int[] corners = { a, b, c };
                for (int k = 0; k < 3; k++)
                {
                    int p = corners[k], q = corners[(k + 1) % 3];
                    if (d[p] >= 0)
                    {
                        kept.Add(p);
                    }
                    else
                    {
                        hole += rim.Vertices[p] / over;
                    }

                    if ((d[p] < 0) != (d[q] < 0))
                    {
                        int cut = Cut(open, d, cuts, p, q);
                        kept.Add(cut);
                        cutPoints.Add(cut);
                    }
                }

                for (int k = 1; k + 1 < kept.Count; k++)
                {
                    open.Triangles.AddRange(new[] { kept[0], kept[k], kept[k + 1] });
                }

                edge.Add((open.Vertices[cutPoints[0]], open.Vertices[cutPoints[1]], hole));
            }

            open.SkirtTriangleStart = open.Triangles.Count;
            foreach ((Vec3 p, Vec3 q, Vec3 hole) in edge)
            {
                Curtain(open, p, q, hole);
            }

            return open;
        }

        /// <summary>How far under the rim the tunnel must reach for the ground to open (its walls are clipped just under the ground).</summary>
        private const float OpenDepth = 0.15f;

        // Signed distance to the void from the point OpenDepth under a rim vertex (< 0: over the tunnel).
        private static float UnderRim(Vec3 v, Entry e) =>
            e.Net.Sdf.Distance(e.Skylight.SurfaceCenter + e.Right * v.X + e.Forward * v.Z + e.Up * (v.Y - OpenDepth));

        // The vertex where the edge p–q crosses the tunnel's outline (shared by the two triangles on that edge).
        private static int Cut(MeshData mesh, float[] d, Dictionary<(int, int), int> cuts, int p, int q)
        {
            var key = p < q ? (p, q) : (q, p);
            if (cuts.TryGetValue(key, out int index))
            {
                return index;
            }

            float t = d[p] / (d[p] - d[q]);
            index = mesh.Vertices.Count;
            mesh.Vertices.Add(Vec3.Lerp(mesh.Vertices[p], mesh.Vertices[q], t));
            mesh.Normals.Add(Vec3.Lerp(mesh.Normals[p], mesh.Normals[q], t).Normalized);
            mesh.Uvs.Add(new Vec2(mesh.Uvs[p].X + (mesh.Uvs[q].X - mesh.Uvs[p].X) * t, mesh.Uvs[p].Y + (mesh.Uvs[q].Y - mesh.Uvs[p].Y) * t));
            cuts[key] = index;
            return index;
        }

        /// <summary>Depth (m) of the wall hanging from a dug mouth's edge.</summary>
        private const float CurtainDepth = 2.5f;

        // A wall under the edge p–q, facing the opening (towards hole): looking in at a slant, the eye meets ground there
        // instead of the back of the tunnel's roof under the rim (not drawn: sky through it). Never solid.
        private static void Curtain(MeshData mesh, Vec3 p, Vec3 q, Vec3 hole)
        {
            Vec3 along = q - p;
            Vec3 toward = hole - p;
            toward -= along * (Vec3.Dot(toward, along) / Mathf.Max(1e-6f, along.LengthSquared));
            Vec3 normal = new Vec3(toward.X, 0, toward.Z).Normalized;
            Vec3 drop = new Vec3(0, -CurtainDepth, 0);
            int start = mesh.Vertices.Count;
            foreach (Vec3 v in new[] { p, q, q + drop, p + drop })
            {
                mesh.Vertices.Add(v);
                mesh.Normals.Add(normal);
                mesh.Uvs.Add(new Vec2((v.X + v.Z) / 4f, v.Y / 4f));
            }

            // Front faces have Cross(v1 - v0, v2 - v0) along their normal.
            bool flip = Vec3.Dot(Vec3.Cross(q - p, drop), normal) < 0;
            mesh.Triangles.AddRange(flip
                ? new[] { start, start + 2, start + 1, start, start + 3, start + 2 }
                : new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }



        // A tunnel mouth's rim is driven on: its heights follow the terrain as built (its collider, flat between
        // vertices tens of metres apart), not the analytic height, which can be centimetres to decimetres off and
        // would push wheels up. Points over the cut (no terrain left there) keep the analytic height.
        // Returns the mean colour the terrain has there (its vertex colours, which tint its textures), or null if none was hit.
        private Color? FitToTerrainMesh(List<Vec3> planar, float[] heights, Vec3d centre, Vec3d up, Entry e)
        {
            float?[] before = e.GroundHeights != null && e.GroundHeights.Length == planar.Count ? e.GroundHeights : null;
            var found = new float?[planar.Count];
            var meshes = new Dictionary<Mesh, (Color[] colours, int[] triangles)>();
            Color sum = Color.clear;
            int count = 0;
            Vector3 world0 = Frames.ToWorld(body, centre);
            Vector3 down = -(Vector3)((Vector3d)world0 - body.position).normalized;
            for (int i = 0; i < planar.Count; i++)
            {
                Vector3 world = Frames.ToWorld(body, centre + Vec3d.FromVec3(planar[i]) + up * heights[i]);
                float best = float.MaxValue;
                RaycastHit? nearest = null;
                foreach (RaycastHit hit in Physics.RaycastAll(world - down * 20f, down, 40f, 1 << ChunkObject.Layer, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance < best && hit.collider.GetComponent<PQ>() != null)
                    {
                        best = hit.distance;
                        nearest = hit;
                    }
                }

                if (nearest is RaycastHit h)
                {
                    heights[i] = (float)Vec3d.Dot(Frames.ToBody(body, h.point) - centre, up);
                    found[i] = heights[i];
                    if (h.collider is MeshCollider mc && mc.sharedMesh != null && mc.sharedMesh.isReadable && h.triangleIndex >= 0)
                    {
                        if (!meshes.TryGetValue(mc.sharedMesh, out var m))
                        {
                            m = (mc.sharedMesh.colors, mc.sharedMesh.triangles);
                            meshes[mc.sharedMesh] = m;
                        }

                        int t = h.triangleIndex * 3;
                        if (m.colours.Length > 0 && t + 2 < m.triangles.Length)
                        {
                            Vector3 b = h.barycentricCoordinate;
                            sum += m.colours[m.triangles[t]] * b.x + m.colours[m.triangles[t + 1]] * b.y + m.colours[m.triangles[t + 2]] * b.z;
                            count++;
                        }
                    }
                }
            }

            // Where the ground is cut now, the height it had (same layout: the rim keeps its surface under the wheels).
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null && before?[i] is float kept)
                {
                    heights[i] = kept;
                    found[i] = kept;
                }
            }

            e.GroundHeights = found;
            if (count > 0)
            {
                e.GroundColour = sum / count;
            }

            return e.GroundColour;
        }

        // The solid part of a dug mouth's rim: all but the curtains (a vehicle drives out over them). Its heights follow
        // the terrain's collider where there is one, so the two do not fight under wheels.
        private static MeshData SolidPart(MeshData data)
        {
            var solid = new MeshData();
            solid.Vertices.AddRange(data.Vertices);
            solid.Normals.AddRange(data.Normals);
            solid.Uvs.AddRange(data.Uvs);
            for (int i = 0; i < data.SkirtTriangleStart; i += 3)
            {
                solid.Triangles.Add(data.Triangles[i]);
                solid.Triangles.Add(data.Triangles[i + 1]);
                solid.Triangles.Add(data.Triangles[i + 2]);
            }

            solid.SkirtTriangleStart = solid.Triangles.Count;
            return solid;
        }

        // One entry per opening: keyed by its owner (a dug tunnel's mouth keeps its key when the tunnel joins a cave).
        private Entry EntryFor(TubeNetwork net, Skylight s)
        {
            var key = (s.Owner ?? net.Id, s.Index);
            if (!entries.TryGetValue(key, out Entry e))
            {
                Vec3 up = s.Up;
                Vec3 forward = Vec3.Cross(up, Mathf.Abs(up.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized;
                e = new Entry
                {
                    Net = net,
                    Skylight = s,
                    Up = up,
                    Forward = forward,
                    Right = Vec3.Cross(up, forward),
                    LocalRotation = Quaternion.LookRotation(forward.ToUnity(), up.ToUnity()),
                };
                entries[key] = e;
            }

            return e;
        }

        /// <summary>
        /// Steadies the landed vessels near a mouth whose ground was just cut, for half a second: their wheels go from
        /// the terrain's collider to the rim's in one frame, and the suspension takes that as a bump (a rover was thrown
        /// up at 3 m/s). Upward speed is taken off, sliding and spin damped meanwhile.
        /// </summary>
        public void Settle(Vec3d centre, float radius)
        {
            if (body == null)
            {
                return;
            }

            PQSMod_SubterraSkylight mod = SkylightInjector.ModFor(body.pqsController);
            double reach = radius + (mod != null ? mod.CutReach : 60f) + 20;
            Vector3d world = Frames.ToWorld(body, centre);
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v.mainBody == body && !v.packed && (v.CoMD - world).magnitude < reach)
                {
                    StartCoroutine(SettleVessel(v));
                }
            }
        }

        private IEnumerator SettleVessel(Vessel v)
        {
            float until = Time.time + 0.5f;
            while (Time.time < until && v != null && v.loaded && !v.packed)
            {
                yield return new WaitForFixedUpdate();
                Vector3 up = (v.CoMD - v.mainBody.position).normalized;
                foreach (Part p in v.parts)
                {
                    Rigidbody rb = p.Rigidbody;
                    if (rb == null)
                    {
                        continue;
                    }

                    // No rising, and sliding damped (a parked vehicle stays parked).
                    float rise = Vector3.Dot(rb.velocity, up);
                    Vector3 slide = rb.velocity - up * rise;
                    rb.velocity = slide * 0.8f + up * Mathf.Min(0, rise);

                    rb.angularVelocity *= 0.7f;
                }
            }
        }

        /// <summary>
        /// Builds an opening's rim (and its collider) right now, before the terrain is cut under it: a vehicle
        /// digging a tunnel mouth stands on the rim instead of falling through the cut for a few frames.
        /// </summary>
        public void BuildRimNow(BodyRuntime runtime, TubeNetwork net, Skylight s, bool rebuild = false)
        {
            if (body != runtime.Body)
            {
                Clear();
                body = runtime.Body;
            }

            Entry e = EntryFor(net, s);
            if (e.Rim != null && !rebuild)
            {
                return;
            }

            // A rebuilt rim (a tunnel grew under its trench) is cut against the network as it is now.
            e.Net = net;
            e.Skylight = s;
            if (e.Rim != null)
            {
                Mesh old = e.Rim.GetComponent<MeshFilter>()?.sharedMesh;
                Destroy(e.Rim);
                if (old != null)
                {
                    Destroy(old);
                }

                e.Rim = null;
            }

            e.Cancelled = false;
            Drain(BuildRim(runtime, e, true));
        }

        // Runs a coroutine (and the ones it yields) to the end in this frame.
        // A routine that keeps yielding (waiting for a later frame) is cut off rather than freezing the game.
        private static void Drain(IEnumerator routine)
        {
            for (int steps = 0; routine.MoveNext(); steps++)
            {
                if (steps > 100000)
                {
                    Log.Error("SkylightRenderer: an immediate rim build did not finish; gave up");
                    return;
                }

                if (routine.Current is IEnumerator inner)
                {
                    Drain(inner);
                }
            }
        }

        // immediate: sample the terrain right away (BuildRimNow) instead of in per-frame slices.
        private IEnumerator BuildRim(BodyRuntime runtime, Entry e, bool immediate = false)
        {
            e.Building = true;
            PQSMod_SubterraSkylight mod = SkylightInjector.ModFor(body.pqsController);
            // A smooth rim (tunnel mouth) is a whole disc that closes the cut ground, open only where the tunnel is.
            float inner = e.Skylight.Smooth ? 0.05f : e.Skylight.Radius;
            float outer = e.Skylight.Radius + (mod != null ? mod.CutReach : 60f) + 5f;
            var points = new List<(double lat, double lon)>();
            var planar = new List<Vec3>();
            float[] radii = RingRadii(inner, outer, e.Skylight.Smooth ? e.Skylight.Radius + 2f : inner);
            int segments = e.Skylight.Smooth ? SmoothSegments : Segments;
            foreach (float radius in radii)
            {
                for (int s = 0; s < segments; s++)
                {
                    float a = 2 * Mathf.PI * s / segments;
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
            void Store(int i, double radius)
            {
                Vec3d terrain = Core.Geo.BodyFrame.Direction(points[i].lat, points[i].lon) * radius;
                heights[i] = (float)Vec3d.Dot(terrain - centre, up);
            }

            if (immediate)
            {
                // Not KspSampler.Fill: its per-frame budget would never refill within this one frame.
                for (int i = 0; i < points.Count; i++)
                {
                    Store(i, KspSampler.SurfaceRadius(body, points[i].lat, points[i].lon));
                }
            }
            else
            {
                yield return KspSampler.Fill(body, points, (i, radius, biome) => Store(i, radius), 2f);
            }

            // Every rim follows the terrain as built where it is not cut (no terrain showing through it), and takes its colour.
            Color? ground = FitToTerrainMesh(planar, heights, centre, up, e);

            MeshData data = RimBuilder.Build(radii, segments, heights, Hash.Combine(Hash.Of(e.Net.Id), (ulong)e.Skylight.Index), e.Skylight.Smooth);
            if (e.Skylight.Smooth)
            {
                data = OpenOverTunnel(data, e);
            }
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
            renderer.sharedMaterial = ground is Color g ? MaterialFactory.ForSurfaceTinted(body, g) : MaterialFactory.ForSurface(body);
            if (ground is Color logged)
            {
                Log.Info($"{go.name}: terrain colour {logged.r:F2} {logged.g:F2} {logged.b:F2}");
            }
            renderer.shadowCastingMode = ShadowCastingMode.On;
            MeshData solid = e.Skylight.Smooth ? SolidPart(data) : data;
            if (solid.Triangles.Count > 0)
            {
                go.AddComponent<MeshCollider>().sharedMesh = solid == data ? mesh : MeshUploader.Upload(solid, go.name + " collider");
            }

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

            // The heights the last build found on the terrain (null where there was none), for a rebuild once the
            // ground under it is cut: the rim keeps the same surface under the wheels.
            public float?[] GroundHeights;

            public Color? GroundColour;
        }
    }
}
