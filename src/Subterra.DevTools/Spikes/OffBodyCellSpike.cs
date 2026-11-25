using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// M0 spike (radar Task 4): does KspSampler.Fill give the same heights and biomes for a body that is not the active
    /// one? 100 fixed points of the Mun are sampled while the vessel is at the Mun (phase = near) and again from Kerbin's
    /// orbit (phase = far), and the two runs are compared. Throwaway code.
    /// </summary>
    [StepProvider]
    internal static class OffBodyCellSpike
    {
        private const int Count = 100;
        // The first sampling of each body is the reference the later ones are compared with.
        private static readonly Dictionary<string, double[]> ReferenceHeights = new Dictionary<string, double[]>();
        private static readonly Dictionary<string, string[]> ReferenceBiomes = new Dictionary<string, string[]>();

        internal static void Register()
        {
            StepRegistry.RegisterSpike("OffBodyCell", Run);
        }

        // spike name = OffBodyCell body = Mun phase = <label> ref = false — samples the 100 points through KspSampler.Fill.
        // The first sampling of a body (or any with ref = true) becomes its reference; later ones are compared with it.
        private static IEnumerator Run(ConfigNode node, TestContext ctx)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(node.Arg("body", "Mun")) ?? throw new ArgumentException("unknown body");
            string phase = node.RequireArg("phase");
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");

            var points = new List<(double lat, double lon)>();
            var random = new System.Random(1234);
            for (int i = 0; i < Count; i++)
            {
                // Uniform over the sphere: latitude from the arcsine of a uniform number.
                points.Add((Math.Asin(2 * random.NextDouble() - 1) * 180 / Math.PI, 360 * random.NextDouble() - 180));
            }

            var heights = new double[Count];
            var biomes = new string[Count];
            int errors = 0;
            string firstError = null;
            var watch = Stopwatch.StartNew();
            IEnumerator fill = KspSampler.Fill(body, points, (i, radius, biome) =>
            {
                heights[i] = radius;
                biomes[i] = biome;
            }, 1000f);
            while (true)
            {
                bool more;
                try
                {
                    more = fill.MoveNext();
                }
                catch (Exception e)
                {
                    errors++;
                    firstError = e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace.Split((char)10)[0];
                    break;
                }

                if (!more)
                {
                    break;
                }

                yield return fill.Current;
            }

            watch.Stop();
            string state = Invariant(
                $"active vessel at {vessel.mainBody.bodyName} ({vessel.situation}), {body.bodyName} pqs active {body.pqsController.isActive}, pqs sphere active {body.pqsController.gameObject.activeInHierarchy}, maxLevel {body.pqsController.maxLevel}, BiomeMap {(body.BiomeMap == null ? "null" : "present")}");
            var report = new StringBuilder();
            report.AppendLine($"phase {phase} ({body.bodyName}): {state}");
            report.AppendLine(Invariant($"  {Count} points in {watch.Elapsed.TotalMilliseconds:F1} ms ({watch.Elapsed.TotalMilliseconds / Count:F3} ms each), Fill errors {errors}{(firstError == null ? "" : ": " + firstError)}"));
            report.AppendLine(Invariant($"  heights: min {heights.Min():F1} max {heights.Max():F1} (radius {body.Radius:F0}), biomes: {string.Join(",", biomes.Where(b => b != null).Distinct().ToArray())}, null biomes {biomes.Count(b => b == null)}"));

            string key = body.bodyName;
            if (node.ArgBool("ref", false) || !ReferenceHeights.ContainsKey(key))
            {
                ReferenceHeights[key] = heights;
                ReferenceBiomes[key] = biomes;
                Finish(report, ctx, errors == 0, phase + ": reference for " + key + " sampled");
                yield break;
            }

            double[] nearHeights = ReferenceHeights[key];
            string[] nearBiomes = ReferenceBiomes[key];
            double maxDiff = 0;
            int worst = 0;
            int biomeDiffs = 0;
            var different = new List<string>();
            for (int i = 0; i < Count; i++)
            {
                double diff = Math.Abs(heights[i] - nearHeights[i]);
                if (diff > maxDiff)
                {
                    maxDiff = diff;
                    worst = i;
                }

                if (biomes[i] != nearBiomes[i])
                {
                    biomeDiffs++;
                    different.Add(Invariant($"#{i} ({points[i].lat:F1}, {points[i].lon:F1}) reference '{nearBiomes[i]}' this '{biomes[i]}'"));
                }
            }

            report.AppendLine(Invariant($"  compared with the reference: max height difference {maxDiff:F4} m (point {worst}: reference {nearHeights[worst]:F3}, this {heights[worst]:F3}), different biomes {biomeDiffs} of {Count}"));
            foreach (string d in different.Take(10))
            {
                report.AppendLine("    " + d);
            }

            bool same = errors == 0 && maxDiff < 0.01 && biomeDiffs == 0;
            Finish(report, ctx, same, Invariant($"{phase}: {key} max height difference {maxDiff:F4} m, {biomeDiffs} different biomes of {Count}"));
        }

        private static void Finish(StringBuilder report, TestContext ctx, bool ok, string summary)
        {
            Log.Info("OffBodyCellSpike:\n" + report);
            File.AppendAllText(Path.Combine(ctx.ResultsDir, "offbody.txt"), report.ToString());
            if (ok)
            {
                ctx.Pass(summary);
            }
            else
            {
                ctx.Fail(summary + " (see offbody.txt)");
            }
        }
    }
}
