using System;
using Subterra.Core.Numerics;

namespace Subterra.Core.Meshing
{
    /// <summary>Kinds of rock a layer can look like (its procedural texture).</summary>
    public enum RockKind
    {
        Generic,
        Regolith,
        Basalt,
        Anorthosite,
        Breccia,
        Limestone,
        Sandstone,
        Granite,
        Ice,
        Salt,
        Metal,
        Olivine,
        Mantle,
        Core,
        Sediment,
        Gabbro,
        Gneiss,
        Asthenosphere,
        MoltenMetal,
    }

    /// <summary>A generated rock texture: colour (0–1 per channel) and height (0–1), row-major, <see cref="Size"/> square.</summary>
    public sealed class RockTexture
    {
        public int Size;
        public float[] R, G, B;
        public float[] Height;

        /// <summary>Thin-film thickness (in colour cycles) for <see cref="RockTextures.Iridescent"/>; null = worked out there.</summary>
        public float[] Film;
    }

    /// <summary>
    /// Procedural, seamlessly tiling textures for each kind of rock (Lucas: each layer its own texture, not one tinted
    /// texture for all): vesicular basalt, light anorthosite with feldspar, regolith with pebbles, breccia's angular
    /// clasts, limestone and sandstone in strata, speckled granite, cracked and bubbly ice, salt crystals, the
    /// Widmanstätten lamellae of meteoritic metal, olivine crystals, dark mantle peridotite, a rusty iron core.
    /// Everything tiles: cellular noise wraps around a whole number of cells and bands use whole frequencies.
    /// [Claude, revisar]
    /// </summary>
    public static class RockTextures
    {
        private static readonly (string Word, RockKind Kind)[] NameRules =
        {
            // Deep structure (Lucas: layers like the Earth's): before the generic "core", "crust", "mantle".
            ("outercore", RockKind.MoltenMetal), ("molten", RockKind.MoltenMetal), ("innercore", RockKind.Metal),
            ("asthenosphere", RockKind.Asthenosphere), ("melt", RockKind.Asthenosphere), ("lithospher", RockKind.Olivine),
            ("gabbro", RockKind.Gabbro), ("lowercrust", RockKind.Gabbro), ("middlecrust", RockKind.Gneiss), ("gneiss", RockKind.Gneiss),
            ("breccia", RockKind.Breccia), ("megaregolith", RockKind.Breccia), ("rubble", RockKind.Breccia),
            ("ice", RockKind.Ice), ("icy", RockKind.Ice), ("frost", RockKind.Ice),
            ("basalt", RockKind.Basalt), ("volcanic", RockKind.Basalt), ("lava", RockKind.Basalt), ("magma", RockKind.Basalt),
            ("salt", RockKind.Salt), ("evaporite", RockKind.Salt),
            ("anorthosite", RockKind.Anorthosite),
            ("regolith", RockKind.Regolith),
            ("limestone", RockKind.Limestone), ("karst", RockKind.Limestone),
            ("sandstone", RockKind.Sandstone),
            ("granite", RockKind.Granite), ("crust", RockKind.Granite),
            ("metal", RockKind.Metal), ("iron", RockKind.Metal), ("nickel", RockKind.Metal),
            ("olivine", RockKind.Olivine), ("peridot", RockKind.Olivine),
            ("mantle", RockKind.Mantle),
            ("core", RockKind.Core),
            ("sediment", RockKind.Sediment), ("soil", RockKind.Sediment), ("clay", RockKind.Sediment), ("mud", RockKind.Sediment),
        };

        /// <summary>The kind for a layer: its explicit <c>texture</c> value if any, else guessed from its name.</summary>
        public static RockKind KindFor(string layerName, string explicitKind = null)
        {
            if (!string.IsNullOrEmpty(explicitKind) && Enum.TryParse(explicitKind.Trim(), true, out RockKind parsed))
            {
                return parsed;
            }

            string n = (layerName ?? "").ToLowerInvariant();
            // First match wins ("MareBasalt" holds "salt": basalt goes first): "IcyBreccia" and "AnorthositeBreccia" are breccias, "IceRegolith" is ice, "Megaregolith"
            // a breccia, "MagmaChamberRoof" basalt, "DeepCrust" granite.
            foreach (var (word, kind) in NameRules)
            {
                if (n.Contains(word))
                {
                    return kind;
                }
            }

            return RockKind.Generic;
        }

        public static RockTexture Generate(RockKind kind, int size, ulong seed)
        {
            var t = new RockTexture { Size = size, R = new float[size * size], G = new float[size * size], B = new float[size * size], Height = new float[size * size] };
            t.Film = new float[size * size];
            int[][] filmTables = Noise.Tables(Hash.Combine(seed, 0x1B1DUL));
            int[][] tables = Noise.Tables(Hash.Combine(seed, (ulong)kind + 1));
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    Pixel(kind, u, v, tables, seed, out float h, out float r, out float g, out float b);
                    int i = y * size + x;
                    t.Height[i] = Clamp01(h);
                    t.R[i] = Clamp01(r);
                    t.G[i] = Clamp01(g);
                    t.B[i] = Clamp01(b);
                    t.Film[i] = 2.2f * t.Height[i] + 2.5f * Fbm(u, v, 2, filmTables, 3);
                }
            }

            return t;
        }

        /// <summary>
        /// <paramref name="rock"/> with thin-film colours over it (Lucas: the deep, hot underground ever more iridescent):
        /// bands of interference colours, as on an oil film or tempered metal, following the rock's relief and a slow swirl;
        /// <paramref name="strength"/> 0 (none) to 1 (vivid). The rock's light and dark stay; it tiles like the rock.
        /// </summary>
        public static RockTexture Iridescent(RockTexture rock, float strength, ulong seed)
        {
            float s = Clamp01(strength);
            int size = rock.Size;
            var t = new RockTexture { Size = size, R = new float[rock.R.Length], G = new float[rock.G.Length], B = new float[rock.B.Length], Height = rock.Height };
            int[][] tables = rock.Film == null ? Noise.Tables(Hash.Combine(seed, 0x1B1DUL)) : null;
            t.Film = rock.Film;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float u = (float)x / size, v = (float)y / size;
                    // Film thickness: the relief plus a broad swirl, in waves (two to four colour cycles across a tile).
                    float film = rock.Film?[i] ?? 2.2f * rock.Height[i] + 2.5f * Fbm(u, v, 2, tables, 3);
                    float luma = (rock.R[i] + rock.G[i] + rock.B[i]) / 3;
                    // Deep, saturated interference colours (squared: less pastel), lit by the rock's own brightness.
                    float r = Sq(0.5f + 0.5f * (float)Math.Cos(2 * Math.PI * film));
                    float g = Sq(0.5f + 0.5f * (float)Math.Cos(2 * Math.PI * (film - 1f / 3)));
                    float b = Sq(0.5f + 0.5f * (float)Math.Cos(2 * Math.PI * (film - 2f / 3)));
                    // Gentle at first, full near the core (Lucas: ever more iridescent going deeper).
                    float k = 0.8f * (float)Math.Pow(s, 1.6), lift = 0.25f + 1.0f * luma;
                    t.R[i] = Clamp01(rock.R[i] * (1 - k) + r * lift * k);
                    t.G[i] = Clamp01(rock.G[i] * (1 - k) + g * lift * k);
                    t.B[i] = Clamp01(rock.B[i] * (1 - k) + b * lift * k);
                }
            }

            return t;
        }

        /// <summary>How iridescent rock is at <paramref name="rockTemp"/> K and <paramref name="depth"/> m in a body of
        /// <paramref name="radius"/> m: from nothing near the surface to 1 at about 2000 K or at the centre.</summary>
        public static float Iridescence(double? rockTemp, double depth, double radius)
        {
            double heat = rockTemp.HasValue ? (rockTemp.Value - 400) / 1600 : 0;
            double deep = radius > 0 ? depth / radius : 0;
            return Clamp01((float)Math.Max(heat, deep));
        }

        private static void Pixel(RockKind kind, float u, float v, int[][] tables, ulong seed, out float h, out float r, out float g, out float b)
        {
            float n = Fbm(u, v, 6, tables, 5);
            float fine = Fbm(u + 0.31f, v + 0.17f, 24, tables, 3);
            switch (kind)
            {
                case RockKind.Regolith:
                {
                    Cells(u, v, 20, seed, out float f1, out _, out float cellTone);
                    float pebble = f1 < 0.28f ? (0.28f - f1) / 0.28f : 0;
                    h = 0.5f * n + 0.3f * fine + 0.4f * pebble;
                    float l = 0.42f + 0.22f * n + 0.12f * fine + 0.1f * pebble * (cellTone - 0.5f);
                    Grey(l, out r, out g, out b);
                    return;
                }

                case RockKind.Basalt:
                {
                    Cells(u, v, 28, seed, out float f1, out _, out _);
                    float vesicle = f1 < 0.2f ? 1 - f1 / 0.2f : 0;
                    h = 0.55f * n + 0.35f * fine - 0.6f * vesicle + 0.3f;
                    float l = 0.24f + 0.12f * n + 0.06f * fine - 0.12f * vesicle;
                    Tone(l, 1.0f, 0.98f, 0.96f, out r, out g, out b);
                    return;
                }

                case RockKind.Anorthosite:
                {
                    // Coarse pale feldspar crystals, each its own shade, with a little dark pyroxene between some of them. The
                    // grain is warped so it reads as rock, not paving.
                    Cells(u + 0.03f * (n - 0.5f), v + 0.03f * (fine - 0.5f), 12, seed, out float f1, out float f2, out float cellTone);
                    float edge = Math.Min(1, (f2 - f1) / 0.05f);
                    float mafic = cellTone < 0.06f ? 1 : 0;
                    h = 0.4f * n + 0.3f * cellTone + 0.15f * edge + 0.15f * fine;
                    float l = 0.66f + 0.2f * cellTone + 0.08f * n + 0.04f * fine - 0.07f * (1 - edge) - 0.28f * mafic;
                    Tone(l, 1.0f, 1.0f, 0.97f, out r, out g, out b);
                    return;
                }

                case RockKind.Breccia:
                {
                    // Angular clasts of many shades and sizes in a fine, darker matrix (two scales of clasts).
                    float wu = u + 0.05f * (n - 0.5f), wv = v + 0.05f * (fine - 0.5f);
                    Cells(wu, wv, 5, seed, out float f1, out float f2, out float bigTone);
                    Cells(wu + 0.37f, wv + 0.11f, 13, seed + 3, out float s1, out float s2, out float smallTone);
                    bool big = (f2 - f1) > 0.22f, small = !big && (s2 - s1) > 0.3f;
                    float tone = big ? bigTone : small ? smallTone : -1;
                    h = tone >= 0 ? 0.55f + 0.3f * tone + 0.15f * n : 0.25f + 0.3f * fine;
                    float l = tone >= 0 ? 0.3f + 0.45f * tone + 0.06f * fine : 0.32f + 0.12f * fine;
                    Tone(l, 1.0f, 0.97f, 0.93f, out r, out g, out b);
                    return;
                }

                case RockKind.Limestone:
                {
                    // Thick pale beds, each its own shade, with thin darker bedding planes and scattered fossil specks.
                    Strata(u, 6, 0.08f * n, seed, out float bed, out float plane);
                    Cells(u, v, 30, seed + 5, out float s1, out _, out float speckTone);
                    float fossil = s1 < 0.08f && speckTone > 0.7f ? 1 : 0;
                    h = 0.5f + 0.2f * n + 0.2f * fine - 0.4f * plane + 0.1f * fossil;
                    float l = 0.62f + 0.14f * bed + 0.06f * n + 0.04f * fine - 0.18f * plane + 0.12f * fossil;
                    Tone(l, 1.0f, 0.96f, 0.86f, out r, out g, out b);
                    return;
                }

                case RockKind.Sandstone:
                {
                    // Beds of varied colour, and inside each bed fine slanted laminae (cross-bedding).
                    Strata(u, 8, 0.06f * n, seed, out float bed, out float plane);
                    float laminae = Bands(u * 2 + v + 0.05f * n, 16);
                    h = 0.45f + 0.3f * fine + 0.07f * laminae - 0.35f * plane;
                    float l = 0.52f + 0.22f * bed + 0.025f * laminae + 0.05f * fine - 0.15f * plane;
                    Tone(l, 1.0f, 0.8f, 0.58f, out r, out g, out b);
                    return;
                }
                case RockKind.Granite:
                {
                    // Grains of four minerals, finer and soberer than before (Lucas: the first one looked like confetti): dark
                    // mica, greyish-white feldspar, faintly pink feldspar, grey quartz, mostly greys.
                    Cells(u + 0.01f * (n - 0.5f), v + 0.01f * (fine - 0.5f), 56, seed, out _, out _, out float cellTone);
                    h = 0.5f * n + 0.5f * cellTone;
                    if (cellTone < 0.14f)
                    {
                        r = 0.16f; g = 0.16f; b = 0.17f;
                    }
                    else if (cellTone < 0.52f)
                    {
                        r = 0.66f; g = 0.65f; b = 0.64f;
                    }
                    else if (cellTone < 0.66f)
                    {
                        r = 0.66f; g = 0.58f; b = 0.55f;
                    }
                    else
                    {
                        r = 0.52f; g = 0.52f; b = 0.54f;
                    }

                    float shade = 0.85f + 0.15f * n;
                    r *= shade; g *= shade; b *= shade;
                    return;
                }

                case RockKind.Gabbro:
                {
                    // The lower crust: coarse dark pyroxene among grey plagioclase.
                    Cells(u + 0.02f * (n - 0.5f), v + 0.02f * (fine - 0.5f), 26, seed, out float f1, out float f2, out float cellTone);
                    float edge = Math.Min(1, (f2 - f1) / 0.05f);
                    h = 0.45f * n + 0.35f * cellTone + 0.2f * edge;
                    float l = cellTone < 0.48f ? 0.16f + 0.06f * n : 0.46f + 0.12f * cellTone;
                    l -= 0.05f * (1 - edge);
                    Tone(l, 0.98f, 1.0f, 1.0f, out r, out g, out b);
                    return;
                }

                case RockKind.Gneiss:
                {
                    // The middle crust: rock folded and banded under heat and pressure, light and dark bands that wander.
                    Strata(u, 18, 0.14f * n + 0.03f * fine, seed + 21, out float bed, out float plane);
                    bool felsic = bed > 0.45f;
                    h = 0.45f + 0.3f * (felsic ? 1 : 0) + 0.15f * fine - 0.2f * plane;
                    float l = felsic ? 0.56f + 0.14f * bed + 0.06f * fine : 0.2f + 0.1f * bed + 0.04f * fine;
                    Tone(l, 1.0f, 0.97f, 0.94f, out r, out g, out b);
                    return;
                }

                case RockKind.Asthenosphere:
                {
                    // Partly molten mantle: dark peridotite veined with glowing melt.
                    float wu = u + 0.05f * (n - 0.5f), wv = v + 0.05f * (fine - 0.5f);
                    Cells(wu, wv, 9, seed + 31, out float f1, out float f2, out _);
                    float vein = (f2 - f1) < 0.06f ? 1 - (f2 - f1) / 0.06f : 0;
                    h = 0.5f + 0.3f * n - 0.3f * vein;
                    float l = 0.24f + 0.1f * n;
                    r = l * 1.1f + vein * 0.85f;
                    g = l * 0.9f + vein * 0.42f;
                    b = l * 0.7f + vein * 0.08f;
                    return;
                }

                case RockKind.MoltenMetal:
                {
                    // The liquid outer core: iron flowing in swirls, glowing.
                    float flow = 0.5f + 0.5f * (float)Math.Sin(2 * Math.PI * (u * 3 + 2.5f * n));
                    float f = 0.7f * flow + 0.3f * fine;
                    h = 0.5f + 0.2f * flow;
                    r = 0.6f + 0.4f * f;
                    g = 0.22f + 0.5f * f;
                    b = 0.06f + 0.2f * f * f;
                    return;
                }

                case RockKind.Ice:
                {
                    Cells(u, v, 8, seed, out float f1, out float f2, out _);
                    float crack = (f2 - f1) < 0.03f ? 1 : 0;
                    Cells(u + 0.5f, v + 0.25f, 48, seed + 7, out float b1, out _, out _);
                    float bubble = b1 < 0.12f ? 1 - b1 / 0.12f : 0;
                    h = 0.7f + 0.15f * n - 0.5f * crack;
                    r = 0.72f + 0.1f * n + 0.15f * bubble - 0.3f * crack;
                    g = 0.84f + 0.08f * n + 0.1f * bubble - 0.25f * crack;
                    b = 0.93f + 0.05f * n + 0.05f * bubble - 0.15f * crack;
                    return;
                }

                case RockKind.Salt:
                {
                    // Interlocking halite crystals, pale and slightly translucent-looking, faint boundaries.
                    Cells(u + 0.02f * (n - 0.5f), v + 0.02f * (fine - 0.5f), 10, seed, out float f1, out float f2, out float cellTone);
                    float facet = Math.Min(1, (f2 - f1) / 0.04f);
                    h = 0.5f * cellTone + 0.2f * facet + 0.3f * fine;
                    float l = 0.8f + 0.12f * cellTone + 0.04f * fine - 0.08f * (1 - facet);
                    Tone(l, 1.0f, 0.93f, 0.93f, out r, out g, out b);
                    return;
                }

                case RockKind.Metal:
                {
                    // Widmanstätten pattern: big grains (each etched to its own sheen), crossed by three sets of long straight
                    // kamacite plates with thin dark taenite between them; one set dominates in each grain. The directions
                    // (1,0), (2,3) and (2,-3) have whole frequencies, so the pattern tiles.
                    Cells(u, v, 3, seed, out _, out _, out float grain);
                    float wu = u + 0.01f * (n - 0.5f), wv = v + 0.01f * (fine - 0.5f);
                    float a = Lines(7 * wu + 0.25f), c = Lines(2 * wu + 3 * wv + 0.4f), d = Lines(2 * wu - 3 * wv + 0.15f);
                    int dominant = (int)(grain * 3);
                    float lines = Math.Max(a * (dominant == 0 ? 1 : 0.35f), Math.Max(c * (dominant == 1 ? 1 : 0.35f), d * (dominant == 2 ? 1 : 0.35f)));
                    h = 0.6f + 0.1f * fine - 0.4f * lines;
                    float l = 0.55f + 0.25f * grain + 0.05f * fine - 0.3f * lines;
                    Tone(l, 0.97f, 0.98f, 1.0f, out r, out g, out b);
                    return;
                }
                case RockKind.Olivine:
                {
                    Cells(u, v, 18, seed, out float f1, out float f2, out float cellTone);
                    float edge = Math.Min(1, (f2 - f1) / 0.06f);
                    h = 0.5f * cellTone + 0.4f * edge + 0.1f * n;
                    float l = 0.4f + 0.25f * cellTone - 0.15f * (1 - edge);
                    Tone(l, 0.82f, 1.0f, 0.48f, out r, out g, out b);
                    return;
                }

                case RockKind.Mantle:
                {
                    Cells(u, v, 22, seed, out _, out _, out float cellTone);
                    h = 0.6f * n + 0.4f * cellTone;
                    float l = 0.26f + 0.12f * n + 0.08f * cellTone;
                    Tone(l, 0.95f, 1.0f, 0.72f, out r, out g, out b);
                    return;
                }

                case RockKind.Core:
                {
                    float streak = Bands(u + 0.15f * n, 5);
                    h = 0.5f * n + 0.3f * streak + 0.2f * fine;
                    float l = 0.38f + 0.14f * n + 0.08f * streak;
                    Tone(l, 1.0f, 0.68f, 0.46f, out r, out g, out b);
                    return;
                }

                case RockKind.Sediment:
                {
                    // Many thin beds of varied shade (silt, clay, sand), slightly wavy.
                    Strata(u, 14, 0.05f * n, seed, out float bed, out float plane);
                    h = 0.4f + 0.3f * bed + 0.3f * fine - 0.3f * plane;
                    float l = 0.42f + 0.2f * bed + 0.08f * fine - 0.1f * plane;
                    Tone(l, 1.0f, 0.92f, 0.78f, out r, out g, out b);
                    return;
                }

                default:
                {
                    float crack = n < 0.18f ? n / 0.18f : 1f;
                    h = n;
                    Grey((0.35f + 0.4f * n) * (0.6f + 0.4f * crack), out r, out g, out b);
                    return;
                }
            }
        }

        // Tiling fBm in [0, 1] (four shifted copies blended across the edges). Lucas saw seams in the rock every tile: callers
        // passing u + 0.31 went past 1, where the blend no longer tiles, and the linear blend tiled in value but not in slope,
        // a crease the normal map (made from the slope) lit as a line. Coordinates wrap, and the blend eases in and out (its
        // weights flat at the edges), so the value and its slope match across them.
        private static float Fbm(float u, float v, float features, int[][] tables, int octaves)
        {
            u = Wrap(u);
            v = Wrap(v);
            float a = S(u, v), b = S(u - 1, v), c = S(u, v - 1), d = S(u - 1, v - 1);
            float wu = u * u * (3 - 2 * u), wv = v * v * (3 - 2 * v);
            float wa = (1 - wu) * (1 - wv), wb = wu * (1 - wv), wc = (1 - wu) * wv, wd = wu * wv;
            float value = a * wa + b * wb + c * wc + d * wd;
            // Averaging copies flattens the noise: in the middle of the tile it had about half the contrast it had at the edges,
            // a rougher band round every tile that read as a grid. Scaled back by the blend's own spread, the same everywhere.
            value /= (float)Math.Sqrt(wa * wa + wb * wb + wc * wc + wd * wd);
            // The fBm hardly strays from 0 (a few hundredths): stretched to use the whole 0–1, about as before on average.
            return Clamp01(value * 1.25f + 0.5f);

            float S(float x, float y) => Noise.Fbm(new Vec3(x * features, y * features, 0.37f), tables, octaves);
        }

        // Cellular (Worley) noise wrapping every `cells` cells: distances to the nearest and second-nearest feature point
        // (in cell units, 0..~1.4) and a random tone (0–1) of the nearest point's cell.
        private static void Cells(float u, float v, int cells, ulong seed, out float f1, out float f2, out float tone)
        {
            float x = Wrap(u) * cells, y = Wrap(v) * cells;
            int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
            f1 = f2 = float.MaxValue;
            tone = 0;
            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                    ulong hsh = Hash.Combine(seed, (ulong)(wx * 73856093 ^ wy * 19349663 ^ cells * 83492791));
                    float px = gx + Unit(hsh), py = gy + Unit(Hash.Mix(hsh + 1));
                    float dx = px - x, dy = py - y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist < f1)
                    {
                        f2 = f1;
                        f1 = dist;
                        tone = Unit(Hash.Mix(hsh + 2));
                    }
                    else if (dist < f2)
                    {
                        f2 = dist;
                    }
                }
            }
        }

        // Beds across t: `count` (whole) beds, their boundaries shifted by `warp` (tiling noise); each bed a random shade
        // (0–1) and `plane` 1 on the thin bedding plane at its base.
        private static void Strata(float t, int count, float warp, ulong seed, out float bed, out float plane)
        {
            float x = Wrap(t) * count + warp * count;
            int index = (int)Math.Floor(x);
            float f = x - index;
            int wrapped = ((index % count) + count) % count;
            bed = Unit(Hash.Combine(seed + 11, (ulong)(wrapped * 2654435761u + (uint)count)));
            plane = f < 0.05f ? 1 - f / 0.05f : 0;
        }

        // Thin dark lines where t is whole: 1 on the line, 0 a little off it.
        private static float Lines(float t)
        {
            float f = t - (float)Math.Floor(t);
            float d = Math.Min(f, 1 - f);
            return d < 0.04f ? 1 - d / 0.04f : 0;
        }

        // Soft bands: `count` (whole) bands across 0..1 of t, 0–1.
        private static float Bands(float t, int count) => 0.5f + 0.5f * (float)Math.Sin(t * count * 2 * Math.PI);

        private static float Wrap(float t) => t - (float)Math.Floor(t);

        private static float Unit(ulong h) => (h >> 40) / (float)(1UL << 24);

        private static float Sq(float x) => x * x;

        private static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;

        private static void Grey(float l, out float r, out float g, out float b) => r = g = b = l;

        private static void Tone(float l, float tr, float tg, float tb, out float r, out float g, out float b)
        {
            r = l * tr;
            g = l * tg;
            b = l * tb;
        }
    }
}
