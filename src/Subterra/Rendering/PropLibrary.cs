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
            bool crystal = model.IndexOf("Crystal", System.StringComparison.OrdinalIgnoreCase) >= 0;
            string key = crystal ? "crystal" : ice ? "ice" : "rock/" + (body?.bodyName ?? "");
            if (!Materials.TryGetValue(key, out Material material) || material == null)
            {
                material = crystal ? MaterialFactory.Crystal()
                    : ice
                    ? MaterialFactory.Create("Ice_d", "Ice_n", new Color(0.75f, 0.85f, 0.95f))
                    : MaterialFactory.ForProps(body) ?? MaterialFactory.Create("Rock_d", "Rock_n", new Color(0.62f, 0.6f, 0.57f));
                Materials[key] = material;
            }

            return material;
        }

        /// <summary>Main thread only. A glowing scatter's material (see <see cref="Glow"/>); otherwise as <see cref="MaterialFor(string, CelestialBody)"/>.</summary>
        public static Material MaterialFor(ScatterSpec spec, string model, CelestialBody body)
        {
            if (spec?.Glow == null)
            {
                return MaterialFor(model, body);
            }

            string key = "glow/" + spec.Name + "/" + model + "/" + (body?.bodyName ?? "");
            if (!Materials.TryGetValue(key, out Material material) || material == null)
            {
                var colour = new Color(spec.Glow[0], spec.Glow[1], spec.Glow[2]);
                bool patch = model.IndexOf("GlowPatch", System.StringComparison.OrdinalIgnoreCase) >= 0;
                material = patch ? GlowPatchMaterial() : GlowBodyMaterial(colour);
                material.name = "Subterra glow " + spec.Name;
                Materials[key] = material;
                Glow.Add(new GlowMaterial { Material = material, Colour = colour, Patch = patch, Daylight = spec.Daylight });
                Glow.Apply(Glow.Count - 1, 1);
            }

            return material;
        }

        /// <summary>The glowing materials made so far: <see cref="GlowLights"/> dims them with the daylight each frame.</summary>
        public static readonly GlowMaterials Glow = new GlowMaterials();

        public sealed class GlowMaterial
        {
            public Material Material;
            public Color Colour;
            public bool Patch;
            public bool Daylight;
        }

        public sealed class GlowMaterials : List<GlowMaterial>
        {
            /// <summary>Sets material <paramref name="i"/>'s colour for a light level (0 = dark, 1 = full).</summary>
            public void Apply(int i, float level)
            {
                GlowMaterial g = this[i];
                if (g.Material == null)
                {
                    return;
                }

                if (g.Patch)
                {
                    // Additive particles double the tint: half of it is the colour itself.
                    g.Material.SetColor("_TintColor", g.Colour * (0.5f * level));
                }
                else if (g.Material.HasProperty("_EmissiveColor"))
                {
                    g.Material.SetColor("_EmissiveColor", g.Colour * level);
                }
                else
                {
                    g.Material.color = g.Colour * Mathf.Max(level, 0.15f);
                }
            }
        }

        // Light seen through rock or ice: additive (it brightens what is behind it, never darkens), fading to nothing at
        // the patch's edge by a radial texture.
        private static Material GlowPatchMaterial()
        {
            var material = new Material(Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive"));
            material.mainTexture = RadialFade();
            return material;
        }

        // Fungi and the like: lit like rock by lamps, plus their own light (emissive); unlit colour if KSP's emissive shader is missing.
        private static Material GlowBodyMaterial(Color colour)
        {
            Shader emissive = Shader.Find("KSP/Emissive/Diffuse");
            if (emissive == null)
            {
                return new Material(Shader.Find("KSP/Unlit")) { color = colour };
            }

            var material = new Material(emissive) { color = Color.Lerp(colour, Color.white, 0.5f) * 0.6f };
            material.SetTexture("_Emissive", Texture2D.whiteTexture);
            return material;
        }

        private static Texture2D radialFade;

        private static Texture2D RadialFade()
        {
            if (radialFade != null)
            {
                return radialFade;
            }

            const int size = 64;
            radialFade = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Subterra radial fade" };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - r);
                    a = a * a * (3 - 2 * a);
                    radialFade.SetPixel(x, y, new Color(a, a, a, a));
                }
            }

            radialFade.Apply(false, true);
            return radialFade;
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
