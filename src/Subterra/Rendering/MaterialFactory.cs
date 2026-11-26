using System;
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
            StartRockKinds();
            ForLayer(null);
            Create("Ice_d", "Ice_n", Color.white);
        }

        public static RockShaderMode Mode => RockShaderMode.BumpedSpecularBoxUv;

        /// <summary>
        /// Rock of a layer. With <paramref name="body"/>, the body's own terrain textures (its cliff texture, as
        /// the stock PQS uses on steep ground) tinted by the layer, so caves look like the same ground as the
        /// surface; without, or when the terrain has none, the procedural rock.
        /// </summary>
        /// <param name="rockTemp">The rock's temperature there (K), if known: hot rock glows (Lucas: the deep underground like
        /// the Earth's insides, dull red to white-hot), or turns iridescent with <c>deepRockLook = iridescent</c>.</param>
        /// <param name="depth">Metres under the surface (for the iridescent look).</param>
        public static Material ForLayer(LayerSpec layer, CelestialBody body = null, double? rockTemp = null, double depth = 0)
        {
            // Where two layers meet: a mix of both rocks, unless one of them wears the body's own ground (the surface layer, or
            // layerTextures off), which cannot be mixed: then the main one alone.
            bool blend = layer != null && layer.IsBlend;
            if (blend && (!Settings.Current.LayerTextures || layer.BlendUpper.Top <= 0 || layer.BlendLower.Top <= 0))
            {
                return ForLayer(layer.BlendShare < 0.5f ? layer.BlendUpper : layer.BlendLower, body, rockTemp, depth);
            }

            bool kindTexture = layer != null && layer.Top > 0 && Settings.Current.LayerTextures;
            string look = Settings.Current.DeepRockLook;
            int step = 0, glow = 0;
            if (kindTexture && rockTemp.HasValue && look == "iridescent" && !blend)
            {
                float iridescence = Core.Meshing.RockTextures.Iridescence(rockTemp, depth, body != null ? body.Radius : 0);
                step = Mathf.Clamp(Mathf.RoundToInt(iridescence * IridescenceSteps), 0, IridescenceSteps);
            }
            else if (kindTexture && rockTemp.HasValue && look == "glow" && rockTemp.Value > Core.Meshing.Incandescence.GlowStart)
            {
                glow = Mathf.Clamp(Mathf.RoundToInt((float)((rockTemp.Value - GlowFrom) / GlowStepKelvin)), 0, 40);
            }

            string key = (body?.bodyName ?? "") + "/" + (layer?.Name ?? "") + (step > 0 ? " iridescent " + step : "") + (glow > 0 ? " glow " + glow : "");
            if (ByLayer.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            float[] tint = layer?.Tint ?? new[] { 0.5f, 0.5f, 0.5f };
            var colour = new Color(tint[0], tint[1], tint[2]);
            // Each layer its own rock (Lucas): below the surface layer, the texture of its kind of rock (basalt, granite,
            // ice...). The surface layer keeps the body's own ground texture, so cave mouths match the terrain around them.
            material = blend ? ForRockBlend(body, layer) : null;
            if (blend && material == null)
            {
                return ForLayer(layer.BlendShare < 0.5f ? layer.BlendUpper : layer.BlendLower, body, rockTemp, depth);
            }

            material = material ?? (kindTexture
                ? ForRockKind(Core.Meshing.RockTextures.KindFor(layer.Name, layer.Texture), colour, step)
                : FromTerrain(body, "_steepTex", "_steepBumpMap", Capped(colour * TerrainTint)) ?? Create("Rock_d", "Rock_n", colour * 1.6f));
            if (glow > 0)
            {
                Glow(material, GlowFrom + glow * GlowStepKelvin);
            }

            material.name = "Subterra rock " + key;
            ByLayer[key] = material;
            return material;
        }

        /// <summary>Kelvin of the first glow step, and between steps (one material each, made when first seen).</summary>
        private const double GlowFrom = 700, GlowStepKelvin = 150;

        private static bool glowLogged;

        // Hot rock lights itself: the colour of a black body at its temperature added as emission (KSP's part shaders take it in
        // _TemperatureColor, the glow of an overheating part; without it, the rim light set to wrap the whole surface).
        private static void Glow(Material material, double kelvin)
        {
            Core.Meshing.Incandescence.Colour(kelvin, out float r, out float g, out float b);
            float intensity = Core.Meshing.Incandescence.Intensity(kelvin);
            if (material.HasProperty("_TemperatureColor"))
            {
                material.SetColor("_TemperatureColor", new Color(r, g, b, intensity));
            }
            else if (material.HasProperty("_RimColor"))
            {
                material.SetColor("_RimColor", new Color(r * intensity, g * intensity, b * intensity, 1));
                if (material.HasProperty("_RimFalloff"))
                {
                    material.SetFloat("_RimFalloff", 0.05f);
                }
            }

            if (!glowLogged)
            {
                glowLogged = true;
                Log.Info($"Hot rock glows through {(material.HasProperty("_TemperatureColor") ? "_TemperatureColor" : material.HasProperty("_RimColor") ? "_RimColor" : "nothing (no emission on " + material.shader?.name + ")")}");
            }
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

        /// <summary>A copy of the body's terrain material (its own shader, textures and tiling), for meshes that stand in for
        /// the terrain and carry its vertex colours; null if the body has no PQS material.</summary>
        public static Material TerrainClone(CelestialBody body)
        {
            Material terrain = body?.pqsController?.surfaceMaterial;
            if (terrain == null)
            {
                return null;
            }

            string key = body.bodyName + "/#terrain";
            if (!ByLayer.TryGetValue(key, out Material material) || material == null)
            {
                material = new Material(terrain) { name = "Subterra terrain " + body.bodyName };
                ByLayer[key] = material;
            }

            return material;
        }

        /// <summary>Geode crystals: pale violet, glossy (they catch a lamp's light), on the procedural ice textures.</summary>
        public static Material Crystal() => Crystal(new Color(0.86f, 0.80f, 1.0f));

        /// <summary>Glossy crystal of a colour (one material per colour).</summary>
        public static Material Crystal(Color tint)
        {
            string key = "#crystal/" + ColorUtility.ToHtmlStringRGB(tint);
            if (ByLayer.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            material = Create("Ice_d", "Ice_n", tint);
            material.SetColor("_SpecColor", new Color(0.85f, 0.85f, 0.9f));
            material.SetFloat("_Shininess", 0.75f);
            material.name = "Subterra crystal";
            ByLayer[key] = material;
            return material;
        }

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
        private const float TerrainMetersPerTile = 12f;

        private const float ArrayTerrainMetersPerTile = 32f;

        // A Bumped Specular material from two textures of the body's PQS material, or null if it has not got them.
        // Parallax (Continued) draws the terrain with its own material and 4096² textures per body: the caves use the same
        // rock when it is installed. Its textures are only borrowed while it draws a body (it unloads them after, which
        // left caves black), so the caves take their own copy on the graphics card the first time they find them loaded.
        // Nothing of Parallax ships with Subterra: the copy is made at run time from the player's own install.
        private static readonly Dictionary<string, string> ParallaxProperty = new Dictionary<string, string>
        {
            { "_steepTex", "_MainTexSteep" }, { "_steepBumpMap", "_BumpMapSteep" },
            { "_midTex", "_MainTexMid" }, { "_midBumpMap", "_BumpMapMid" },
        };

        /// <summary>Metres per tile of Parallax's textures on cave meshes (sharper than the stock ones: tiled closer).</summary>
        private const float ParallaxMetersPerTile = 16f;

        private static readonly Dictionary<string, Texture2D> ParallaxCopies = new Dictionary<string, Texture2D>();

        private static Material ParallaxMaterialOf(CelestialBody body)
        {
            if (body?.pqsController == null)
            {
                return null;
            }

            foreach (MeshRenderer renderer in body.pqsController.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material m = renderer.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name == "Custom/Parallax" && m.HasProperty("_MainTexSteep") && m.GetTexture("_MainTexSteep") != null)
                {
                    return m;
                }
            }

            return null;
        }

        // Subterra's own copy of one of Parallax's textures (same format and mipmaps, copied on the GPU); null if it is not
        // loaded now or cannot be copied.
        private static Texture2D ParallaxCopy(CelestialBody body, Material parallax, string property)
        {
            string key = body.bodyName + "/" + property;
            if (ParallaxCopies.TryGetValue(key, out Texture2D copy) && copy != null)
            {
                return copy;
            }

            if (!(parallax.GetTexture(property) is Texture2D source) || source == null)
            {
                return null;
            }

            try
            {
                copy = new Texture2D(source.width, source.height, source.format, source.mipmapCount > 1)
                {
                    name = "Subterra copy of " + source.name,
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = source.anisoLevel,
                    filterMode = source.filterMode,
                };
                Graphics.CopyTexture(source, copy);
                ParallaxCopies[key] = copy;
                return copy;
            }
            catch (Exception e)
            {
                Log.Warn($"Could not copy Parallax's {property} on {body.bodyName}: {e.Message}");
                return null;
            }
        }

        private static Material FromParallax(CelestialBody body, string albedoProperty, string normalProperty, Color tint)
        {
            if (!ParallaxProperty.TryGetValue(albedoProperty, out string albedoName) || !(ParallaxMaterialOf(body) is Material parallax)
                || !(ParallaxCopy(body, parallax, albedoName) is Texture2D albedo))
            {
                return null;
            }

            var material = new Material(Shader.Find("KSP/Bumped Specular"));
            material.SetTexture("_MainTex", albedo);
            material.SetTexture("_BumpMap", ParallaxProperty.TryGetValue(normalProperty, out string normalName) && ParallaxCopy(body, parallax, normalName) is Texture2D normal
                ? normal : Texture("Rock_n", true));
            var scale = new Vector2(MetersPerUv / ParallaxMetersPerTile, MetersPerUv / ParallaxMetersPerTile);
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureScale("_BumpMap", scale);
            material.SetColor("_Color", tint);
            material.SetColor("_SpecColor", new Color(0.06f, 0.06f, 0.06f));
            material.SetFloat("_Shininess", 0.06f);
            material.enableInstancing = true;
            Log.Info($"Cave rock on {body.bodyName} uses a copy of Parallax's {albedoName}");
            return material;
        }

        private static Material FromTerrain(CelestialBody body, string albedoProperty, string normalProperty, Color tint)
        {
            if (FromParallax(body, albedoProperty, normalProperty, tint) is Material parallaxRock)
            {
                return parallaxRock;
            }

            Material terrain = body?.pqsController?.surfaceMaterial;
            if (terrain == null || !terrain.HasProperty(albedoProperty) || !(terrain.GetTexture(albedoProperty) is Texture albedo))
            {
                return null;
            }

            // Low-quality material first where it exists: the texture-array terrain shader (Kerbin) keeps its real textures
            // there, the high-quality one only has a fallback.
            bool arrayTerrain = terrain.shader.name.Contains("Texture Array");
            Material low = body.pqsController.lowQualitySurfaceMaterial;
            if (low != null && low.HasProperty(albedoProperty) && low.GetTexture(albedoProperty) is Texture lowAlbedo && terrain.shader.name.Contains("Texture Array"))
            {
                terrain = low;
                albedo = lowAlbedo;
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

            // Texture-array terrains (Kerbin) have contrasted rock textures made for far views: tiled wider, or their
            // repetition shows across big chambers.
            float tile = arrayTerrain ? ArrayTerrainMetersPerTile : TerrainMetersPerTile;
            var scale = new Vector2(MetersPerUv / tile, MetersPerUv / tile);
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureScale("_BumpMap", scale);
            material.SetColor("_Color", tint);
            material.SetColor("_SpecColor", new Color(0.06f, 0.06f, 0.06f));
            material.SetFloat("_Shininess", 0.06f);
            material.enableInstancing = true;
            return material;
        }

        /// <summary>Still cave water: dark and see-through (the floor shows under it near the edge), glossy where a lamp
        /// catches it, its surface rippled by the ice normal map. Shared per colour.</summary>
        public static Material Water(float[] colour)
        {
            Color c = colour == null ? new Color(0.10f, 0.20f, 0.22f) : new Color(colour[0], colour[1], colour[2]);
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "#water {0:F2} {1:F2} {2:F2}", c.r, c.g, c.b);
            if (ByLayer.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("KSP/Alpha/Translucent Specular") ?? Shader.Find("KSP/Alpha/Translucent") ?? Shader.Find("Legacy Shaders/Transparent/Specular");
            material = new Material(shader) { name = "Subterra water" };
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", Texture("Ice_n", true));
            }

            material.SetColor("_Color", new Color(c.r, c.g, c.b, 0.82f));
            if (material.HasProperty("_SpecColor"))
            {
                material.SetColor("_SpecColor", new Color(0.5f, 0.5f, 0.5f));
                material.SetFloat("_Shininess", 0.6f);
            }

            ByLayer[key] = material;
            return material;
        }

        /// <summary>Pixels per side of the rock-kind textures, and metres of wall each tile covers.</summary>
        private const int RockKindSize = 512;

        private const float RockKindMetersPerTile = 8f;

        private static readonly Dictionary<Core.Meshing.RockKind, System.Threading.Tasks.Task<Core.Meshing.RockTexture>> RockKindJobs =
            new Dictionary<Core.Meshing.RockKind, System.Threading.Tasks.Task<Core.Meshing.RockTexture>>();

        private static readonly Dictionary<(Core.Meshing.RockKind, int), (Texture2D Albedo, Texture2D Normal, Color Mean)> RockKindTextures =
            new Dictionary<(Core.Meshing.RockKind, int), (Texture2D, Texture2D, Color)>();

        /// <summary>Steps of iridescence from none to full (one texture and material each, made when first seen).</summary>
        public const int IridescenceSteps = 8;

        // The textures of every kind of rock are worked out on worker threads while the game loads (pure maths in Core); the
        // first cave of a kind only uploads them.
        private static void StartRockKinds()
        {
            if (blendMasks == null)
            {
                blendMasks = System.Threading.Tasks.Task.Run(() => Core.Meshing.RockTextures.BlendMasks(RockKindSize, 0x5EB7E77AUL,
                    Core.Meshing.LayerBands.MinorShare, 1 - Core.Meshing.LayerBands.MinorShare));
            }

            foreach (Core.Meshing.RockKind kind in Enum.GetValues(typeof(Core.Meshing.RockKind)))
            {
                if (!RockKindJobs.ContainsKey(kind))
                {
                    RockKindJobs[kind] = System.Threading.Tasks.Task.Run(() => Core.Meshing.RockTextures.Generate(kind, RockKindSize, 0x5EB7E77AUL));
                }
            }
        }

        /// <summary>
        /// Rock of a kind tinted to a layer's colour: the colour is divided by the texture's own mean so that the wall's average
        /// colour stays the layer's (each body's layers keep their designed colours) while the texture brings the pattern.
        /// </summary>
        // The textures of a kind at a step of iridescence, uploaded the first time; null if the kind's texture failed.
        private static (Texture2D Albedo, Texture2D Normal, Color Mean)? ForRockKindTextures(Core.Meshing.RockKind kind, int step)
        {
            if (RockKindTextures.TryGetValue((kind, step), out var textures) && textures.Albedo != null)
            {
                return textures;
            }

            StartRockKinds();
            Core.Meshing.RockTexture rock;
            try
            {
                rock = RockKindJobs[kind].Result;
            }
            catch (Exception e)
            {
                Log.Warn($"Rock texture {kind} failed ({e.GetBaseException().Message}); procedural rock instead");
                return null;
            }

            if (step > 0)
            {
                // Lucas: ever more iridescent going deeper and hotter. The film pattern was worked out at load; this only
                // blends its colours in (a few hundredths of a second, once per kind and step).
                Texture2D relief = ForRockKindTextures(kind, 0)?.Normal;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                textures = Upload(Core.Meshing.RockTextures.Iridescent(rock, (float)step / IridescenceSteps, 0x5EB7E77AUL), kind, step, relief);
                Log.Info($"Iridescent rock {kind} step {step}/{IridescenceSteps} made in {clock.ElapsedMilliseconds} ms");
            }
            else
            {
                textures = Upload(rock, kind, 0);
            }

            RockKindTextures[(kind, step)] = textures;
            return textures;
        }

        // The masks of the two mixed bands between layers (see LayerBands), worked out while the game loads.
        private static System.Threading.Tasks.Task<float[][]> blendMasks;

        private static readonly Dictionary<string, (Texture2D Albedo, Texture2D Normal, float Scale)> BlendTextures =
            new Dictionary<string, (Texture2D, Texture2D, float)>();

        private static float[] BlendMask(float share)
        {
            if (blendMasks == null)
            {
                blendMasks = System.Threading.Tasks.Task.Run(() => Core.Meshing.RockTextures.BlendMasks(RockKindSize, 0x5EB7E77AUL,
                    Core.Meshing.LayerBands.MinorShare, 1 - Core.Meshing.LayerBands.MinorShare));
            }

            float[][] masks = blendMasks.Result;
            return share < 0.5f ? masks[0] : masks[1];
        }

        /// <summary>
        /// The walls where two layers meet (Lucas: subtler, interlocked changes of rock): both rocks mixed by a mask, each with
        /// the contrast and tint it has on its own walls; the main one's gloss. Null if a rock's texture failed.
        /// </summary>
        private static Material ForRockBlend(CelestialBody body, LayerSpec blend)
        {
            string key = (body?.bodyName ?? "") + "/" + blend.Name;
            if (!BlendTextures.TryGetValue(key, out var textures) || textures.Albedo == null)
            {
                StartRockKinds();
                Core.Meshing.RockKind upperKind = Core.Meshing.RockTextures.KindFor(blend.BlendUpper.Name, blend.BlendUpper.Texture);
                Core.Meshing.RockKind lowerKind = Core.Meshing.RockTextures.KindFor(blend.BlendLower.Name, blend.BlendLower.Texture);
                try
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Core.Meshing.RockTexture mixed = Core.Meshing.RockTextures.Blend(RockKindJobs[upperKind].Result, RockKindJobs[lowerKind].Result,
                        BlendMask(blend.BlendShare), blend.BlendUpper.Tint, blend.BlendLower.Tint, KindContrast, MaxKindTint, out float scale);
                    Core.Meshing.RockKind main = blend.BlendShare < 0.5f ? upperKind : lowerKind;
                    var (albedo, normal, _) = Upload(mixed, main, 0, null, 1f, "Subterra rock " + key);
                    textures = (albedo, normal, scale);
                    BlendTextures[key] = textures;
                    Log.Info($"Rock between layers {blend.Name} ({upperKind}/{lowerKind}) made in {clock.ElapsedMilliseconds} ms");
                }
                catch (Exception e)
                {
                    Log.Warn($"Rock between layers {blend.Name} failed ({e.GetBaseException().Message}); its main layer instead");
                    return null;
                }
            }

            LayerSpec mainLayer = blend.BlendShare < 0.5f ? blend.BlendUpper : blend.BlendLower;
            Core.Meshing.RockKind kind = Core.Meshing.RockTextures.KindFor(mainLayer.Name, mainLayer.Texture);
            var material = new Material(Shader.Find("KSP/Bumped Specular"));
            material.SetTexture("_MainTex", textures.Albedo);
            material.SetTexture("_BumpMap", textures.Normal);
            var tiling = new Vector2(MetersPerUv / RockKindMetersPerTile, MetersPerUv / RockKindMetersPerTile);
            material.SetTextureScale("_MainTex", tiling);
            material.SetTextureScale("_BumpMap", tiling);
            material.SetColor("_Color", new Color(textures.Scale, textures.Scale, textures.Scale, 1));
            float gloss = kind == Core.Meshing.RockKind.Ice ? 0.35f : kind == Core.Meshing.RockKind.Metal ? 0.5f : kind == Core.Meshing.RockKind.Salt ? 0.2f : 0.06f;
            material.SetColor("_SpecColor", new Color(gloss, gloss, gloss));
            material.SetFloat("_Shininess", Mathf.Max(0.06f, gloss * 0.8f));
            material.enableInstancing = true;
            return material;
        }

        private static Material ForRockKind(Core.Meshing.RockKind kind, Color layerColour, int step)
        {
            if (!(ForRockKindTextures(kind, step) is var found) || found == null)
            {
                return Create("Rock_d", "Rock_n", layerColour * 1.6f);
            }

            var textures = found.Value;
            var material = new Material(Shader.Find("KSP/Bumped Specular"));
            material.SetTexture("_MainTex", textures.Albedo);
            material.SetTexture("_BumpMap", textures.Normal);
            var scale = new Vector2(MetersPerUv / RockKindMetersPerTile, MetersPerUv / RockKindMetersPerTile);
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureScale("_BumpMap", scale);
            Color mean = textures.Mean;
            material.SetColor("_Color", new Color(
                Mathf.Min(MaxKindTint, layerColour.r / Mathf.Max(0.05f, mean.r)),
                Mathf.Min(MaxKindTint, layerColour.g / Mathf.Max(0.05f, mean.g)),
                Mathf.Min(MaxKindTint, layerColour.b / Mathf.Max(0.05f, mean.b)), 1));

            // Ice, salt and metal catch a lamp's light; rock barely does.
            float gloss = kind == Core.Meshing.RockKind.Ice ? 0.35f : kind == Core.Meshing.RockKind.Metal ? 0.5f : kind == Core.Meshing.RockKind.Salt ? 0.2f : 0.06f;
            var spec = new Color(gloss, gloss, gloss);
            if (step > 0)
            {
                // Iridescent rock shines, and its sheen changes colour with the angle it is seen from: the highlight takes one
                // hue, the edges (KSP's rim light) another, both stronger the deeper and hotter.
                float s = (float)step / IridescenceSteps;
                float hue = Mathf.Repeat(0.55f + 0.11f * step + 0.07f * (int)kind, 1f);
                spec = Color.Lerp(spec, Color.HSVToRGB(hue, 0.8f, 1f) * 0.3f, s);
                gloss = Mathf.Lerp(gloss, 0.3f, s);
                if (material.HasProperty("_RimColor"))
                {
                    Color rim = Color.HSVToRGB(Mathf.Repeat(hue + 0.4f, 1f), 0.9f, 1f) * (0.15f * s);
                    material.SetColor("_RimColor", new Color(rim.r, rim.g, rim.b, 1));
                    if (material.HasProperty("_RimFalloff"))
                    {
                        material.SetFloat("_RimFalloff", 4f);
                    }
                }
            }

            material.SetColor("_SpecColor", spec);
            material.SetFloat("_Shininess", Mathf.Max(0.06f, gloss * 0.8f));
            material.enableInstancing = true;
            return material;
        }

        /// <summary>Most a channel of a rock-kind tint may be (a light layer on a dark texture would blow out: the first photos
        /// in game showed washed-out walls at 2.5).</summary>
        private const float MaxKindTint = 1.8f;

        /// <summary>How much of the texture's own contrast is kept around its mean (the clasts and grains read too harsh under
        /// a floodlight at full contrast).</summary>
        private const float KindContrast = 0.7f;

        private static (Texture2D, Texture2D, Color) Upload(Core.Meshing.RockTexture rock, Core.Meshing.RockKind kind, int step, Texture2D sameRelief = null,
            float contrast = KindContrast, string name = null)
        {
            double mr = 0, mg = 0, mb = 0;
            for (int i = 0; i < rock.R.Length; i++)
            {
                mr += rock.R[i];
                mg += rock.G[i];
                mb += rock.B[i];
            }

            mr /= rock.R.Length;
            mg /= rock.R.Length;
            mb /= rock.R.Length;
            int size = rock.Size;
            name = name ?? "Subterra rock " + kind + (step > 0 ? " iridescent " + step : "");
            var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            Texture2D normal = sameRelief ?? new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name + " normal", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var colours = new Color32[size * size];
            double sr = 0, sg = 0, sb = 0;
            for (int i = 0; i < colours.Length; i++)
            {
                float r = Mathf.Clamp01((float)(mr + (rock.R[i] - mr) * contrast));
                float g = Mathf.Clamp01((float)(mg + (rock.G[i] - mg) * contrast));
                float b = Mathf.Clamp01((float)(mb + (rock.B[i] - mb) * contrast));
                colours[i] = new Color32((byte)(255 * r), (byte)(255 * g), (byte)(255 * b), 255);
                sr += r;
                sg += g;
                sb += b;
            }

            albedo.SetPixels32(colours);
            albedo.Apply(true);

            if (sameRelief != null)
            {
                Compress(albedo, kind);
                return (albedo, normal, new Color((float)(sr / colours.Length), (float)(sg / colours.Length), (float)(sb / colours.Length)));
            }

            // Normal maps packed as Unity expects them (x in alpha, y in green).
            float strength = kind == Core.Meshing.RockKind.Ice || kind == Core.Meshing.RockKind.Metal ? 2f : 5f;
            Core.Numerics.Vec3[] normals = Core.Meshing.TextureGenerator.Normals(rock.Height, size, strength);
            for (int i = 0; i < colours.Length; i++)
            {
                byte x = (byte)(255 * (normals[i].X * 0.5f + 0.5f)), y = (byte)(255 * (normals[i].Y * 0.5f + 0.5f));
                colours[i] = new Color32(y, y, y, x);
            }

            normal.SetPixels32(colours);
            normal.Apply(true);
            Compress(albedo, kind);
            Compress(normal, kind);

            int n = colours.Length;
            return (albedo, normal, new Color((float)(sr / n), (float)(sg / n), (float)(sb / n)));
        }

        private static void Compress(Texture2D texture, Core.Meshing.RockKind kind)
        {
            try
            {
                texture.Compress(true);
            }
            catch (Exception e)
            {
                Log.Warn($"Rock texture {kind} left uncompressed: {e.Message}");
            }
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
