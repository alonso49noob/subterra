using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// Generates the procedural networks of every cell of a body (KSP terrain and biomes, like CellScheduler)
    /// and counts them, to set the fields' density: writes networks.txt. <c>scale</c> multiplies every
    /// field's networksPer10k for this count only (to try a density before writing it in the cfg).
    /// </summary>
    [StepProvider]
    internal static class NetworkCounter
    {
        private const int GridSize = 64;

        internal static void Register()
        {
            StepRegistry.RegisterSpike("NetworkCounter", Count);
        }

        // spike name = NetworkCounter body = Mun scale = 1
        private static IEnumerator Count(ConfigNode node, TestContext ctx)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(node.Arg("body", "Mun")) ?? throw new ArgumentException("unknown body");
            if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                ctx.Fail("no Subterra body " + body.bodyName);
                yield break;
            }

            double scale = node.ArgDouble("scale", 1);
            List<FieldSpec> fields = runtime.Spec.Fields.Select(f => Scaled(f, scale)).ToList();
            CellGrid grid = runtime.Catalog.Grid;
            var perField = new Dictionary<string, int>();
            int total = 0, skylights = 0, full = 0, hidden = 0;
            float frameStart = Time.realtimeSinceStartup;
            foreach (CellId cell in grid.All())
            {
                var rect = grid.GridRect(cell, FieldGenerator.CellMargin);
                IReadOnlyList<(double lat, double lon)> points = grid.SampleGrid(cell, GridSize, FieldGenerator.CellMargin);
                var radius = new double[points.Count];
                var biome = new string[points.Count];
                for (int i = 0; i < points.Count; i++)
                {
                    radius[i] = KspSampler.SurfaceRadius(body, points[i].lat, points[i].lon);
                    biome[i] = KspSampler.Biome(body, points[i].lat, points[i].lon);
                    if (Time.realtimeSinceStartup - frameStart > 0.05f)
                    {
                        yield return null;
                        frameStart = Time.realtimeSinceStartup;
                    }
                }

                var samples = new GridSamples(rect.latMin, rect.latMax, rect.lonMin, rect.lonMax, GridSize, radius, biome);
                List<SiteSpec> sites = FieldGenerator.Generate(fields, body.bodyName, runtime.Spec.IdPrefix, grid, cell, samples, samples);
                total += sites.Count;
                if (sites.Count >= FieldGenerator.MaxNetworksPerCell)
                {
                    full++;
                }

                foreach (SiteSpec site in sites)
                {
                    skylights += site.Nodes.Count(n => n.Feature == NodeFeature.Skylight);
                    if (!site.Nodes.Any(n => n.Feature == NodeFeature.Skylight))
                    {
                        hidden++;
                    }

                    string field = site.Field ?? "?";
                    perField[field] = (perField.TryGetValue(field, out int n) ? n : 0) + 1;
                }
            }

            var report = new StringBuilder();
            report.AppendLine(Invariant($"{body.bodyName}: {total} procedural networks in {grid.CellCount} cells (scale {scale}), {skylights} skylights, {hidden} networks without any, {full} cells at the cap of {FieldGenerator.MaxNetworksPerCell}"));
            foreach (var pair in perField.OrderBy(p => p.Key))
            {
                report.AppendLine(Invariant($"  {pair.Key}: {pair.Value}"));
            }

            File.WriteAllText(Path.Combine(ctx.ResultsDir, "networks.txt"), report.ToString());
            ctx.Pass(report.ToString().Split('\n')[0].Trim());
        }

        private static FieldSpec Scaled(FieldSpec f, double scale) => new FieldSpec
        {
            Name = f.Name,
            Biomes = new List<string>(f.Biomes),
            NetworksPer10k = f.NetworksPer10k * scale,
            Width = f.Width,
            Length = f.Length,
            RoofDepth = f.RoofDepth,
            SkylightSpacing = f.SkylightSpacing,
            BranchChance = f.BranchChance,
            ChamberChance = f.ChamberChance,
            HiddenChance = f.HiddenChance,
            Seed = f.Seed,
        };
    }
}
