using System;
using System.Collections.Generic;
using Subterra.Core.Environment;
using Subterra.Core.Model;

namespace Subterra.Core.Excavation
{
    /// <summary>
    /// Loads a vertical blast hole the way a mine does (sub-project 2, §7.4): explosive decks along the hole,
    /// sized by each layer's powder factor, nothing where the hole crosses a cave, inert gaps between decks and
    /// stemming at the top; fired bottom-up. The blast opens a vertical shaft of <see cref="Radius"/>.
    /// </summary>
    public sealed class BlastDesign
    {
        /// <summary>Length (m) of each explosive deck.</summary>
        public const double DeckLength = 3;

        /// <summary>Inert gap (m) between decks.</summary>
        public const double DeckGap = 1;

        /// <summary>A deck: depths below the terrain (top &lt; bottom) and its explosive (kg).</summary>
        public readonly struct Deck
        {
            public Deck(double top, double bottom, double kg, string layer)
            {
                Top = top;
                Bottom = bottom;
                Kg = kg;
                Layer = layer;
            }

            public double Top { get; }

            public double Bottom { get; }

            public double Kg { get; }

            public string Layer { get; }
        }

        private BlastDesign(double radius, double holeTop, double holeBottom, double stemming, List<Deck> decks)
        {
            Radius = radius;
            HoleTop = holeTop;
            HoleBottom = holeBottom;
            Stemming = stemming;
            Decks = decks;
            double kg = 0;
            foreach (Deck d in decks)
            {
                kg += d.Kg;
            }

            TotalKg = kg;
        }

        /// <summary>Radius (m) of the shaft the blast opens.</summary>
        public double Radius { get; }

        public double HoleTop { get; }

        public double HoleBottom { get; }

        /// <summary>Inert column (m) at the top of the hole.</summary>
        public double Stemming { get; }

        /// <summary>Decks from the bottom up (the firing order).</summary>
        public IReadOnlyList<Deck> Decks { get; }

        public double TotalKg { get; }

        /// <summary>The highest deck (sets the danger at the surface), or null without decks.</summary>
        public Deck? TopDeck => Decks.Count == 0 ? (Deck?)null : Decks[Decks.Count - 1];

        /// <summary>
        /// Powder factor (kg of explosive per m³ of rock broken): about 0.4 in soft, light rock to 2 in hard,
        /// dense rock (shaft sinking needs more than an open pit).
        /// </summary>
        public static double PowderFactor(LayerSpec layer)
        {
            double h = layer?.Hardness ?? 0.5, density = layer?.Density ?? 2.7;
            return (0.4 + 1.6 * h) * density / 2.7;
        }

        /// <summary>
        /// Loads the hole from <paramref name="holeTop"/> to <paramref name="holeBottom"/> (depths below the terrain)
        /// to open a shaft of <paramref name="radius"/>; <paramref name="voids"/> are depth spans where the hole
        /// crosses caves (never loaded). Stemming is the larger of 2 m and the radius (about one burden).
        /// </summary>
        public static BlastDesign Plan(double holeTop, double holeBottom, double radius, Strata strata, string biome,
            IEnumerable<(double top, double bottom)> voids)
        {
            var spans = new List<(double top, double bottom)>(voids ?? new (double, double)[0]);
            double stemming = Math.Max(2, radius);
            var decks = new List<Deck>();
            double area = Math.PI * radius * radius;

            // From the bottom up: a deck, a gap, a deck...
            double bottom = holeBottom;
            while (bottom - DeckLength >= holeTop + stemming - 1e-9)
            {
                double top = bottom - DeckLength;
                if (Overlaps(spans, top, bottom))
                {
                    bottom -= 0.5;
                    continue;
                }

                LayerSpec layer = strata.LayerAt((top + bottom) * 0.5, biome);
                double kg = PowderFactor(layer) * area * (DeckLength + DeckGap);
                decks.Add(new Deck(top, bottom, kg, layer?.Name));
                bottom = top - DeckGap;
            }

            return new BlastDesign(radius, holeTop, holeBottom, stemming, decks);
        }

        private static bool Overlaps(List<(double top, double bottom)> spans, double top, double bottom)
        {
            foreach (var s in spans)
            {
                if (s.top < bottom && s.bottom > top)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// How far a blast hurts: Hopkinson–Cranz scaled distance Z = R / W^(1/3) (m/kg^(1/3)). In the open, Z ≈ 3
    /// wrecks parts and Z ≈ 8 still damages them; a charge confined under its stemming throws far less at the
    /// surface (radii × 0.35).
    /// </summary>
    public static class BlastSafety
    {
        public const double DestroyZ = 3;

        public const double DamageZ = 8;

        public const double ConfinedFactor = 0.35;

        public static (double destroy, double damage) Radii(double kg, bool confined)
        {
            if (kg <= 0)
            {
                return (0, 0);
            }

            double w = Math.Pow(kg, 1.0 / 3.0), f = confined ? ConfinedFactor : 1;
            return (DestroyZ * w * f, DamageZ * w * f);
        }
    }
}
