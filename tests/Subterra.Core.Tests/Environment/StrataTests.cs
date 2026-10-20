using System.Collections.Generic;
using Subterra.Core.Environment;
using Subterra.Core.Model;
using Xunit;

namespace Subterra.Core.Tests.Environment
{
    public class StrataTests
    {
        private static readonly List<string> Maria = new List<string> { "Lowlands", "Northern Basin", "Midlands", "Polar Lowlands" };

        private static LayerSpec L(string name, double top, double bottom, double gradient, List<string> where = null) =>
            new LayerSpec { Name = name, Top = top, Bottom = bottom, Gradient = gradient, Where = where ?? new List<string>() };

        private static readonly Strata Mun = new Strata(new List<LayerSpec>
        {
            L("Regolith", 0, 8, 4),
            L("MareBasalt", 8, 1500, 4, Maria),
            L("AnorthositeBreccia", 8, 1500, 3, new List<string> { "*" }),
            L("Megaregolith", 1500, 3000, 3),
            L("LowerCrust", 3000, 6000, 3),
            L("UpperMantle", 6000, 60000, 2),
            L("LowerMantle", 60000, 170000, 1.5),
            L("Core", 170000, 200000, 0.5),
        });

        private static readonly Geotherm Heat = new Geotherm(new GeothermSpec { SurfaceMeanTemp = 250 }, Mun);

        [Fact] public void MareUnderLowlands() => Assert.Equal("MareBasalt", Mun.LayerAt(100, "Lowlands").Name);

        [Fact] public void BrecciaUnderHighlands() => Assert.Equal("AnorthositeBreccia", Mun.LayerAt(100, "Highlands").Name);

        [Fact] public void Megaregolith() => Assert.Equal("Megaregolith", Mun.LayerAt(2000, "Lowlands").Name);

        [Fact] public void NullAboveDamping() => Assert.Null(Heat.Temperature(1.0, "Lowlands"));

        [Fact] public void At100m() => Assert.Equal(250 + 0.008 * 4 + 0.092 * 4, Heat.Temperature(100, "Lowlands").Value, 9);

        [Fact] public void At70mFloorOfDevTube() => Assert.Equal(250.28, Heat.Temperature(70, "Lowlands").Value, 9);

        [Fact]
        public void ContinuousAtBoundaries()
        {
            foreach (double b in new double[] { 8, 1500, 3000, 6000, 60000, 170000 })
            {
                foreach (string biome in new[] { "Lowlands", "Highlands" })
                {
                    Assert.True(System.Math.Abs(Heat.Temperature(b - 1e-6, biome).Value - Heat.Temperature(b + 1e-6, biome).Value) < 1e-3);
                }
            }
        }

        [Fact]
        public void BelowLastLayerKeepsItsGradient()
        {
            double t1 = Heat.Temperature(210000, "Lowlands").Value, t2 = Heat.Temperature(220000, "Lowlands").Value;
            Assert.Equal(0.5 * 10, t2 - t1, 6);
        }
    }
}
