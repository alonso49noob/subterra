using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Loads cave chunks around the camera (visual, octree LODs) and around loaded vessels (LOD0
    /// colliders). Meshing runs on the worker pool; the main thread only uploads, in small steps
    /// within its budget. Collider meshes (without skirts, props included) are baked on the pool
    /// before they are assigned. Network roots are children of the body's PQS, so cave colliders move
    /// with the terrain in step with physics.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class ChunkStreamer : MonoBehaviour
    {
        private const float PlanInterval = 0.2f;
        private const int MaxPendingJobs = 24;

        private readonly Dictionary<string, Transform> roots = new Dictionary<string, Transform>();
        private readonly Dictionary<string, TubeNetwork> networks = new Dictionary<string, TubeNetwork>();
        private readonly HashSet<ChunkKey> pending = new HashSet<ChunkKey>();
        // Cached chunks whose network changed under them (a dug tunnel): shown until their new mesh is ready.
        private readonly HashSet<ChunkKey> stale = new HashSet<ChunkKey>();
        private readonly Dictionary<string, int> revisions = new Dictionary<string, int>();
        private readonly HashSet<ChunkKey> desiredVisual = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> desiredColliders = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> missingVisual = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> collidersOn = new HashSet<ChunkKey>();
        private readonly List<ChunkKey> wanted = new List<ChunkKey>();
        private readonly ConcurrentQueue<Action> fromWorkers = new ConcurrentQueue<Action>();
        private readonly Queue<Action> steps = new Queue<Action>();
        private ChunkCache<ChunkObject> cache;
        private CelestialBody body;
        private BodyRuntime runtime;
        private float nextPlan;
        private long frame;
        private int generation;
        private int wantedIndex;
        private bool planned;
        private bool planInFlight;
        private bool visibilityDirty;

        public static ChunkStreamer Instance { get; private set; }

        public int ActiveChunks { get; private set; }

        public int PendingJobs => pending.Count;

        public int PendingSteps => steps.Count;

        public int CachedChunks => cache?.Count ?? 0;

        public long CachedBytes => cache?.Bytes ?? 0;

        public IEnumerable<ChunkObject> Chunks
        {
            get
            {
                if (cache == null)
                {
                    yield break;
                }

                foreach (ChunkKey key in cache.Keys)
                {
                    if (cache.TryGet(key, out ChunkObject c))
                    {
                        yield return c;
                    }
                }
            }
        }

        /// <summary>
        /// True when every LOD0 chunk with cave surface within <paramref name="radius"/> of the world
        /// point is loaded with its colliders on, or the body has no caves. False while this body's
        /// chunks have not been planned yet (e.g. right after a scene load).
        /// </summary>
        public bool CollidersReady(CelestialBody vesselBody, Vector3d worldPos, float radius)
        {
            if (!BodyRegistry.Instance.TryGet(vesselBody, out BodyRuntime bodyRuntime))
            {
                return true;
            }

            if (vesselBody != body || runtime != bodyRuntime || !planned)
            {
                return false;
            }

            Vec3d bodyPos = Frames.ToBody(body, worldPos);
            var keys = new HashSet<ChunkKey>();
            foreach (TubeNetwork net in runtime.Catalog.NetworksNear(bodyPos, radius))
            {
                ChunkPlanner.Colliders(net.Id, new[] { net.ToLocal(bodyPos) }, radius, net.Sdf.MayHaveSurface, keys);
            }

            foreach (ChunkKey key in keys)
            {
                if (!cache.TryGet(key, out ChunkObject chunk) || !chunk.ColliderReady)
                {
                    return false;
                }
            }

            return true;
        }

        private void Awake()
        {
            Instance = this;
            cache = new ChunkCache<ChunkObject>(Settings.Current.MemoryCapMB * 1024L * 1024L);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Clear();
        }

        // Drops everything (scene end or another body); late worker results see a new generation.
        private void Clear()
        {
            generation++;
            planInFlight = false;
            foreach (ChunkKey key in cache.Keys.ToList())
            {
                if (cache.Remove(key, out ChunkObject chunk))
                {
                    chunk.Destroy();
                }
            }

            foreach (Transform root in roots.Values)
            {
                if (root != null)
                {
                    Destroy(root.gameObject);
                }
            }

            roots.Clear();
            networks.Clear();
            pending.Clear();
            desiredVisual.Clear();
            desiredColliders.Clear();
            missingVisual.Clear();
            collidersOn.Clear();
            stale.Clear();
            revisions.Clear();
            wanted.Clear();
            steps.Clear();
            while (fromWorkers.TryDequeue(out _))
            {
            }

            planned = false;
        }

        /// <summary>
        /// A network changed shape inside <paramref name="localBox"/> (a tunnel was dug or joined into it): the
        /// cached chunks there are meshed again, staying on screen (and solid) until their new mesh is ready.
        /// </summary>
        public void NetworkChanged(TubeNetwork net, Aabb localBox)
        {
            revisions[net.Id] = RevisionOf(net.Id) + 1;
            if (networks.ContainsKey(net.Id))
            {
                networks[net.Id] = net;
            }

            Aabb box = localBox.Expand(4f);
            foreach (ChunkKey key in cache.Keys)
            {
                if (key.NetworkId == net.Id && key.Bounds.Intersects(box))
                {
                    stale.Add(key);
                }
            }

            nextPlan = 0;
        }

        /// <summary>A standalone dug tunnel became part of another network: its own chunks and root go.</summary>
        public void NetworkRemoved(string id)
        {
            revisions[id] = RevisionOf(id) + 1;
            foreach (ChunkKey key in cache.Keys.Where(k => k.NetworkId == id).ToList())
            {
                if (cache.Remove(key, out ChunkObject chunk))
                {
                    collidersOn.Remove(key);
                    stale.Remove(key);
                    chunk.Destroy();
                }
            }

            if (roots.TryGetValue(id, out Transform root))
            {
                if (root != null)
                {
                    Destroy(root.gameObject);
                }

                roots.Remove(id);
            }

            networks.Remove(id);
            nextPlan = 0;
            visibilityDirty = true;
        }

        private int RevisionOf(string id) => revisions.TryGetValue(id, out int revision) ? revision : 0;

        private void Update()
        {
            if (!FlightGlobals.ready || FlightCamera.fetch == null)
            {
                return;
            }

            using (SubterraClock.Measure("ChunkStreamer"))
            {
                frame++;
                SwitchBody(FlightGlobals.currentMainBody);
                if (runtime == null)
                {
                    return;
                }

                if (!planInFlight && Time.realtimeSinceStartup >= nextPlan)
                {
                    nextPlan = Time.realtimeSinceStartup + PlanInterval;
                    using (SubterraClock.Measure("ChunkStreamer.Plan"))
                    {
                        Plan();
                    }
                }

                // Named phases (nested scopes: they show in the breakdown, not twice in the frame total).
                using (SubterraClock.Measure("ChunkStreamer.Results"))
                {
                    while (fromWorkers.TryDequeue(out Action action))
                    {
                        action();
                    }
                }

                using (SubterraClock.Measure("ChunkStreamer.Issue"))
                {
                    Issue();
                }

                using (SubterraClock.Measure("ChunkStreamer.Steps"))
                {
                    RunSteps(Settings.Current.MainThreadBudgetMs * 0.5f);
                }

                using (SubterraClock.Measure("ChunkStreamer.Colliders"))
                {
                    Colliders(Settings.Current.MainThreadBudgetMs * 0.25f);
                }

                if (visibilityDirty)
                {
                    using (SubterraClock.Measure("ChunkStreamer.Visibility"))
                    {
                        UpdateVisibility();
                    }
                }

                if (cache.OverBudget)
                {
                    using (SubterraClock.Measure("ChunkStreamer.Evict"))
                    {
                        Evict();
                    }
                }
            }
        }

        private void SwitchBody(CelestialBody current)
        {
            if (current == body)
            {
                return;
            }

            Clear();
            body = current;
            runtime = BodyRegistry.Instance.TryGet(current, out BodyRuntime r) && current.pqsController != null ? r : null;
        }

        // Inputs are gathered on the main thread; the octree walk and the SDF tests run on the pool (they cost
        // milliseconds with many networks in range) and ApplyPlan takes the result on the main thread.
        private void Plan()
        {
            Settings settings = Settings.Current;
            Vec3d camera = Frames.ToBody(body, FlightCamera.fetch.mainCamera.transform.position);
            var vessels = new List<Vec3d>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v != null && v.mainBody == body)
                {
                    vessels.Add(Frames.ToBody(body, v.GetWorldPos3D()));
                }
            }

            var used = new HashSet<TubeNetwork>(runtime.Catalog.NetworksNear(camera, settings.VisualRange));
            foreach (Vec3d v in vessels)
            {
                foreach (TubeNetwork net in runtime.Catalog.NetworksNear(v, settings.ColliderRadius))
                {
                    used.Add(net);
                }
            }

            foreach (TubeNetwork net in used)
            {
                EnsureRoot(net);
            }

            List<TubeNetwork> nets = used.ToList();
            float colliderRadius = settings.ColliderRadius;
            int planGeneration = generation;
            planInFlight = true;
            WorkerPool.Shared.Enqueue(() =>
            {
                var visual = new HashSet<ChunkKey>();
                var colliders = new HashSet<ChunkKey>();
                var distance = new Dictionary<ChunkKey, float>();
                var ordered = new List<ChunkKey>();
                Exception error = null;
                try
                {
                    var cameraLocal = new Dictionary<string, Vec3>();
                    foreach (TubeNetwork net in nets)
                    {
                        Vec3 local = net.ToLocal(camera);
                        cameraLocal[net.Id] = local;
                        ChunkPlanner.Desired(net.Id, net.Bounds, local, net.Sdf.MayHaveSurface, visual);
                        ChunkPlanner.Colliders(net.Id, vessels.Select(net.ToLocal), colliderRadius, net.Sdf.MayHaveSurface, colliders);
                    }

                    foreach (ChunkKey key in colliders.Concat(visual))
                    {
                        if (!distance.ContainsKey(key))
                        {
                            distance[key] = key.Bounds.DistanceTo(cameraLocal[key.NetworkId]);
                        }
                    }

                    // Colliders first, then by LOD and distance: the order Issue meshes them in.
                    ordered.AddRange(distance.Keys);
                    ordered.Sort((a, b) =>
                    {
                        int byCollider = colliders.Contains(b).CompareTo(colliders.Contains(a));
                        if (byCollider != 0)
                        {
                            return byCollider;
                        }

                        int byLod = a.Lod.CompareTo(b.Lod);
                        return byLod != 0 ? byLod : distance[a].CompareTo(distance[b]);
                    });
                }
                catch (Exception e)
                {
                    error = e;
                }

                fromWorkers.Enqueue(() => ApplyPlan(planGeneration, visual, colliders, ordered, error));
            });
        }

        private void ApplyPlan(int planGeneration, HashSet<ChunkKey> visual, HashSet<ChunkKey> colliders, List<ChunkKey> ordered, Exception error)
        {
            if (planGeneration != generation)
            {
                return;
            }

            planInFlight = false;
            if (error != null)
            {
                Log.Error($"Chunk planning failed: {error}");
                return;
            }

            desiredVisual.Clear();
            desiredVisual.UnionWith(visual);
            desiredColliders.Clear();
            desiredColliders.UnionWith(colliders);

            // What to mesh, in the order sorted on the pool; Issue walks this list.
            missingVisual.Clear();
            wanted.Clear();
            foreach (ChunkKey key in ordered)
            {
                bool cached = cache.TryGet(key, out _);
                if (cached)
                {
                    cache.Touch(key, frame);
                }
                else if (desiredVisual.Contains(key))
                {
                    missingVisual.Add(key);
                }

                if ((!cached || stale.Contains(key)) && !pending.Contains(key))
                {
                    wanted.Add(key);
                }
            }

            wantedIndex = 0;
            visibilityDirty = true;
            planned = true;
        }

        private void EnsureRoot(TubeNetwork net)
        {
            // Always the catalog's current version (a network grows when a tunnel joins it).
            networks[net.Id] = net;
            if (roots.ContainsKey(net.Id))
            {
                return;
            }

            var go = new GameObject("SubterraNet " + net.Id) { layer = ChunkObject.Layer };
            go.transform.SetParent(body.pqsController.transform, false);
            go.transform.localPosition = net.Anchor.ToKsp();
            go.transform.localRotation = Quaternion.identity;
            roots[net.Id] = go.transform;
        }

        // Starts meshing jobs for wanted chunks, in plan order, up to the job limit.
        private void Issue()
        {
            int jobGeneration = generation;
            while (pending.Count < MaxPendingJobs && wantedIndex < wanted.Count)
            {
                ChunkKey key = wanted[wantedIndex++];
                if (pending.Contains(key) || cache.TryGet(key, out _) && !stale.Contains(key))
                {
                    continue;
                }

                if (!networks.TryGetValue(key.NetworkId, out TubeNetwork net))
                {
                    continue;
                }

                pending.Add(key);
                int revision = RevisionOf(net.Id);
                IReadOnlyList<ScatterSpec> scatterSpecs = runtime.Spec.Scatters;
                WorkerPool.Shared.Enqueue(() =>
                {
                    try
                    {
                        MeshData data = MeshClip.DropAboveSurface(SurfaceNets.Build(net.Sdf.ForRegion(key.Bounds), key.Origin, key.CellSize, skirts: true), net);
                        MeshData collider = key.Lod == 0 && !data.IsEmpty ? WithoutSkirts(data) : null;
                        List<ScatterMesh> scatters = ScatterBuilder.Build(data, key, net, scatterSpecs);
                        if (!data.IsEmpty && MaterialFactory.Mode == RockShaderMode.BumpedSpecularBoxUv)
                        {
                            data = UvProjector.Project(data, MaterialFactory.MetersPerUv);
                        }

                        // Unity-ready arrays are built here too: the main thread only hands them over.
                        PreparedMesh visual = data.IsEmpty ? null : MeshUploader.Prepare(data);
                        var scatterVisuals = new List<PreparedMesh>(scatters.Count);
                        foreach (ScatterMesh scatter in scatters)
                        {
                            scatterVisuals.Add(MeshUploader.Prepare(scatter.Visual));
                        }

                        fromWorkers.Enqueue(() => Meshed(jobGeneration, revision, net, key, data, visual, collider, scatters, scatterVisuals));
                    }
                    catch (Exception e)
                    {
                        fromWorkers.Enqueue(() =>
                        {
                            if (jobGeneration == generation)
                            {
                                pending.Remove(key);
                            }

                            Log.Error($"Meshing {key} failed: {e}");
                        });
                    }
                });
            }
        }

        // A finished job becomes upload steps: the chunk mesh, then one step per scatter, then the cache entry.
        private void Meshed(int jobGeneration, int revision, TubeNetwork net, ChunkKey key, MeshData data, PreparedMesh visual, MeshData colliderData, List<ScatterMesh> scatters, List<PreparedMesh> scatterVisuals)
        {
            if (jobGeneration != generation)
            {
                return;
            }

            // Meshed from a version of the network that has since grown: mesh it again.
            if (revision != RevisionOf(net.Id))
            {
                pending.Remove(key);
                nextPlan = 0;
                return;
            }

            var chunk = new ChunkObject(key, data.ByteSize + (colliderData?.ByteSize ?? 0)) { ColliderData = colliderData };
            if (data.IsEmpty)
            {
                Finish(chunk);
                return;
            }

            steps.Enqueue(() =>
            {
                if (!roots.TryGetValue(net.Id, out Transform root) || root == null)
                {
                    pending.Remove(key);
                    return;
                }

                using (SubterraClock.Measure("ChunkStreamer.Step.Mesh"))
                {
                    chunk.Create(MeshUploader.Upload(visual, key.ToString(), readable: false), MaterialFor(net, key), root);
                }
            });
            for (int i = 0; i < scatters.Count; i++)
            {
                ScatterMesh scatter = scatters[i];
                PreparedMesh scatterVisual = scatterVisuals[i];
                steps.Enqueue(() =>
                {
                    if (chunk.IsEmpty)
                    {
                        return;
                    }

                    using (SubterraClock.Measure("ChunkStreamer.Step.Scatter"))
                    {
                        chunk.AddScatter(scatter, MeshUploader.Upload(scatterVisual, chunk.Key + " " + scatter.Model, readable: false), PropLibrary.MaterialFor(scatter.Model));
                    }

                    chunk.Bytes += scatter.Visual.ByteSize + (scatter.Collider?.ByteSize ?? 0);
                });
            }

            steps.Enqueue(() =>
            {
                if (chunk.IsEmpty)
                {
                    pending.Remove(key);
                    return;
                }

                using (SubterraClock.Measure("ChunkStreamer.Step.Finish"))
                {
                    Finish(chunk);
                }
            });
        }

        private void Finish(ChunkObject chunk)
        {
            pending.Remove(chunk.Key);
            stale.Remove(chunk.Key);
            if (cache.TryGet(chunk.Key, out ChunkObject old) && old != chunk)
            {
                cache.Remove(chunk.Key, out _);
                collidersOn.Remove(chunk.Key);
                old.Destroy();
            }

            cache.Put(chunk.Key, chunk, chunk.Bytes);
            cache.Touch(chunk.Key, frame);
            missingVisual.Remove(chunk.Key);
            visibilityDirty = true;
        }

        private void RunSteps(float budgetMs)
        {
            var watch = Stopwatch.StartNew();
            while (steps.Count > 0 && watch.Elapsed.TotalMilliseconds < budgetMs)
            {
                Action step = steps.Dequeue();
                try
                {
                    step();
                }
                catch (Exception e)
                {
                    Log.Error($"Chunk upload step failed: {e}");
                }
            }
        }

        // LOD0 chunks near vessels get colliders: collider meshes uploaded, baked on the pool, then assigned.
        private void Colliders(float budgetMs)
        {
            // Uploads and collider swaps cost about a millisecond each: a slice per frame, at least one.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool didAny = false;
            foreach (ChunkKey key in desiredColliders)
            {
                if (didAny && watch.Elapsed.TotalMilliseconds >= budgetMs)
                {
                    break;
                }

                if (!cache.TryGet(key, out ChunkObject chunk) || chunk.IsEmpty || chunk.BakeInFlight)
                {
                    continue;
                }

                if (chunk.Baked)
                {
                    if (collidersOn.Add(key))
                    {
                        chunk.EnableCollider();
                        didAny = true;
                    }

                    continue;
                }

                if (chunk.ColliderData != null)
                {
                    StartBake(chunk);
                    didAny = true;
                }
            }

            if (collidersOn.Count > 0)
            {
                List<ChunkKey> off = null;
                foreach (ChunkKey key in collidersOn)
                {
                    if (!desiredColliders.Contains(key))
                    {
                        (off ?? (off = new List<ChunkKey>())).Add(key);
                    }
                }

                if (off != null)
                {
                    foreach (ChunkKey key in off)
                    {
                        collidersOn.Remove(key);
                        if (cache.TryGet(key, out ChunkObject chunk))
                        {
                            chunk.DisableCollider();
                        }
                    }
                }
            }
        }

        private void StartBake(ChunkObject chunk)
        {
            if (chunk.ColliderData == null)
            {
                return;
            }

            chunk.ColliderMesh = MeshUploader.Upload(chunk.ColliderData, chunk.Key + " collider", readable: true);
            var ids = new List<int> { chunk.ColliderMesh.GetInstanceID() };
            foreach (ChunkObject.ScatterPart part in chunk.Scatters)
            {
                if (part.Data.Collider != null && !part.Data.Collider.IsEmpty)
                {
                    part.ColliderMesh = MeshUploader.Upload(part.Data.Collider, chunk.Key + " " + part.Data.Model + " collider", readable: true);
                    ids.Add(part.ColliderMesh.GetInstanceID());
                }
            }

            chunk.BakeInFlight = true;
            int bakeGeneration = generation;
            WorkerPool.Shared.Enqueue(() =>
            {
                Exception error = null;
                try
                {
                    foreach (int id in ids)
                    {
                        Physics.BakeMesh(id, false);
                    }
                }
                catch (Exception e)
                {
                    error = e;
                }

                fromWorkers.Enqueue(() =>
                {
                    chunk.BakeInFlight = false;
                    if (bakeGeneration != generation)
                    {
                        chunk.Destroy();
                        return;
                    }

                    if (error != null)
                    {
                        Log.Error($"Baking {chunk.Key} failed: {error.Message}");
                    }

                    // Assigning an unbaked mesh still works, it is just cooked here instead.
                    chunk.Baked = true;
                    chunk.ColliderData = null;
                });
            });
        }

        // Wanted chunks are shown; an unwanted one stays while a wanted chunk over it is still missing.
        private void UpdateVisibility()
        {
            visibilityDirty = false;
            int active = 0;
            foreach (ChunkKey key in cache.Keys)
            {
                cache.TryGet(key, out ChunkObject chunk);
                bool visible = desiredVisual.Contains(key) || StillCovering(key);
                chunk.SetVisible(visible);
                if (visible && !chunk.IsEmpty)
                {
                    active++;
                }
            }

            ActiveChunks = active;
        }

        private bool StillCovering(ChunkKey key)
        {
            if (missingVisual.Count == 0)
            {
                return false;
            }

            Aabb box = key.Bounds;
            foreach (ChunkKey missing in missingVisual)
            {
                if (missing.NetworkId == key.NetworkId && Overlap(missing.Bounds, box))
                {
                    return true;
                }
            }

            return false;
        }

        private void Evict()
        {
            var keep = new HashSet<ChunkKey>(desiredVisual);
            keep.UnionWith(desiredColliders);
            foreach (ChunkKey key in cache.Keys)
            {
                if (cache.TryGet(key, out ChunkObject chunk) && (chunk.Visible || chunk.BakeInFlight))
                {
                    keep.Add(key);
                }
            }

            foreach (var evicted in cache.EvictOver(keep))
            {
                collidersOn.Remove(evicted.Key);
                evicted.Value.Destroy();
            }
        }

        private static bool Overlap(Aabb a, Aabb b) =>
            a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;

        private static MeshData WithoutSkirts(MeshData data)
        {
            var result = new MeshData();
            result.Vertices.AddRange(data.Vertices);
            result.Normals.AddRange(data.Normals);
            for (int i = 0; i < data.SkirtTriangleStart; i++)
            {
                result.Triangles.Add(data.Triangles[i]);
            }

            result.SkirtTriangleStart = result.Triangles.Count;
            return result;
        }

        private Material MaterialFor(TubeNetwork net, ChunkKey key)
        {
            Vec3 centre = key.Bounds.Center;
            Vec3d bodyPos = net.ToBody(centre);
            Core.Geo.BodyFrame.ToLatLon(bodyPos, out double lat, out double lon);
            double depth = net.SurfaceRadiusNear(centre) - bodyPos.Length;
            LayerSpec layer = runtime.Strata.LayerAt(Math.Max(0, depth), KspSampler.Biome(body, lat, lon));
            return MaterialFactory.ForLayer(layer, body);
        }
    }
}
