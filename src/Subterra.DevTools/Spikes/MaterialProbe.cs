using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Subterra.DevTools
{
    /// <summary>Writes the textures and colours of a body's terrain material (materials.txt), to reuse them for cave rock.</summary>
    [StepProvider]
    internal static class MaterialProbe
    {
        internal static void Register()
        {
            StepRegistry.RegisterSpike("MaterialProbe", Probe);
        }

        // spike name = MaterialProbe body = Mun
        private static IEnumerator Probe(ConfigNode node, TestContext ctx)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(node.Arg("body", "Mun")) ?? throw new ArgumentException("unknown body");
            var report = new StringBuilder();
            foreach (var (label, material) in new[] { ("surfaceMaterial", body.pqsController.surfaceMaterial), ("fallbackMaterial", body.pqsController.fallbackMaterial), ("lowQualitySurfaceMaterial", body.pqsController.lowQualitySurfaceMaterial) })
            {
                if (material == null)
                {
                    report.AppendLine($"{label}: none");
                    continue;
                }

                report.AppendLine($"{label}: {material.name}, shader {material.shader.name}");
                foreach (string prop in material.GetTexturePropertyNames())
                {
                    Texture t = material.GetTexture(prop);
                    report.AppendLine(t == null ? $"  {prop}: -" : $"  {prop}: {t.name} {t.width}x{t.height} scale {material.GetTextureScale(prop)}");
                }

                foreach (string colour in new[] { "_Color", "_lowColor", "_midColor", "_highColor", "_steepColor", "_lowColorMultiplier" })
                {
                    if (material.HasProperty(colour))
                    {
                        report.AppendLine($"  {colour}: {(colour.EndsWith("Multiplier") ? material.GetFloat(colour).ToString() : material.GetColor(colour).ToString())}");
                    }
                }
            }

            File.WriteAllText(Path.Combine(ctx.ResultsDir, "materials.txt"), report.ToString());
            ctx.Pass("materials.txt");
            yield break;
        }
    }
}
