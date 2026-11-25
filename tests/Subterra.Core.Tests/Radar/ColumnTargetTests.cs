using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class ColumnTargetTests
    {
        private static readonly List<string> Maria = new List<string> { "Lowlands", "Northern Basin", "Midlands", "Polar Lowlands" };

        private static LayerSpec L(string name, double top, double bottom, double density, List<string> where = null,
            double gasChance = 0, string gas = null) =>
            new LayerSpec
            {
                Name = name, Top = top, Bottom = bottom, Density = density, Where = where ?? new List<string>(),
                GasChance = gasChance, GasResource = gas,
            };

        // The Mun strata of StrataTests, with densities.
        private static Strata Mun() => new Strata(new List<LayerSpec>
        {
            L("Regolith", 0, 8, 1.5),
            L("MareBasalt", 8, 1500, 3.0, Maria),
            L("AnorthositeBreccia", 8, 1500, 2.6, new List<string> { "*" }),
            L("Megaregolith", 1500, 3000, 2.8),
            L("LowerCrust", 3000, 6000, 3.0),
            L("UpperMantle", 6000, 60000, 3.3),
            L("LowerMantle", 60000, 170000, 3.6),
            L("Core", 170000, 200000, 5.0),
        });

        private static readonly Vec3d Surface = new Vec3d(200000, 0, 0);

        [Fact]
        public void BoundariesUnderLowlands()
        {
            IReadOnlyList<SubsurfaceTarget> t = ColumnTargets.Boundaries(Mun(), "Lowlands", Surface, 2000);

            Assert.Equal(2, t.Count);
            Assert.Equal("layer:Regolith/MareBasalt", t[0].Id);
            Assert.Equal(8, t[0].Depth);
            Assert.Equal(1.5, t[0].DensityContrast, 9);
            Assert.Equal("MareBasalt", t[0].Host.Name);
            Assert.Equal("layer:MareBasalt/Megaregolith", t[1].Id);
            Assert.Equal(1500, t[1].Depth);
            Assert.Equal(0.2, t[1].DensityContrast, 9);
            Assert.All(t, x =>
            {
                Assert.Equal(TargetKind.LayerBoundary, x.Kind);
                Assert.Null(x.NetworkId);
                Assert.Equal(-1, x.PrimitiveIndex);
                Assert.True(double.IsPositiveInfinity(x.Width));
                Assert.True(double.IsPositiveInfinity(x.Length));
                Assert.Equal(0, x.Height);
            });
            Assert.Equal(200000 - 1500, t[1].Position.Length, 6);
            Assert.Equal(0, t[1].Lat, 6);
        }

        [Fact]
        public void BoundariesStopAtMaxDepth()
        {
            IReadOnlyList<SubsurfaceTarget> t = ColumnTargets.Boundaries(Mun(), "Lowlands", Surface, 100);

            Assert.Single(t);
            Assert.Equal(8, t[0].Depth);
        }

        [Fact]
        public void BoundaryAtMaxDepthIsExcluded()
        {
            Assert.Single(ColumnTargets.Boundaries(Mun(), "Lowlands", Surface, 1500));
        }

        private static Strata GasStrata(double chance) => new Strata(new List<LayerSpec>
        {
            L("Cap", 0, 50, 2.0),
            L("Pocket", 50, 400, 2.5, null, chance, "Methane"),
        });

        [Fact]
        public void GasOnlyWhereHit()
        {
            IReadOnlyList<SubsurfaceTarget> hit = ColumnTargets.Gas("Mun", 10.5, 20.5, GasStrata(1), "Lowlands", Surface, 1000);
            IReadOnlyList<SubsurfaceTarget> none = ColumnTargets.Gas("Mun", 10.5, 20.5, GasStrata(0), "Lowlands", Surface, 1000);

            Assert.Single(hit);
            Assert.Equal(TargetKind.Gas, hit[0].Kind);
            Assert.Equal(50, hit[0].Depth);
            Assert.Equal("Pocket", hit[0].Host.Name);
            Assert.Empty(none);
        }

        [Fact]
        public void GasBeyondMaxDepthIsLeftOut()
        {
            Assert.Empty(ColumnTargets.Gas("Mun", 10.5, 20.5, GasStrata(1), "Lowlands", Surface, 50));
        }

        [Fact]
        public void GasIsFixedPerPlace()
        {
            string a = ColumnTargets.Gas("Mun", 10.5, 20.5, GasStrata(1), "Lowlands", Surface, 1000).Single().Id;
            string b = ColumnTargets.Gas("Mun", 10.5, 20.5, GasStrata(1), "Lowlands", Surface, 1000).Single().Id;
            string c = ColumnTargets.Gas("Mun", 10.505, 20.505, GasStrata(1), "Lowlands", Surface, 1000).Single().Id;

            Assert.Equal(a, b);
            Assert.Equal(a, c); // same 0.01 degree cell
            Assert.StartsWith("gas:Mun:Pocket:", a);
        }
    }
}
