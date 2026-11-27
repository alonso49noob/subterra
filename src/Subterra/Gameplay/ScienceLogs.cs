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
            ZoneKind zone = site != null ? SiteZone(body, site) : biome.StartsWith(BiomeIds.LayerPrefix, StringComparison.Ordinal) ? ZoneKind.Excavation : ParseZone(biome);
            string key = Pick("#Subterra_Log_" + experiment + "_" + zone) ?? Pick("#Subterra_Log_Any_" + zone);
            if (key == null)
            {
                return null;
            }

            // Lucas: clearer, with more information, easier to read. A heading (the instrument, the place), what it read here
            // (the real values where the craft is), what that means, and the place's story in a named site.
            var text = new System.Text.StringBuilder();
            text.Append(Heading(experiment, body, biome));
            List<string> reading = Reading(runtime, body, experiment);
            if (reading.Count > 0)
            {
                text.Append("\n\n").Append(Localizer.Format("#Subterra_Log_Reading"));
                foreach (string line in reading)
                {
                    text.Append("\n  • ").Append(line);
                }
            }

            text.Append("\n\n").Append(Localizer.Format("#Subterra_Log_Meaning")).Append('\n').Append(Localizer.Format(key, BodyName(body)));
            // Every body's Core Chamber shares one story (told with the body's name).
            string lore = site == null ? null
                : Localizer.TryGetStringByTag("#Subterra_Lore_" + site.Name, out _) ? "#Subterra_Lore_" + site.Name
                : site.Name.EndsWith("CoreChamber", StringComparison.Ordinal) ? "#Subterra_Lore_CoreChamber" : null;
            if (lore != null && LoreExperiments.Contains(experiment))
            {
                text.Append("\n\n").Append(Localizer.Format("#Subterra_Log_Story")).Append('\n').Append(Localizer.Format(lore, BodyName(body)));
            }

            return text.ToString();
        }

        // "Thermometer · Tube gallery, Kerbin".
        private static string Heading(string experiment, CelestialBody body, string biome)
        {
            string instrument = ResearchAndDevelopment.GetExperiment(experiment)?.experimentTitle ?? experiment;
            return Localizer.Format("#Subterra_Log_Heading", instrument, SciencePatches.DisplayName(body, biome), BodyName(body));
        }

        private static string N(double v, string format = "N0") => v.ToString(format, CultureInfo.InvariantCulture);

        // What the instrument read where the active vessel is (if it is on this body): each experiment its own lines, all of
        // them where and in what rock.
        private static List<string> Reading(BodyRuntime runtime, CelestialBody body, string experiment)
        {
            var lines = new List<string>();
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.mainBody != body || body.pqsController == null)
            {
                return lines;
            }

            Vector3d at = v.CoMD;
            double lat = body.GetLatitude(at), lon = body.GetLongitude(at);
            string biome = KspSampler.Biome(body, lat, lon);
            double depth = Math.Max(0, KspSampler.SurfaceRadius(body, lat, lon) - (at - body.position).magnitude);
            UndergroundState state = UndergroundTracker.For(v);
            LayerSpec layer = runtime.Strata.LayerAt(depth, biome);
            string layerName = layer == null ? "-" : string.IsNullOrEmpty(layer.DisplayName) ? layer.Name : Localizer.Format(layer.DisplayName);
            lines.Add(Localizer.Format("#Subterra_Log_Read_Where", N(depth), layerName));

            double? rock = state.Inside ? state.RockTemp : runtime.Geotherm.Temperature(depth, biome);
            SubterraWaterModule water = SubterraWaterModule.Of(v);
            bool wet = water != null && water.AnyWet;
            bool ocean = Underground.OceanAt(body, at) is Core.Water.WaterSample sea && sea.Under;

            void Temperature()
            {
                if (!rock.HasValue)
                {
                    return;
                }

                double surface = runtime.Geotherm.Temperature(0, biome) ?? rock.Value;
                string gradient = depth > 20 ? N(Math.Max(0, rock.Value - surface) / depth * 1000, "0.#") : "-";
                lines.Add(Localizer.Format("#Subterra_Log_Read_Temp", N(rock.Value), N(rock.Value - 273.15), gradient));
            }

            void Pressure()
            {
                double gas = Math.Max(0, v.staticPressurekPa), fluid = gas;
                if (water != null && water.PressureOn(v.rootPart, out double kPa))
                {
                    fluid = Math.Max(gas, kPa);
                }

                double rockKPa = ocean ? 0 : InteriorGravityPatch.RockKPa(body, at);
                double total = fluid + rockKPa;
                lines.Add(Localizer.Format("#Subterra_Log_Read_Pressure", N(total, "0.##"), N(total / 101.325, "0.###"),
                    N(gas, "0.##"), N(fluid - gas, "0.##"), N(rockKPa, "0.##")));
            }

            void Gravity()
            {
                double g0 = body.GeeASL * PhysicsGlobals.GravitationalAcceleration;
                double factor = InteriorGravityPatch.Factor(body, at);
                lines.Add(Localizer.Format("#Subterra_Log_Read_Gravity", N(g0 * Math.Min(1, factor), "0.00"), N(Math.Min(1, factor) * 100, "0")));
            }

            void Rock(bool holds)
            {
                if (layer == null)
                {
                    return;
                }

                // A rough speed of sound in the rock, from its hardness (loose regolith a few hundred m/s, hard rock ~6 km/s).
                double vp = 300 + 7500 * layer.Hardness * layer.Hardness;
                string what = holds
                    ? layer.Yields.Count == 0 ? Localizer.Format("#Subterra_Log_Core_Nothing")
                        : string.Join(", ", layer.Yields.Select(y => $"{y.Key} {(y.Value * 100).ToString("0.#", CultureInfo.InvariantCulture)}%"))
                    : null;
                lines.Add(holds
                    ? Localizer.Format("#Subterra_Log_Read_Sample", N(layer.Hardness, "0.00"), N(layer.Density, "0.0"), what)
                    : Localizer.Format("#Subterra_Log_Read_Seismic", N(vp), N(layer.Density, "0.0"), N(layer.Hardness, "0.00")));
            }

            switch (experiment)
            {
                case "temperatureScan":
                    Temperature();
                    break;
                case "barometerScan":
                    Pressure();
                    break;
                case "gravityScan":
                    Gravity();
                    break;
                case "seismicScan":
                    Rock(false);
                    break;
                case "surfaceSample":
                case "evaScience":
                    Rock(true);
                    Temperature();
                    break;
                case "crewReport":
                case "evaReport":
                    Temperature();
                    Pressure();
                    lines.Add(Localizer.Format("#Subterra_Log_Read_Sky", N(state.Inside ? state.Sky * 100 : 0, "0.#")));
                    break;
                default:
                    Temperature();
                    break;
            }

            if (wet && water.DeepestDepth > 0)
            {
                lines.Add(Localizer.Format("#Subterra_Log_Read_Water", N(water.DeepestDepth, "0.#")));
            }

            return lines;
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

            string meaning = Localizer.Format(key, BodyName(body));
            if (layer == null)
            {
                return meaning;
            }

            // Lucas: more information, easier to read. A heading, the measurements as a list (where the layer lies, how hard
            // and dense, how hot at its top and bottom, the weight of the rock over its middle, how fast a drill gets through
            // it, what it holds), then what the core shows.
            string shown = string.IsNullOrEmpty(layer.DisplayName) ? layer.Name : Localizer.Format(layer.DisplayName);
            string biome = layer.Where.Count > 0 && layer.Where[0] != "*" ? layer.Where[0] : null;
            double? top = runtime.Geotherm.Temperature(layer.Top, biome), bottom = runtime.Geotherm.Temperature(Math.Min(layer.Bottom, body.Radius), biome);
            string holds = layer.Yields.Count == 0 ? Localizer.Format("#Subterra_Log_Core_Nothing")
                : string.Join(", ", layer.Yields.Select(y => $"{y.Key} {(y.Value * 100).ToString("0.#", CultureInfo.InvariantCulture)}%"));
            double mid = (layer.Top + Math.Min(layer.Bottom, body.Radius)) * 0.5;
            double weight = Subterra.Core.Environment.Lithostatic.PressureKPa(runtime.Strata.Spans(biome), mid,
                body.GeeASL * PhysicsGlobals.GravitationalAcceleration, body.Radius) * Settings.Current.RockPressureScale;
            double rate = Subterra.Core.Excavation.DrillMath.Rate(1, layer.Hardness, null, 0);
            var text = new System.Text.StringBuilder();
            text.Append(Localizer.Format("#Subterra_Log_CoreHeading", shown, BodyName(body)));
            text.Append("\n\n").Append(Localizer.Format("#Subterra_Log_Reading"));
            text.Append("\n  • ").Append(Localizer.Format("#Subterra_Log_Core_Where", N(layer.Top), N(Math.Min(layer.Bottom, body.Radius)), N(Math.Min(layer.Bottom, body.Radius) - layer.Top)));
            text.Append("\n  • ").Append(Localizer.Format("#Subterra_Log_Core_Rock", N(layer.Hardness, "0.00"), N(layer.Density, "0.0"), N(rate * 100, "0")));
            text.Append("\n  • ").Append(Localizer.Format("#Subterra_Log_Core_Heat", top.HasValue ? N(top.Value) : "-", bottom.HasValue ? N(bottom.Value) : "-"));
            text.Append("\n  • ").Append(Localizer.Format("#Subterra_Log_Core_Weight", N(weight, "0.#")));
            text.Append("\n  • ").Append(Localizer.Format("#Subterra_Log_Core_Holds", holds));
            text.Append("\n\n").Append(Localizer.Format("#Subterra_Log_Meaning")).Append('\n').Append(meaning);
            return text.ToString();
        }
    }
}
