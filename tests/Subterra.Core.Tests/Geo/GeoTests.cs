using System;
using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Xunit;

namespace Subterra.Core.Tests.Geo
{
    public class GeoTests
    {
        private static void Near(Vec3d expected, Vec3d actual)
        {
            Assert.True((expected - actual).Length < 1e-12, $"{actual} != {expected}");
        }

        [Fact] public void DirectionEquator() => Near(new Vec3d(1, 0, 0), BodyFrame.Direction(0, 0));

        [Fact] public void DirectionNorthPole() => Near(new Vec3d(0, 1, 0), BodyFrame.Direction(90, 0));

        [Fact] public void DirectionLon90() => Near(new Vec3d(0, 0, 1), BodyFrame.Direction(0, 90));

        [Fact]
        public void LatLonRoundTrip()
        {
            var rng = new Rng(11);
            for (int i = 0; i < 1000; i++)
            {
                double lat = rng.Range(-89.999, 89.999);
                double lon = rng.Range(-179.999, 180.0);
                BodyFrame.ToLatLon(BodyFrame.Direction(lat, lon) * 123456.0, out double lat2, out double lon2);
                Assert.True(Math.Abs(lat - lat2) < 1e-9, $"lat {lat} → {lat2}");
                Assert.True(Math.Abs(lon - lon2) < 1e-9, $"lon {lon} → {lon2}");
            }
        }

        [Fact]
        public void PrecomputedReturnsExactlyWhatWasAdded()
        {
            var s = new PrecomputedSamples();
            s.Add(1.25, -38.4, 200123.5, "Lowlands");
            Assert.True(s.Has(1.25, -38.4));
            Assert.Equal(200123.5, s.SurfaceRadius(1.25, -38.4));
            Assert.Equal("Lowlands", s.Biome(1.25, 321.6));
            Assert.Throws<KeyNotFoundException>(() => s.SurfaceRadius(1.25, -38.5));
        }

        [Fact]
        public void GridBilinearOnLinearField()
        {
            const int n = 5;
            var radius = new double[n * n];
            var biome = new string[n * n];
            Func<double, double, double> f = (lat, lon) => 200000 + 3 * lat - 7 * lon;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double lat = -2 + 4.0 * i / (n - 1), lon = 10 + 2.0 * j / (n - 1);
                    radius[i * n + j] = f(lat, lon);
                    biome[i * n + j] = lat < 0 ? "South" : "North";
                }
            }

            var g = new GridSamples(-2, 2, 10, 12, n, radius, biome);
            Assert.Equal(f(0.37, 11.13), g.SurfaceRadius(0.37, 11.13), 6);
            Assert.Equal(f(-1.9, 10.01), g.SurfaceRadius(-1.9, 10.01), 6);
            Assert.Equal("North", g.Biome(1.9, 11));
            Assert.Equal("South", g.Biome(-1.9, 11));
        }
    }
}
