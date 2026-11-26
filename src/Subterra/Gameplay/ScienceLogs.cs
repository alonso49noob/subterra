using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using Subterra.Core.Gameplay;
using Subterra.Core.Meshing;
using Subterra.Core.Model;

namespace Subterra
{
    /// <summary>
    /// The text of an experiment's result in Subterra's biomes, and of a core sample (Lucas: science logs for each
    /// underground biome, and logs with more to them, information and lore). KSP picks a result at random among every
    /// RESULTS key its subject contains, so cave subjects got the body's generic lines; these come from the localization:
    /// <list type="bullet">
    /// <item><c>#Subterra_Log_&lt;experiment&gt;_&lt;zone&gt;</c> (and <c>_2</c>, <c>_3</c> variants), else <c>#Subterra_Log_Any_&lt;zone&gt;</c>
    /// for experiments without their own (mods'); <c>&lt;&lt;1&gt;&gt;</c> is the body.</item>
    /// <item>In a named site, its lore (<c>#Subterra_Lore_&lt;site&gt;</c>) after the reports a kerbal or a sample gives.</item>
    /// <item>A core sample: <c>#Subterra_Exp_Core_&lt;layer&gt;</c>, else <c>#Subterra_Exp_Core_Kind_&lt;rock kind&gt;</c>, then a line
    /// of measurements (depth, hardness, density, temperature, what it holds).</item>
    /// </list>
    /// </summary>
    public static class ScienceLogs
    {
        // Reports in which a site's lore is told (what a kerbal sees or a sample shows; not the instruments' readings).
        private static readonly HashSet<string> LoreExperiments = new HashSet<string>
        {
            "crewReport", "evaReport", "surfaceSample", "evaScience",
        };

        private static readonly ExperimentSituations[] Situations = (ExperimentSituations[])Enum.GetValues(typeof(ExperimentSituations));
        private static readonly Random Dice = new Random();

        /// <summary>The result text for a subject id ("experiment@BodySituationBiome"), or null to leave it to KSP.</summary>
        public static string For(string subjectId)
        {
            int at = subjectId?.IndexOf('@') ?? -1;
            if (at <= 0 || !Parse(subjectId.Substring(at + 1), out CelestialBody body, out string biome)
                || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                return null;
            }

            string experiment = subjectId.Substring(0, at);
            if (experiment == ModuleSubterraCoreDrill.ExperimentId)
            {
                return Core(runtime, body, biome);
            }

            return BiomeIds.IsSubterra(biome, runtime.Spec) ? Cave(runtime, body, experiment, biome) : null;
        }

        // "KerbinSrfLandedTubeGallery": the body with the longest name followed by a situation, and what is left.
        private static bool Parse(string where, out CelestialBody body, out string biome)
        {
            body = null;
            biome = null;
            if (FlightGlobals.Bodies == null)
            {
                return false;
            }

            foreach (CelestialBody b in FlightGlobals.Bodies)
            {
                if (b == null || !where.StartsWith(b.name, StringComparison.Ordinal) || (body != null && body.name.Length >= b.name.Length))
                {
                    continue;
                }

                string rest = where.Substring(b.name.Length);
                foreach (ExperimentSituations s in Situations)
                {
                    string name = s.ToString();
                    if (rest.StartsWith(name, StringComparison.Ordinal))
                    {
                        body = b;
                        biome = rest.Substring(name.Length);
                        break;
                    }
                }
            }

            return body != null && !string.IsNullOrEmpty(biome);
        }

        private static string BodyName(CelestialBody body) => body.displayName.LocalizeRemoveGender();

        private static string Cave(BodyRuntime runtime, CelestialBody body, string experiment, string biome)
        {
            SiteSpec site = runtime.Spec.Sites.Find(s => s.Name == biome);
            ZoneKind zone = site == null ? ParseZone(biome) : SiteZone(body, site);
            string key = Pick("#Subterra_Log_" + experiment + "_" + zone) ?? Pick("#Subterra_Log_Any_" + zone);
            if (key == null)
            {
                return null;
            }

            string text = Localizer.Format(key, BodyName(body));
            if (site != null && LoreExperiments.Contains(experiment) && Localizer.TryGetStringByTag("#Subterra_Lore_" + site.Name, out _))
            {
                text += "\n\n" + Localizer.Format("#Subterra_Lore_" + site.Name, BodyName(body));
            }

            return text;
        }

        private static ZoneKind ParseZone(string biome) =>
            Enum.TryParse(biome, out ZoneKind kind) && Enum.IsDefined(typeof(ZoneKind), kind) ? kind : ZoneKind.TubeGallery;

        // A named site's part the result is from: where the active vessel is in it, else what the site is mostly.
        private static ZoneKind SiteZone(CelestialBody body, SiteSpec site)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null && v.mainBody == body)
            {
                UndergroundState state = UndergroundTracker.For(v);
                if (state.Inside && state.Zone.SiteName == site.Name && state.Zone.Kind != ZoneKind.None)
                {
                    return state.Zone.Kind;
                }
            }

            NodeFeature feature = site.Nodes.Count > 0 ? site.Nodes[0].Feature : NodeFeature.None;
            switch (feature)
            {
                case NodeFeature.Geode: return ZoneKind.Geode;
                case NodeFeature.Lake: return ZoneKind.SubglacialLake;
                case NodeFeature.Chamber: return ZoneKind.TubeChamber;
                default: return site.Nodes.Any(n => n.Feature == NodeFeature.Chamber) ? ZoneKind.TubeChamber : ZoneKind.TubeGallery;
            }
        }

        // The key, or one of its variants (_2, _3...) at random, among those the localization has; null if none.
        private static string Pick(string key)
        {
            var found = new List<string>(3);
            for (int i = 1; i <= 5; i++)
            {
                string k = i == 1 ? key : key + "_" + i.ToString(CultureInfo.InvariantCulture);
                if (Localizer.TryGetStringByTag(k, out _))
                {
                    found.Add(k);
                }
                else if (i > 1)
                {
                    break;
                }
            }

            return found.Count == 0 ? null : found[Dice.Next(found.Count)];
        }

        private static string Core(BodyRuntime runtime, CelestialBody body, string layerName)
        {
            LayerSpec layer = runtime.Strata.Layers.FirstOrDefault(l => l.Name == layerName);
            string key = Pick("#Subterra_Exp_Core_" + layerName);
            if (key == null)
            {
                RockKind kind = RockTextures.KindFor(layerName, layer?.Texture);
                key = Pick("#Subterra_Exp_Core_Kind_" + kind) ?? "#Subterra_Exp_CoreSample_Result";
            }

            string text = Localizer.Format(key, BodyName(body));
            if (layer == null)
            {
                return text;
            }

            // The measurements: where the layer lies, how hard and dense it is, how hot at its middle, and what it holds.
            double mid = (layer.Top + layer.Bottom) * 0.5;
            double? temp = runtime.Geotherm.Temperature(mid, layer.Where.Count > 0 && layer.Where[0] != "*" ? layer.Where[0] : null);
            string holds = layer.Yields.Count == 0 ? Localizer.Format("#Subterra_Log_Core_Nothing")
                : string.Join(", ", layer.Yields.Select(y => $"{y.Key} {(y.Value * 100).ToString("0.#", CultureInfo.InvariantCulture)}%"));
            text += "\n\n" + Localizer.Format("#Subterra_Log_Core_Data",
                layer.Top.ToString("N0", CultureInfo.InvariantCulture), layer.Bottom.ToString("N0", CultureInfo.InvariantCulture),
                layer.Hardness.ToString("0.00", CultureInfo.InvariantCulture), layer.Density.ToString("0.0", CultureInfo.InvariantCulture),
                temp.HasValue ? temp.Value.ToString("N0", CultureInfo.InvariantCulture) : "-", holds);
            return text;
        }
    }
}
