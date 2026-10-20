using Subterra.Core.Meshing;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Subterra
{
    /// <summary>One uploaded chunk: a layer-15 GameObject under its network's root, with an optional collider.</summary>
    public sealed class ChunkObject
    {
        public const int Layer = 15;

        public ChunkObject(ChunkKey key, Mesh mesh, Material material, Transform parent, long bytes)
        {
            Key = key;
            Mesh = mesh;
            Bytes = bytes;
            GameObject = new GameObject("SubterraChunk " + key) { layer = Layer };
            GameObject.transform.SetParent(parent, false);
            GameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer = GameObject.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            Renderer.receiveShadows = true;
            Renderer.enabled = false;
        }

        private Material rockMaterial;

        public ChunkKey Key { get; }

        public GameObject GameObject { get; }

        public Mesh Mesh { get; }

        public MeshRenderer Renderer { get; }

        public MeshCollider Collider { get; private set; }

        public long Bytes { get; }

        public bool IsEmpty => Mesh == null || Mesh.vertexCount == 0;

        /// <summary>The collider was assigned (or there is nothing to collide with).</summary>
        public bool ColliderReady => IsEmpty || Collider != null && Collider.enabled && Collider.sharedMesh != null;

        public bool BakeRequested { get; set; }

        /// <summary>Props merged on this chunk (children with their own renderers and, for collide scatters, colliders).</summary>
        public List<(ScatterMesh Data, GameObject Object)> Scatters { get; } = new List<(ScatterMesh, GameObject)>();

        public int ScatterInstances
        {
            get
            {
                int n = 0;
                foreach (var s in Scatters)
                {
                    n += s.Data.Instances;
                }

                return n;
            }
        }

        public void AddScatter(ScatterMesh scatter, Material material)
        {
            var go = new GameObject("SubterraScatter " + scatter.Scatter) { layer = Layer };
            go.transform.SetParent(GameObject.transform, false);
            Mesh visual = MeshUploader.Upload(scatter.Visual, go.name);
            go.AddComponent<MeshFilter>().sharedMesh = visual;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            if (scatter.Collider != null && !scatter.Collider.IsEmpty)
            {
                go.AddComponent<MeshCollider>().sharedMesh = MeshUploader.Upload(scatter.Collider, go.name + " collider");
            }

            go.SetActive(Renderer.enabled);
            Scatters.Add((scatter, go));
        }

        public bool Visible
        {
            get => Renderer.enabled;
            set
            {
                bool on = value && !IsEmpty;
                if (Renderer.enabled == on)
                {
                    return;
                }

                Renderer.enabled = on;
                foreach (var s in Scatters)
                {
                    s.Object.SetActive(on);
                }
            }
        }

        /// <summary>Assigns the (already baked) mesh to a MeshCollider.</summary>
        public void EnableCollider()
        {
            if (IsEmpty)
            {
                return;
            }

            if (Collider == null)
            {
                Collider = GameObject.AddComponent<MeshCollider>();
            }

            Collider.sharedMesh = Mesh;
            Collider.enabled = true;
        }

        public void DisableCollider()
        {
            if (Collider != null)
            {
                Collider.enabled = false;
            }
        }

        /// <summary>Debug view: another material instead of the rock (null restores the rock).</summary>
        public void DebugMaterial(Material debug)
        {
            if (debug != null)
            {
                if (rockMaterial == null)
                {
                    rockMaterial = Renderer.sharedMaterial;
                }

                Renderer.sharedMaterial = debug;
            }
            else if (rockMaterial != null)
            {
                Renderer.sharedMaterial = rockMaterial;
                rockMaterial = null;
            }
        }

        public void Destroy()
        {
            foreach (var s in Scatters)
            {
                Object.Destroy(s.Object.GetComponent<MeshFilter>().sharedMesh);
                MeshCollider collider = s.Object.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    Object.Destroy(collider.sharedMesh);
                }
            }

            Object.Destroy(GameObject);
            if (Mesh != null)
            {
                Object.Destroy(Mesh);
            }
        }
    }
}
