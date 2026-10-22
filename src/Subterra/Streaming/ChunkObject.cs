using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Subterra
{
    /// <summary>
    /// One loaded chunk. Empty chunks (no cave surface) are bare markers without a GameObject. A
    /// non-empty chunk is a layer-15 GameObject under its network's root with a render mesh, and for
    /// LOD0 chunks near vessels a collider mesh (no skirts) baked on a worker before it is assigned.
    /// Colliding props get their own baked collider, enabled together with the chunk's.
    /// </summary>
    public sealed class ChunkObject
    {
        public const int Layer = 15;

        private Material rockMaterial;

        public ChunkObject(Core.Meshing.ChunkKey key, long bytes)
        {
            Key = key;
            Bytes = bytes;
        }

        public Core.Meshing.ChunkKey Key { get; }

        public long Bytes { get; set; }

        /// <summary>Null for an empty chunk.</summary>
        public GameObject GameObject { get; private set; }

        public Mesh Mesh { get; private set; }

        public MeshRenderer Renderer { get; private set; }

        public bool IsEmpty => GameObject == null;

        /// <summary>Collider geometry kept until the collider mesh is baked (LOD0 chunks only).</summary>
        public Core.Meshing.MeshData ColliderData { get; set; }

        /// <summary>Collider mesh (render mesh without skirts); null until requested.</summary>
        public Mesh ColliderMesh { get; set; }

        public MeshCollider Collider { get; private set; }

        /// <summary>A worker is baking this chunk's collider meshes: it must not be destroyed meanwhile.</summary>
        public bool BakeInFlight { get; set; }

        public bool Baked { get; set; }

        /// <summary>Props merged on this chunk.</summary>
        public List<ScatterPart> Scatters { get; } = new List<ScatterPart>();

        public int ScatterInstances
        {
            get
            {
                int n = 0;
                foreach (ScatterPart s in Scatters)
                {
                    n += s.Data.Instances;
                }

                return n;
            }
        }

        /// <summary>The chunk can be stood on: empty, or its (and its props') colliders are on.</summary>
        public bool ColliderReady => IsEmpty || Collider != null && Collider.enabled;

        public bool Visible { get; private set; }

        /// <summary>Creates the GameObject and render mesh (main thread).</summary>
        public void Create(Mesh mesh, Material material, Transform parent)
        {
            Mesh = mesh;
            GameObject = new GameObject("SubterraChunk " + Key) { layer = Layer };
            GameObject.transform.SetParent(parent, false);
            GameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer = GameObject.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            Renderer.receiveShadows = true;
            Renderer.enabled = Visible;
        }

        public void AddScatter(ScatterMesh scatter, Mesh visual, Material material)
        {
            var go = new GameObject("SubterraScatter " + scatter.Scatter) { layer = Layer };
            go.transform.SetParent(GameObject.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = visual;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // Glowing props are light, not things in it: they neither cast nor take shadows.
            bool glow = scatter.Spec?.Glow != null;
            renderer.shadowCastingMode = glow ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !glow;
            renderer.enabled = Visible;
            Scatters.Add(new ScatterPart { Data = scatter, Object = go, Renderer = renderer, Visual = visual });
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (Renderer != null && Renderer.enabled != visible)
            {
                Renderer.enabled = visible;
                foreach (ScatterPart s in Scatters)
                {
                    s.Renderer.enabled = visible;
                }
            }
        }

        /// <summary>Assigns the baked collider meshes (chunk and props).</summary>
        public void EnableCollider()
        {
            if (IsEmpty || ColliderMesh == null)
            {
                return;
            }

            foreach (ScatterPart s in Scatters)
            {
                if (s.ColliderMesh != null)
                {
                    if (s.Collider == null)
                    {
                        s.Collider = s.Object.AddComponent<MeshCollider>();
                    }

                    s.Collider.sharedMesh = s.ColliderMesh;
                    s.Collider.enabled = true;
                }
            }

            if (Collider == null)
            {
                Collider = GameObject.AddComponent<MeshCollider>();
            }

            if (Collider.sharedMesh != ColliderMesh)
            {
                Collider.sharedMesh = ColliderMesh;
            }

            Collider.enabled = true;
        }

        public void DisableCollider()
        {
            if (Collider != null && Collider.enabled)
            {
                Collider.enabled = false;
                foreach (ScatterPart s in Scatters)
                {
                    if (s.Collider != null)
                    {
                        s.Collider.enabled = false;
                    }
                }
            }
        }

        /// <summary>Debug view: another material instead of the rock (null restores the rock).</summary>
        public void DebugMaterial(Material debug)
        {
            if (Renderer == null)
            {
                return;
            }

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
            foreach (ScatterPart s in Scatters)
            {
                DestroyObject(s.Visual);
                DestroyObject(s.ColliderMesh);
            }

            DestroyObject(GameObject);
            DestroyObject(Mesh);
            DestroyObject(ColliderMesh);
            Scatters.Clear();
            GameObject = null;
        }

        private static void DestroyObject(Object o)
        {
            if (o != null)
            {
                Object.Destroy(o);
            }
        }

        public sealed class ScatterPart
        {
            public ScatterMesh Data;
            public GameObject Object;
            public MeshRenderer Renderer;
            public Mesh Visual;
            public Mesh ColliderMesh;
            public MeshCollider Collider;
        }
    }
}
