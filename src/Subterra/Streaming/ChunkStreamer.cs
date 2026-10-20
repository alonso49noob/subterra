using System;
using System.Collections.Generic;
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
    /// colliders). Meshing and collider baking run on the worker pool; uploads use the main-thread
    /// budget. Network roots are placed in world space every frame in double precision (a float child
    /// 200 km from the body centre would be off by centimetres).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class ChunkStreamer : MonoBehaviour
    {
        private const float PlanInterval = 0.2f;
        private const int MaxPendingJobs = 24;

        private readonly Dictionary<string, Transform> roots = new Dictionary<string, Transform>();
        private readonly Dictionary<string, TubeNetwork> networks = new Dictionary<string, TubeNetwork>();
        private readonly HashSet<ChunkKey> pending = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> desiredVisual = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> desiredColliders = new HashSet<ChunkKey>();
        private ChunkCache<ChunkObject> cache;
        private CelestialBody body;
        private BodyRuntime runtime;
        private float nextPlan;
        private long frame;
        private bool visibilityDirty = true;

        public static ChunkStreamer Instance { get; private set; }

        public int ActiveChunks { get; private set; }

        public int PendingJobs => pending.Count;

        public int CachedChunks => cache?.Count ?? 0;

        public long CachedBytes => cache?.Bytes ?? 0;

        public IEnumerable<ChunkObject> Chunks => cache == null ? Enumerable.Empty<ChunkObject>() : cache.Keys.Select(k => cache.TryGet(k, out ChunkObject c) ? c : null).Where(c => c != null);

        /// <summary>
        /// True when every LOD0 chunk with cave surface within <paramref name="radius"/> of the world
        /// point has its collider (or the point is far from all networks).
        /// </summary>
        public bool CollidersReady(Vector3d worldPos, float radius)
        {
            if (runtime == null)
            {
                return true;
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
        }

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

                PlaceRoots();
                if (Time.realtimeSinceStartup >= nextPlan)
                {
                    nextPlan = Time.realtimeSinceStartup + PlanInterval;
                    Plan();
                }

                Issue();
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (runtime != null)
            {
                using (SubterraClock.Measure("ChunkStreamer.Roots"))
                {
                    PlaceRoots();
                }
            }
        }

        private void SwitchBody(CelestialBody current)
        {
            if (current == body)
            {
                return;
            }

            OnDestroy();
            Instance = this;
            roots.Clear();
            networks.Clear();
            pending.Clear();
            desiredVisual.Clear();
            desiredColliders.Clear();
            body = current;
            runtime = BodyRegistry.Instance.TryGet(current, out BodyRuntime r) ? r : null;
        }

        private void PlaceRoots()
        {
            Quaternion rotation = body.pqsController.transform.rotation;
            foreach (var pair in roots)
            {
                Transform root = pair.Value;
                Vector3 position = Frames.ToWorld(body, networks[pair.Key].Anchor);
                if ((root.position - position).sqrMagnitude > 1e-8f || root.rotation != rotation)
                {
                    root.SetPositionAndRotation(position, rotation);
                }
            }
        }

        private void Plan()
        {
            visibilityDirty = true;
            desiredVisual.Clear();
            desiredColliders.Clear();
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
                ChunkPlanner.Desired(net.Id, net.Bounds, net.ToLocal(camera), net.Sdf.MayHaveSurface, desiredVisual);
                ChunkPlanner.Colliders(net.Id, vessels.Select(net.ToLocal), settings.ColliderRadius, net.Sdf.MayHaveSurface, desiredColliders);
            }
        }

        private void EnsureRoot(TubeNetwork net)
        {
            if (roots.ContainsKey(net.Id))
            {
                return;
            }

            var go = new GameObject("SubterraNet " + net.Id) { layer = ChunkObject.Layer };
            roots[net.Id] = go.transform;
            networks[net.Id] = net;
            go.transform.SetPositionAndRotation(Frames.ToWorld(body, net.Anchor), body.pqsController.transform.rotation);
        }

        // Starts meshing for wanted chunks that are neither cached nor in progress (nearest first).
        private void Issue()
        {
            if (pending.Count >= MaxPendingJobs)
            {
                return;
            }

            Vec3d camera = Frames.ToBody(body, FlightCamera.fetch.mainCamera.transform.position);
            IEnumerable<ChunkKey> wanted = desiredColliders.Concat(desiredVisual)
                .Where(k => !pending.Contains(k) && !cache.TryGet(k, out _))
                .OrderBy(k => k.Lod)
                .ThenBy(k => k.Bounds.DistanceTo(networks[k.NetworkId].ToLocal(camera)))
                .Take(MaxPendingJobs - pending.Count)
                .ToList();
            foreach (ChunkKey key in wanted)
            {
                pending.Add(key);
                TubeNetwork net = networks[key.NetworkId];
                CelestialBody jobBody = body;
                IReadOnlyList<ScatterSpec> scatterSpecs = runtime.Spec.Scatters;
                WorkerPool.Shared.Enqueue(() =>
                {
                    MeshData data = MeshClip.DropAboveSurface(SurfaceNets.Build(net.Sdf.ForRegion(key.Bounds), key.Origin, key.CellSize, skirts: true), net);
                    List<ScatterMesh> scatters = ScatterBuilder.Build(data, key, net, scatterSpecs);
                    if (!data.IsEmpty && MaterialFactory.Mode == RockShaderMode.BumpedSpecularBoxUv)
                    {
                        data = UvProjector.Project(data, MaterialFactory.MetersPerUv);
                    }

                    WorkerPool.Shared.RunOnMain(() => Uploaded(jobBody, net, key, data, scatters));
                });
            }
        }

        private void Uploaded(CelestialBody jobBody, TubeNetwork net, ChunkKey key, MeshData data, List<ScatterMesh> scatters)
        {
            if (jobBody != body || !roots.TryGetValue(net.Id, out Transform root) || root == null)
            {
                return;
            }

            pending.Remove(key);
            visibilityDirty = true;
            Mesh mesh = data.IsEmpty ? null : MeshUploader.Upload(data, key.ToString());
            long bytes = data.ByteSize;
            var chunk = new ChunkObject(key, mesh, MaterialFor(net, key), root, bytes);
            foreach (ScatterMesh scatter in scatters)
            {
                chunk.AddScatter(scatter, PropLibrary.MaterialFor(scatter.Model));
                bytes += scatter.Visual.ByteSize + (scatter.Collider?.ByteSize ?? 0);
            }

            cache.Put(key, chunk, bytes);
        }

        // Visibility (keeping replaced chunks until their replacements are in), colliders, eviction.
        private void Apply()
        {
            bool updateVisibility = visibilityDirty;
            visibilityDirty = false;
            int active = 0;
            var keep = new HashSet<ChunkKey>(desiredVisual);
            keep.UnionWith(desiredColliders);
            foreach (ChunkKey key in cache.Keys.ToList())
            {
                cache.TryGet(key, out ChunkObject chunk);
                if (updateVisibility)
                {
                    chunk.Visible = desiredVisual.Contains(key) || StillCovering(key);
                }

                if (chunk.Visible)
                {
                    keep.Add(key);
                    cache.Touch(key, frame);
                    active++;
                }

                if (desiredColliders.Contains(key))
                {
                    cache.Touch(key, frame);
                    if (!chunk.ColliderReady && !chunk.BakeRequested && chunk.Mesh != null)
                    {
                        chunk.BakeRequested = true;
                        int meshId = chunk.Mesh.GetInstanceID();
                        WorkerPool.Shared.Enqueue(() =>
                        {
                            Physics.BakeMesh(meshId, false);
                            WorkerPool.Shared.RunOnMain(() =>
                            {
                                if (chunk.GameObject != null)
                                {
                                    chunk.EnableCollider();
                                }
                            });
                        });
                    }
                    else if (chunk.Collider != null && !chunk.Collider.enabled && chunk.BakeRequested)
                    {
                        chunk.EnableCollider();
                    }
                }
                else
                {
                    chunk.DisableCollider();
                }
            }

            ActiveChunks = active;
            foreach (var evicted in cache.EvictOver(keep))
            {
                evicted.Value.Destroy();
            }
        }

        // An unwanted chunk stays visible while any wanted chunk overlapping it is not uploaded yet.
        private bool StillCovering(ChunkKey key)
        {
            Aabb box = key.Bounds;
            foreach (ChunkKey wanted in desiredVisual)
            {
                if (wanted.NetworkId != key.NetworkId || !Overlap(wanted.Bounds, box))
                {
                    continue;
                }

                if (!cache.TryGet(wanted, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Overlap(Aabb a, Aabb b) =>
            a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;

        private Material MaterialFor(TubeNetwork net, ChunkKey key)
        {
            Vec3 centre = key.Bounds.Center;
            Vec3d bodyPos = net.ToBody(centre);
            BodyFrameLatLon(bodyPos, out double lat, out double lon);
            double depth = net.SurfaceRadiusNear(centre) - bodyPos.Length;
            LayerSpec layer = runtime.Strata.LayerAt(Math.Max(0, depth), KspSampler.Biome(body, lat, lon));
            return MaterialFactory.ForLayer(layer);
        }

        private static void BodyFrameLatLon(Vec3d bodyPos, out double lat, out double lon) =>
            Core.Geo.BodyFrame.ToLatLon(bodyPos, out lat, out lon);
    }
}
