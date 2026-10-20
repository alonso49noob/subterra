using System.Collections.Generic;
using Subterra.Core.Model;
using UnityEngine;

namespace Subterra
{
    public enum RockShaderMode
    {
        /// <summary>KSP/Bumped Specular with box-projected UVs (chosen in M0: the stock triplanar terrain shader works on custom meshes but looks blocky up close).</summary>
        BumpedSpecularBoxUv,
    }

    /// <summary>Rock materials, one per layer (tinted), shared by all chunks.</summary>
    public static class MaterialFactory
    {
        /// <summary>Metres per texture tile for the box-projected UVs.</summary>
        public const float MetersPerUv = 4f;

        private static readonly Dictionary<string, Material> ByLayer = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> Generated = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<bool, float[]> Heights = new Dictionary<bool, float[]>();

        /// <summary>Builds the procedural textures now (at load) instead of at the first cave chunk.</summary>
        public static void Prewarm()
        {
            ForLayer(null);
            Create("Ice_d", "Ice_n", Color.white);
        }

        public static RockShaderMode Mode => RockShaderMode.BumpedSpecularBoxUv;

        public static Material ForLayer(LayerSpec layer)
        {
            string key = layer?.Name ?? "";
            if (ByLayer.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            float[] tint = layer?.Tint ?? new[] { 0.5f, 0.5f, 0.5f };
            material = Create("Rock_d", "Rock_n", new Color(tint[0], tint[1], tint[2]) * 1.6f);
            material.name = "Subterra rock " + key;
            ByLayer[key] = material;
            return material;
        }

        /// <summary>A Bumped Specular material from GameData/Subterra/Textures (procedural fallback).</summary>
        public static Material Create(string albedoName, string normalName, Color tint)
        {
            var material = new Material(Shader.Find("KSP/Bumped Specular"));
            material.SetTexture("_MainTex", Texture(albedoName, false));
            material.SetTexture("_BumpMap", Texture(normalName, true));
            material.SetColor("_Color", tint);
            material.SetColor("_SpecColor", new Color(0.08f, 0.08f, 0.08f));
            material.SetFloat("_Shininess", 0.08f);
            material.enableInstancing = true;
            return material;
        }

        private static Texture2D Texture(string name, bool isNormal)
        {
            Texture2D texture = GameDatabase.Instance.GetTexture("Subterra/Textures/" + name, isNormal);
            if (texture != null)
            {
                return texture;
            }

            bool ice = name.StartsWith("Ice", System.StringComparison.Ordinal);
            string key = name;
            if (Generated.TryGetValue(key, out Texture2D made) && made != null)
            {
                return made;
            }

            made = Procedural(isNormal, ice);
            Generated[key] = made;
            return made;
        }

        // Seamless procedural rock or ice (TextureGenerator); normal maps packed as Unity expects (x in alpha, y in green).
        private static Texture2D Procedural(bool isNormal, bool ice)
        {
            const int size = 256;
            if (!Heights.TryGetValue(ice, out float[] height))
            {
                height = Core.Meshing.TextureGenerator.Height(size, ice ? 23UL : 11UL, ice ? 3f : 6f);
                Heights[ice] = height;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var pixels = new Color32[size * size];
            if (isNormal)
            {
                Core.Numerics.Vec3[] normals = Core.Meshing.TextureGenerator.Normals(height, size, ice ? 1.5f : 5f);
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte x = (byte)(255 * (normals[i].X * 0.5f + 0.5f)), y = (byte)(255 * (normals[i].Y * 0.5f + 0.5f));
                    pixels[i] = new Color32(y, y, y, x);
                }
            }
            else
            {
                float[] albedo = Core.Meshing.TextureGenerator.Albedo(height, size, ice ? 0.65f : 0.35f, ice ? 0.95f : 0.75f);
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte g = (byte)(255 * Mathf.Clamp01(albedo[i]));
                    pixels[i] = ice ? new Color32((byte)(g * 0.9f), (byte)(g * 0.96f), g, 255) : new Color32(g, g, g, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
