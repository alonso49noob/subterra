using System;

namespace Subterra.Core.Geo
{
    /// <summary>
    /// A regular lat/lon grid of samples: n × n values, row i at latitude latMin + i·Δlat, column j at
    /// longitude lonMin + j·Δlon (index i·n + j). Heights are bilinear, biomes nearest-neighbour;
    /// points outside are clamped to the edge. Longitudes may exceed 180 (cells across the antimeridian).
    /// </summary>
    public sealed class GridSamples : IHeightSampler, IBiomeSampler
    {
        private readonly double latMin, latMax, lonMin, lonMax;
        private readonly int n;
        private readonly double[] radius;
        private readonly string[] biome;

        public GridSamples(double latMin, double latMax, double lonMin, double lonMax, int n, double[] radius, string[] biome)
        {
            if (n < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(n), "needs at least 2 × 2 samples");
            }

            if (radius == null || radius.Length != n * n || biome == null || biome.Length != n * n)
            {
                throw new ArgumentException("radius and biome need n × n values");
            }

            this.latMin = latMin;
            this.latMax = latMax;
            this.lonMin = lonMin;
            this.lonMax = lonMax;
            this.n = n;
            this.radius = radius;
            this.biome = biome;
        }

        public int N => n;

        public double Lat(int i) => latMin + (latMax - latMin) * i / (n - 1);

        public double Lon(int j) => lonMin + (lonMax - lonMin) * j / (n - 1);

        public double SurfaceRadius(double latDeg, double lonDeg)
        {
            double fi = Fraction(latDeg, latMin, latMax);
            double fj = Fraction(UnwrapLon(lonDeg), lonMin, lonMax);
            int i0 = Math.Min((int)Math.Floor(fi), n - 2);
            int j0 = Math.Min((int)Math.Floor(fj), n - 2);
            double ti = fi - i0, tj = fj - j0;
            double a = radius[i0 * n + j0], b = radius[i0 * n + j0 + 1];
            double c = radius[(i0 + 1) * n + j0], d = radius[(i0 + 1) * n + j0 + 1];
            return (a * (1 - tj) + b * tj) * (1 - ti) + (c * (1 - tj) + d * tj) * ti;
        }

        public string Biome(double latDeg, double lonDeg)
        {
            int i = (int)Math.Round(Fraction(latDeg, latMin, latMax));
            int j = (int)Math.Round(Fraction(UnwrapLon(lonDeg), lonMin, lonMax));
            return biome[i * n + j];
        }

        // Grid position in [0, n-1] along one axis.
        private double Fraction(double value, double min, double max)
        {
            if (max <= min)
            {
                return 0;
            }

            double f = (value - min) / (max - min) * (n - 1);
            return Math.Max(0, Math.Min(n - 1, f));
        }

        // Brings a longitude into the grid's span when the grid uses longitudes beyond ±180.
        private double UnwrapLon(double lon)
        {
            double centre = (lonMin + lonMax) * 0.5;
            while (lon - centre > 180)
            {
                lon -= 360;
            }

            while (lon - centre < -180)
            {
                lon += 360;
            }

            return lon;
        }
    }
}
