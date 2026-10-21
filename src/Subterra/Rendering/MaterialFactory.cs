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

        /// <summary>
        /// Rock of a layer. With <paramref name="body"/>, the body's own terrain textures (its cliff texture, as
        /// the stock PQS uses on steep ground) tinted by the layer, so caves look like the same ground as the
        /// surface; without, or when the terrain has none, the procedural rock.
        /// </summary>
        public static Material ForLayer(LayerSpec layer, CelestialBody body = null)
        {
            string key = (body?.bodyName ?? "") + "/" + (layer?.Name ?? "");
            if (ByLayer.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            float[] tint = layer?.Tint ?? new[] { 0.5f, 0.5f, 0.5f };
            var colour = new Color(tint[0], tint[1], tint[2]);
            material = FromTerrain(body, "_steepTex", "_steepBumpMap", Capped(colour * TerrainTint))
                ?? Create("Rock_d", "Rock_n", colour * 1.6f);
            material.name = "Subterra rock " + key;
            ByLayer[key] = material;
            return material;
        }

        /// <summary>The body's ground (its flat-terrain texture, untinted): for the rims around holes, so they blend into the terrain.</summary>
        public static Material ForSurface(CelestialBody body)
        {
            string key = (body?.bodyName ?? "") + "/#surface";
            if (ByLayer.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            // The terrain gets its colour from vertex colours this mesh has not got: a grey close to the surface's.
            material = FromTerrain(body, "_midTex", "_midBumpMap", SurfaceGrey) ?? ForLayer(null, body);
            ByLayer[key] = material;
            return material;
        }

        /// <summary>
        /// The ground with the colour the terrain has at a rim (its vertex colours, which the terrain shader multiplies its
        /// textures by): the rim matches the ground around it wherever it is. Shared per colour (to 1/64).
        /// </summary>
        public static Material ForSurfaceTinted(CelestialBody body, Color ground)
        {
            Color tint = new Color(ground.r, ground.g, ground.b) * GroundScale;
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}/#surface {1:F2} {2:F2} {3:F2}", body?.bodyName, Mathf.Round(tint.r * 64) / 64, Mathf.Round(tint.g * 64) / 64, Mathf.Round(tint.b * 64) / 64);
            if (ByLayer.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            material = FromTerrain(body, "_midTex", "_midBumpMap", tint) ?? ForLayer(null, body);
            ByLayer[key] = material;
            return material;
        }

        /// <summary>Terrain vertex colour to rim tint (calibrated on the Mun against the terrain next to a rim).</summary>
        private const float GroundScale = 1.2f;

        /// <summary>Rock props: the body's cliff texture a little lighter than the walls (loose blocks catch the light); null without one.</summary>
        public static Material ForProps(CelestialBody body)
        {
            Material material = FromTerrain(body, "_steepTex", "_steepBumpMap", new Color(1.1f, 1.1f, 1.1f));
            if (material != null)
            {
                material.name = "Subterra props " + body.bodyName;
            }

            return material;
        }

        /// <summary>Layer tints are absolute greys around 0.5; the terrain textures already have the body's colour.</summary>
        private const float TerrainTint = 2f;

        /// <summary>Tint of the terrain's flat texture on rims (the PQS adds its colour per vertex).</summary>
        private static readonly Color SurfaceGrey = new Color(0.52f, 0.515f, 0.53f);

        // Light layer tints (ice) doubled would wash the texture out: each channel at most MaxTint.
        private const float MaxTint = 1.15f;

        private static Color Capped(Color c) => new Color(Mathf.Min(c.r, MaxTint), Mathf.Min(c.g, MaxTint), Mathf.Min(c.b, MaxTint), 1);

        /// <summary>Metres per tile of the terrain textures on cave meshes (the meshes' UVs are 4 m per tile).</summary>
        private const float TerrainMetersPerTile = 8f;

        // A Bumped Specular material from two textures of the body's PQS material, or null if it has not got them.
        private static Material FromTerrain(CelestialBody body, string albedoProperty, string normalProperty, Color tint)
        {
            Material terrain = body?.pqsController?.surfaceMaterial;
            if (terrain == null || !terrain.HasProperty(albedoProperty) || !(terrain.GetTexture(albedoProperty) is Texture albedo))
            {
                return null;
            }

            var material = new Material(Shader.Find("KSP/Bumped Specular"));
            material.SetTexture("_MainTex", albedo);
            if (terrain.HasProperty(normalProperty) && terrain.GetTexture(normalProperty) is Texture normal)
            {
                material.SetTexture("_BumpMap", normal);
            }
            else
            {
                material.SetTexture("_BumpMap", Texture("Rock_n", true));
            }

            var scale = new Vector2(MetersPerUv / TerrainMetersPerTile, MetersPerUv / TerrainMetersPerTile);
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureScale("_BumpMap", scale);
            material.SetColor("_Color", tint);
            material.SetColor("_SpecColor", new Color(0.06f, 0.06f, 0.06f));
            material.SetFloat("_Shininess", 0.06f);
            material.enableInstancing = true;
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
