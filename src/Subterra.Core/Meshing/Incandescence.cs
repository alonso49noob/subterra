using System;

namespace Subterra.Core.Meshing
{
    /// <summary>
    /// How hot rock glows (Lucas: the deep underground should look like the Earth's insides, as diagrams draw them: a red
    /// mantle, an orange outer core, a yellow inner core). A black body's colours, shifted a step cooler than the physical
    /// ones so the inside of a planet reads as those diagrams do (a real 3600 K core would look almost white): dark red from
    /// about 800 K, red at 2000 K, orange at 3000 K, yellow at 4000 K, white past 5500 K. The glow stays moderate, so the rock
    /// still shows through it.
    /// </summary>
    public static class Incandescence
    {
        /// <summary>Kelvin below which rock does not glow.</summary>
        public const double GlowStart = 750;

        /// <summary>Brightest the glow gets (0–1): over it the rock's texture still shows.</summary>
        public const float MaxIntensity = 0.6f;

        // Kelvin and colour, coolest first.
        private static readonly (double K, float R, float G, float B)[] Palette =
        {
            (750, 0.35f, 0.02f, 0.00f),
            (1200, 0.62f, 0.06f, 0.02f),
            (2000, 0.88f, 0.18f, 0.04f),
            (3000, 1.00f, 0.42f, 0.08f),
            (4000, 1.00f, 0.72f, 0.26f),
            (5500, 1.00f, 0.93f, 0.72f),
        };

        /// <summary>The colour (0–1 per channel) of rock glowing at <paramref name="kelvin"/>.</summary>
        public static void Colour(double kelvin, out float r, out float g, out float b)
        {
            if (kelvin <= Palette[0].K)
            {
                (r, g, b) = (Palette[0].R, Palette[0].G, Palette[0].B);
                return;
            }

            for (int i = 1; i < Palette.Length; i++)
            {
                if (kelvin <= Palette[i].K)
                {
                    float t = (float)((kelvin - Palette[i - 1].K) / (Palette[i].K - Palette[i - 1].K));
                    r = Palette[i - 1].R + (Palette[i].R - Palette[i - 1].R) * t;
                    g = Palette[i - 1].G + (Palette[i].G - Palette[i - 1].G) * t;
                    b = Palette[i - 1].B + (Palette[i].B - Palette[i - 1].B) * t;
                    return;
                }
            }

            var last = Palette[Palette.Length - 1];
            (r, g, b) = (last.R, last.G, last.B);
        }

        /// <summary>How bright the glow is at <paramref name="kelvin"/>: 0 under <see cref="GlowStart"/> to
        /// <see cref="MaxIntensity"/> at about 3000 K.</summary>
        public static float Intensity(double kelvin)
        {
            if (kelvin <= GlowStart)
            {
                return 0;
            }

            double x = Math.Min(1, (kelvin - GlowStart) / 2250);
            return MaxIntensity * (float)Math.Pow(x, 1.3);
        }
    }
}
