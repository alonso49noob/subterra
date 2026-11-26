using System;
using Subterra.Core.Environment;
using Xunit;

namespace Subterra.Core.Tests.Environment
{
    public class AirColumnTests
    {
        // Kerbin: 600 km, scale height about 5.6 km, 288 K at sea level, rock warming 23 K/km.
        private static AirColumn Kerbin() => new AirColumn(600000, 5600, 288, z => 285 + 0.023 * z);

        [Fact]
        public void ThePressureGrowsGoingDown()
        {
            AirColumn air = Kerbin();
            Assert.Equal(1, air.PressureFactor(0), 6);
            double previous = 1;
            foreach (double depth in new[] { 100.0, 1000, 5000, 35000, 100000, 300000, 600000 })
            {
                double f = air.PressureFactor(depth);
                Assert.True(f > previous, $"{depth} m: {f}");
                previous = f;
            }
        }

        [Fact]
        public void NearTheDatumItIsTheBarometricFormula()
        {
            // Over the first hundreds of metres gravity and temperature hardly change: exp(depth / H).
            AirColumn air = Kerbin();
            Assert.InRange(air.PressureFactor(500), Math.Exp(500 / 5600.0) * 0.99, Math.Exp(500 / 5600.0) * 1.01);
        }

        [Fact]
        public void HotRockAndWeakGravityKeepItBounded()
        {
            // About 17 atm 35 km down, hundreds (not astronomical numbers) at the core.
            AirColumn air = Kerbin();
            Assert.InRange(air.PressureFactor(35000), 10, 30);
            Assert.InRange(air.PressureFactor(600000), 100, 2000);
        }

        [Fact]
        public void TheAirTakesTheRocksTemperature()
        {
            AirColumn air = Kerbin();
            Assert.Equal(288, air.Temperature(0), 1);
            Assert.InRange(air.Temperature(26862), 288 + 0.023 * 26862 - 5, 288 + 0.023 * 26862 + 5);
        }

        [Fact]
        public void WithoutAGeothermTheRockWarmsAtTheDefaultGradient()
        {
            var air = new AirColumn(200000, 7000, 250);
            Assert.InRange(air.Temperature(10000), 250 + AirColumn.DefaultGradient * 10000 - 1, 250 + AirColumn.DefaultGradient * 10000 + 1);
        }

        [Fact]
        public void GravityFallsToZeroAtTheCentre()
        {
            Assert.Equal(1, AirColumn.GravityFactor(0, 600000));
            Assert.Equal(0.5, AirColumn.GravityFactor(300000, 600000), 6);
            Assert.Equal(0, AirColumn.GravityFactor(600000, 600000));
            Assert.Equal(1, AirColumn.GravityFactor(-100, 600000));
        }
    }
}
