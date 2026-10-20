using System;
using Subterra.Core.Geo;
using Subterra.Core.Model;

namespace Subterra.Core.Tests
{
    /// <summary>Terrain of constant radius with one biome (or a biome function).</summary>
    internal sealed class FlatSampler : IHeightSampler, IBiomeSampler
    {
        private readonly Func<double, double, double> radius;
        private readonly Func<double, double, string> biome;

        public FlatSampler(double radius, string biome = "Lowlands")
            : this((lat, lon) => radius, (lat, lon) => biome)
        {
        }

        public FlatSampler(Func<double, double, double> radius, Func<double, double, string> biome)
        {
            this.radius = radius;
            this.biome = biome;
        }

        public double SurfaceRadius(double latDeg, double lonDeg) => radius(latDeg, lonDeg);

        public string Biome(double latDeg, double lonDeg) => biome(latDeg, lonDeg);
    }

    internal static class Sites
    {
        public const double R = 200000;

        /// <summary>Degrees of arc for <paramref name="meters"/> on the test body.</summary>
        public static double Deg(double meters) => meters / R * 180.0 / Math.PI;

        public static NodeSpec Node(double lat, double lon, double depth = 95, double width = 120, double height = 60,
            NodeFeature feature = NodeFeature.None, double skylightRadius = 0) =>
            new NodeSpec { Lat = lat, Lon = lon, Depth = depth, Width = width, Height = height, Feature = feature, SkylightRadius = skylightRadius };

        /// <summary>A straight chain of nodes along the equator (or another latitude), one edge between each pair.</summary>
        public static SiteSpec Line(string name, double lat, double lon0, double spacingMeters, int nodes, double sinuosity = 0,
            Func<int, NodeSpec, NodeSpec> tweak = null)
        {
            var site = new SiteSpec { Name = name, IsNamed = true };
            for (int i = 0; i < nodes; i++)
            {
                NodeSpec n = Node(lat, lon0 + Deg(spacingMeters * i));
                site.Nodes.Add(tweak == null ? n : tweak(i, n));
            }

            for (int i = 1; i < nodes; i++)
            {
                site.Edges.Add(new EdgeSpec { From = i - 1, To = i, Sinuosity = sinuosity, Roughness = 0.05 });
            }

            return site;
        }
    }
}
