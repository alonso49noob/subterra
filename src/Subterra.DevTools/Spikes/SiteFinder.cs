using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// Task 36: lists the body's biomes and, per wanted site, straight tracks of the wanted length that
    /// stay in the target biome with gentle slopes (max &lt; 3° between 200 m samples) and little height
    /// spread. Writes sitefinder.txt.
    /// </summary>
    [StepProvider]
    internal static class SiteFinder
    {
        private const double Step = 200;

        internal static void Register()
        {
            StepRegistry.RegisterSpike("SiteFinder", Find);
        }

        // spike name = SiteFinder body = Mun — sites as SITE { name biome length latMin latMax } children of the STEP.
        private static IEnumerator Find(ConfigNode node, TestContext ctx)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(node.Arg("body", "Mun")) ?? throw new ArgumentException("unknown body");
            var report = new StringBuilder();
            report.AppendLine("Biomes: " + string.Join(", ", body.BiomeMap.Attributes.Select(a => a.name)));
            foreach (ConfigNode site in node.GetNodes("SITE"))
            {
                string name = site.RequireArg("name");
                var biomes = site.RequireArg("biome").Split(',').Select(s => s.Trim()).ToList();
                double length = site.ArgDouble("length", 2000);
                double latMin = site.ArgDouble("latMin", -85), latMax = site.ArgDouble("latMax", 85);
                double slopeLimit = site.ArgDouble("maxSlope", 3);
                int inBiome = 0;
                double bestSlope = double.MaxValue, bestReach = 0;
                var candidates = new List<(double score, double lat, double lon, double heading, double maxSlope, double spread, double meanAlt)>();
                var rng = new System.Random(name.GetHashCode() & 0x7fffffff);
                int tries = 0;
                float frameStart = Time.realtimeSinceStartup;
                while (tries < 4000)
                {
                    tries++;
                    double lat = latMin + rng.NextDouble() * (latMax - latMin);
                    double lon = rng.NextDouble() * 360 - 180;
                    if (!biomes.Contains(KspSampler.Biome(body, lat, lon)))
                    {
                        continue;
                    }

                    inBiome++;
                    for (int h = 0; h < 8; h++)
                    {
                        double heading = h * 45;
                        bool ok = Track(body, biomes, lat, lon, heading, length, slopeLimit, out double maxSlope, out double spread, out double meanAlt, out double reach);
                        bestReach = Math.Max(bestReach, reach);
                        if (reach >= length - Step)
                        {
                            bestSlope = Math.Min(bestSlope, maxSlope);
                        }

                        if (ok)
                        {
                            candidates.Add((spread + maxSlope * 2, lat, lon, heading, maxSlope, spread, meanAlt));
                        }
                    }

                    if (Time.realtimeSinceStartup - frameStart > 0.05f)
                    {
                        yield return null;
                        frameStart = Time.realtimeSinceStartup;
                    }
                }

                report.AppendLine(Invariant($"{name} ({string.Join("/", biomes)}, {length} m, max slope {slopeLimit}°): {candidates.Count} candidates from {tries} starts ({inBiome} in the biome; longest in-biome track {bestReach:F0} m; gentlest full-length track {(bestSlope == double.MaxValue ? "none" : bestSlope.ToString("F2") + "°")})"));
                foreach (var c in candidates.OrderBy(c => c.score).Take(5))
                {
                    BodyFrame.ToLatLon(Along(c.lat, c.lon, c.heading, length, body.Radius), out double endLat, out double endLon);
                    report.AppendLine(Invariant($"  start {c.lat:F4}, {c.lon:F4} → end {endLat:F4}, {endLon:F4} heading {c.heading} | max slope {c.maxSlope:F2}°, height spread {c.spread:F1} m, mean altitude {c.meanAlt:F0} m"));
                }
            }

            // One file per body (a scenario may search several).
            string file = body.bodyName == "Mun" ? "sitefinder.txt" : $"sitefinder-{body.bodyName}.txt";
            File.WriteAllText(Path.Combine(ctx.ResultsDir, file), report.ToString());
            ctx.Pass(file);
        }

        private static bool Track(CelestialBody body, List<string> biomes, double lat, double lon, double heading, double length, double slopeLimit,
            out double maxSlope, out double spread, out double meanAlt, out double reach)
        {
            maxSlope = 0;
            spread = 0;
            meanAlt = 0;
            reach = 0;
            int n = (int)(length / Step) + 1;
            var heights = new double[n];
            for (int i = 0; i < n; i++)
            {
                Vec3d p = Along(lat, lon, heading, i * Step, body.Radius);
                BodyFrame.ToLatLon(p, out double la, out double lo);
                if (!biomes.Contains(KspSampler.Biome(body, la, lo)))
                {
                    return false;
                }

                reach = i * Step;
                heights[i] = KspSampler.SurfaceRadius(body, la, lo) - body.Radius;
                if (i > 0)
                {
                    double slope = Math.Atan(Math.Abs(heights[i] - heights[i - 1]) / Step) * 180 / Math.PI;
                    maxSlope = Math.Max(maxSlope, slope);
                }
            }

            if (maxSlope >= slopeLimit)
            {
                return false;
            }

            meanAlt = heights.Average();
            double mean = meanAlt;
            spread = Math.Sqrt(heights.Average(h => (h - mean) * (h - mean)));
            return true;
        }

        // Unit direction after travelling `distance` m from (lat, lon) along the great circle with that heading (0 = north, 90 = east).
        private static Vec3d Along(double lat, double lon, double heading, double distance, double radius)
        {
            Vec3d p = BodyFrame.Direction(lat, lon);
            Vec3d east = BodyFrame.Direction(0, lon + 90);
            Vec3d north = Vec3d.Cross(p, east).Normalized;
            if (Vec3d.Dot(north, new Vec3d(0, 1, 0)) < 0)
            {
                north = -north;
            }

            east = Vec3d.Cross(north, p).Normalized;
            double h = heading * Math.PI / 180, a = distance / radius;
            Vec3d dir = north * Math.Cos(h) + east * Math.Sin(h);
            return (p * Math.Cos(a) + dir * Math.Sin(a)).Normalized;
        }
    }
}
