using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Scatter;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Prop meshes for cave scatters, read once on the main thread (then safe to read from workers):
    /// the GameDatabase model when one exists (<c>model</c> / <c>model_lod1</c>), else Subterra's
    /// procedural prop of that name. Materials: ice for names containing "Ice", rock otherwise.
    /// </summary>
    public static class PropLibrary
    {
        private static readonly Dictionary<(string, int), MeshData> Meshes = new Dictionary<(string, int), MeshData>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static void Preload(IEnumerable<ScatterSpec> scatters)
        {
            foreach (ScatterSpec spec in scatters)
            {
                foreach (string model in spec.Models)
                {
                    for (int lod = 0; lod <= 1; lod++)
                    {
                        if (!Meshes.ContainsKey((model, lod)))
                        {
                            MeshData mesh = FromDatabase(model, lod) ?? PropGenerator.Build(model, lod);
                            if (mesh == null)
                            {
                                Log.Warn($"Scatter {spec.Name}: no model '{model}' and no procedural prop of that name");
                            }

                            Meshes[(model, lod)] = mesh;
                        }
                    }
                }
            }
        }

        public static bool TryGet(string model, int lod, out MeshData mesh) => Meshes.TryGetValue((model, lod), out mesh) && mesh != null;

        /// <summary>Main thread only. Rock props take the body's own cliff texture (red on Duna, grey on the Mun).</summary>
        public static Material MaterialFor(string model, CelestialBody body = null)
        {
            bool ice = model.IndexOf("Ice", System.StringComparison.OrdinalIgnoreCase) >= 0;
            string key = ice ? "ice" : "rock/" + (body?.bodyName ?? "");
            if (!Materials.TryGetValue(key, out Material material) || material == null)
            {
                material = ice
                    ? MaterialFactory.Create("Ice_d", "Ice_n", new Color(0.75f, 0.85f, 0.95f))
                    : MaterialFactory.ForProps(body) ?? MaterialFactory.Create("Rock_d", "Rock_n", new Color(0.62f, 0.6f, 0.57f));
                Materials[key] = material;
            }

            return material;
        }

        private static MeshData FromDatabase(string model, int lod)
        {
            GameObject prefab = GameDatabase.Instance.GetModelPrefab(lod == 1 ? model + "_lod1" : model);
            MeshFilter filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null || !source.isReadable)
            {
                return null;
            }

            var mesh = new MeshData();
            foreach (Vector3 v in source.vertices)
            {
                mesh.Vertices.Add(v.ToCore());
            }

            foreach (Vector3 n in source.normals)
            {
                mesh.Normals.Add(n.ToCore());
            }

            foreach (Vector2 uv in source.uv)
            {
                mesh.Uvs.Add(new Vec2(uv.x, uv.y));
            }

            mesh.Triangles.AddRange(source.triangles);
            mesh.SkirtTriangleStart = mesh.Triangles.Count;
            return mesh;
        }
    }
}
