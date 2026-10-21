using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Subterra.Core.Model;

namespace Subterra.Core.Config
{
    /// <summary>
    /// Reads one <c>SUBTERRA_BODY</c> node. Bad structures are dropped (or bad values replaced by
    /// defaults) with a warning; the rest still loads. Returns a null value only when <c>body</c> is missing.
    /// </summary>
    public static class BodySpecParser
    {
        public const double MinWidth = 20;
        public const double MaxWidth = 500;
        public const double MinHeight = 5;
        public const double MaxHeight = 300;

        /// <summary>TUBE_FIELD limits (m): a trunk of at least one generator step, a real roof, skylights not crowding.</summary>
        public const double MinFieldLength = 400;
        public const double MinRoofDepth = 1;
        public const double MaxRoofDepth = 5000;
        public const double MinSkylightSpacing = 500;
        public const double MaxFieldDistance = 100000;

        public static ParseResult<BodySpec> Parse(CfgNode node)
        {
            var warnings = new List<string>();
            string body = node.GetValue("body")?.Trim();
            if (string.IsNullOrEmpty(body))
            {
                warnings.Add("SUBTERRA_BODY without 'body' skipped");
                return new ParseResult<BodySpec>(null, warnings);
            }

            var r = new Reader("SUBTERRA_BODY " + body, warnings);
            var spec = new BodySpec { Body = body };
            spec.Enabled = r.Bool(node, "enabled", true);
            string prefix = node.GetValue("idPrefix")?.Trim();
            spec.IdPrefix = string.IsNullOrEmpty(prefix) ? DefaultPrefix(body) : prefix;
            spec.CaveScienceMultiplier = r.Double(node, "caveScienceMultiplier", 1.5);
            spec.SiteScienceMultiplier = r.Double(node, "siteScienceMultiplier", 2.0);

            CfgNode geotherm = node.GetNode("GEOTHERM");
            if (geotherm != null)
            {
                spec.Geotherm = ParseGeotherm(geotherm, r.Sub("GEOTHERM"));
            }

            foreach (CfgNode strata in node.GetNodes("STRATA"))
            {
                foreach (CfgNode layer in strata.GetNodes("LAYER"))
                {
                    LayerSpec parsed = ParseLayer(layer, r);
                    if (parsed != null)
                    {
                        spec.Strata.Add(parsed);
                    }
                }
            }

            foreach (CfgNode site in node.GetNodes("TUBE_SITE"))
            {
                SiteSpec parsed = ParseSite(site, r);
                if (parsed != null)
                {
                    spec.Sites.Add(parsed);
                }
            }

            foreach (CfgNode field in node.GetNodes("TUBE_FIELD"))
            {
                FieldSpec parsed = ParseField(field, r);
                if (parsed != null)
                {
                    spec.Fields.Add(parsed);
                }
            }

            foreach (CfgNode scatter in node.GetNodes("CAVE_SCATTER"))
            {
                ScatterSpec parsed = ParseScatter(scatter, r);
                if (parsed != null)
                {
                    spec.Scatters.Add(parsed);
                }
            }

            foreach (CfgNode enrichment in node.GetNodes("ENRICHMENT"))
            {
                EnrichmentSpec parsed = ParseEnrichment(enrichment, r);
                if (parsed != null)
                {
                    spec.Enrichment.Add(parsed);
                }
            }

            return new ParseResult<BodySpec>(spec, warnings);
        }

        /// <summary>Default id prefix: the first two letters of the body name in upper case.</summary>
        public static string DefaultPrefix(string body)
        {
            string letters = new string(body.Where(char.IsLetter).ToArray());
            if (letters.Length == 0)
            {
                return "XX";
            }

            return letters.Substring(0, Math.Min(2, letters.Length)).ToUpperInvariant();
        }

        public static List<string> SplitList(string value)
        {
            var items = new List<string>();
            if (value == null)
            {
                return items;
            }

            foreach (string part in value.Split(','))
            {
                string item = part.Trim();
                if (item.Length > 0)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private static GeothermSpec ParseGeotherm(CfgNode node, Reader r)
        {
            return new GeothermSpec
            {
                SurfaceMeanTemp = r.Double(node, "surfaceMeanTemp", 250),
                DampingDepth = r.Double(node, "dampingDepth", 1.5),
            };
        }

        private static LayerSpec ParseLayer(CfgNode node, Reader parent)
        {
            string name = node.GetValue("name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                parent.Warn("LAYER without 'name' skipped");
                return null;
            }

            Reader r = parent.Sub("LAYER " + name);
            var layer = new LayerSpec
            {
                Name = name,
                DisplayName = node.GetValue("displayName")?.Trim(),
                Top = r.Double(node, "top", 0),
                Bottom = r.Double(node, "bottom", double.PositiveInfinity),
                Hardness = r.Double(node, "hardness", 0.5),
                Density = r.Double(node, "density", 2.5),
                Gradient = r.Double(node, "gradient", 0),
                Where = SplitList(node.GetValue("where")),
            };

            string tint = node.GetValue("tint");
            if (tint != null)
            {
                List<string> parts = SplitList(tint);
                var rgb = new float[3];
                bool ok = parts.Count == 3;
                for (int i = 0; ok && i < 3; i++)
                {
                    ok = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out rgb[i]);
                }

                if (ok)
                {
                    layer.Tint = rgb;
                }
                else
                {
                    r.Warn($"'tint' is not r, g, b: {tint}; using grey");
                }
            }

            foreach (CfgNode y in node.GetNodes("YIELD"))
            {
                string resource = y.GetValue("resource")?.Trim();
                double rate = r.Double(y, "rate", 0);
                if (string.IsNullOrEmpty(resource) || rate <= 0)
                {
                    r.Warn("YIELD needs a resource and a rate > 0; skipped");
                    continue;
                }

                layer.Yields[resource] = rate;
            }

            layer.GasChance = Math.Max(0, Math.Min(1, r.Double(node, "gasChance", 0)));
            layer.GasResource = node.GetValue("gasResource")?.Trim();

            if (!(layer.Bottom > layer.Top) || layer.Top < 0)
            {
                r.Warn($"needs 0 <= top < bottom (top {Inv(layer.Top)}, bottom {Inv(layer.Bottom)}); layer skipped");
                return null;
            }

            return layer;
        }

        private static SiteSpec ParseSite(CfgNode node, Reader parent)
        {
            string name = node.GetValue("name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                parent.Warn("TUBE_SITE without 'name' skipped");
                return null;
            }

            Reader r = parent.Sub(name);
            var site = new SiteSpec
            {
                Name = name,
                DisplayName = node.GetValue("displayName")?.Trim(),
                IsNamed = true,
            };

            int index = 0;
            foreach (CfgNode n in node.GetNodes("NODE"))
            {
                NodeSpec parsed = ParseNode(n, r.Sub("NODE " + index));
                if (parsed == null)
                {
                    r.Warn($"NODE {index} is invalid; site skipped");
                    return null;
                }

                site.Nodes.Add(parsed);
                index++;
            }

            if (site.Nodes.Count == 0)
            {
                r.Warn("no NODE; site skipped");
                return null;
            }

            foreach (CfgNode e in node.GetNodes("EDGE"))
            {
                Reader er = r.Sub("EDGE");
                var edge = new EdgeSpec
                {
                    From = er.Int(e, "from", -1),
                    To = er.Int(e, "to", -1),
                    Sinuosity = Clamp(er.Double(e, "sinuosity", 0.3), 0, 1, er, "sinuosity"),
                    Roughness = Clamp(er.Double(e, "roughness", 0.05), 0, 0.5, er, "roughness"),
                    BenchHeight = Clamp(er.Double(e, "bench", 0), 0, 0.6, er, "bench"),
                };

                if (edge.From < 0 || edge.From >= site.Nodes.Count || edge.To < 0 || edge.To >= site.Nodes.Count || edge.From == edge.To)
                {
                    r.Warn($"EDGE from = {edge.From} to = {edge.To} does not join two of its {site.Nodes.Count} nodes; site skipped");
                    return null;
                }

                site.Edges.Add(edge);
            }

            if (site.Nodes.Count > 1 && site.Edges.Count == 0)
            {
                r.Warn("several nodes but no EDGE; site skipped");
                return null;
            }

            return site;
        }

        private static NodeSpec ParseNode(CfgNode node, Reader r)
        {
            if (node.GetValue("lat") == null || node.GetValue("lon") == null)
            {
                r.Warn("needs lat and lon");
                return null;
            }

            var n = new NodeSpec
            {
                Lat = r.Double(node, "lat", double.NaN),
                Lon = r.Double(node, "lon", double.NaN),
                Depth = r.Double(node, "depth", 30),
                Width = r.Double(node, "width", 60),
                Height = r.Double(node, "height", 25),
                Feature = r.Enum(node, "feature", NodeFeature.None),
                SkylightRadius = r.Double(node, "skylightRadius", 0),
            };

            if (double.IsNaN(n.Lat) || double.IsNaN(n.Lon) || Math.Abs(n.Lat) > 90)
            {
                r.Warn("lat/lon out of range");
                return null;
            }

            n.Depth = Clamp(n.Depth, 0, 5000, r, "depth");
            n.Width = Clamp(n.Width, MinWidth, MaxWidth, r, "width");
            n.Height = Clamp(n.Height, MinHeight, MaxHeight, r, "height");
            if (n.Feature == NodeFeature.Skylight)
            {
                if (n.SkylightRadius <= 0)
                {
                    n.SkylightRadius = Math.Min(30, n.Width / 2);
                    r.Warn($"skylight without skylightRadius; using {Inv(n.SkylightRadius)} m");
                }

                n.SkylightRadius = Clamp(n.SkylightRadius, 5, n.Width / 2, r, "skylightRadius");
            }

            return n;
        }

        private static FieldSpec ParseField(CfgNode node, Reader parent)
        {
            string name = node.GetValue("name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                parent.Warn("TUBE_FIELD without 'name' skipped");
                return null;
            }

            Reader r = parent.Sub("TUBE_FIELD " + name);
            var field = new FieldSpec { Name = name, Biomes = SplitList(node.GetValue("biomes")) };
            field.NetworksPer10k = Math.Max(0, r.Double(node, "networksPer10k", field.NetworksPer10k));
            field.Width = r.Range(node, "width", field.Width);
            field.Width = new Range(Math.Max(MinWidth, field.Width.Min), Math.Min(MaxWidth, Math.Max(MinWidth, field.Width.Max)));
            field.Length = ClampRange(r.Range(node, "length", field.Length), field.Length, MinFieldLength, MaxFieldDistance, r, "length");
            field.RoofDepth = ClampRange(r.Range(node, "roofDepth", field.RoofDepth), field.RoofDepth, MinRoofDepth, MaxRoofDepth, r, "roofDepth");
            field.SkylightSpacing = ClampRange(r.Range(node, "skylightSpacing", field.SkylightSpacing), field.SkylightSpacing, MinSkylightSpacing, MaxFieldDistance, r, "skylightSpacing");
            field.BranchChance = Clamp(r.Double(node, "branchChance", field.BranchChance), 0, 1, r, "branchChance");
            field.ChamberChance = Clamp(r.Double(node, "chamberChance", field.ChamberChance), 0, 1, r, "chamberChance");
            field.HiddenChance = Clamp(r.Double(node, "hiddenChance", field.HiddenChance), 0, 1, r, "hiddenChance");
            field.Seed = r.ULong(node, "seed", field.Seed);
            if (field.Biomes.Count == 0)
            {
                r.Warn("no 'biomes'; field skipped");
                return null;
            }

            return field;
        }

        private static ScatterSpec ParseScatter(CfgNode node, Reader parent)
        {
            string name = node.GetValue("name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                parent.Warn("CAVE_SCATTER without 'name' skipped");
                return null;
            }

            Reader r = parent.Sub("CAVE_SCATTER " + name);
            var scatter = new ScatterSpec
            {
                Name = name,
                Models = SplitList(node.GetValue("model")),
                Placement = r.Enum(node, "placement", ScatterPlacement.Floor),
                Density = Math.Max(0, r.Double(node, "density", 0.5)),
                Scale = r.Range(node, "scale", new Range(1, 1)),
                ScalePower = Math.Max(0.1, r.Double(node, "scalePower", 1)),
                Sink = Clamp(r.Double(node, "sink", 0), 0, 1, r, "sink"),
                AlignToSurface = Clamp(r.Double(node, "alignToSurface", 0), 0, 1, r, "alignToSurface"),
                Collide = r.Bool(node, "collide", false),
            };

            foreach (string zone in SplitList(node.GetValue("zones")))
            {
                if (Enum.TryParse(zone, true, out ZoneKind kind) && kind != ZoneKind.None)
                {
                    scatter.Zones.Add(kind);
                }
                else
                {
                    r.Warn($"unknown zone '{zone}' ignored");
                }
            }

            CfgNode mask = node.GetNode("noiseMask");
            if (mask != null)
            {
                scatter.NoiseFrequency = r.Double(mask, "frequency", 0);
                scatter.NoiseThreshold = r.Double(mask, "threshold", 0);
            }

            Range lods = r.Range(node, "lodDistances", new Range(scatter.Lod0Distance, scatter.Lod1Distance));
            scatter.Lod0Distance = lods.Min;
            scatter.Lod1Distance = Math.Max(lods.Min, lods.Max);
            if (scatter.Models.Count == 0)
            {
                r.Warn("no 'model'; scatter skipped");
                return null;
            }

            return scatter;
        }

        private static EnrichmentSpec ParseEnrichment(CfgNode node, Reader parent)
        {
            string resource = node.GetValue("resource")?.Trim();
            if (string.IsNullOrEmpty(resource))
            {
                parent.Warn("ENRICHMENT without 'resource' skipped");
                return null;
            }

            Reader r = parent.Sub("ENRICHMENT " + resource);
            var enrichment = new EnrichmentSpec
            {
                Resource = resource,
                Layer = node.GetValue("layer")?.Trim(),
                Multiplier = Math.Max(0, r.Double(node, "multiplier", 1)),
                MinAbundance = Math.Max(0, r.Double(node, "minAbundance", 0)),
            };

            string zone = node.GetValue("zone")?.Trim();
            if (!string.IsNullOrEmpty(zone))
            {
                if (Enum.TryParse(zone, true, out ZoneKind kind) && kind != ZoneKind.None)
                {
                    enrichment.Zone = kind;
                }
                else
                {
                    r.Warn($"unknown zone '{zone}'; rule skipped");
                    return null;
                }
            }

            if (string.IsNullOrEmpty(enrichment.Layer))
            {
                enrichment.Layer = null;
            }

            return enrichment;
        }

        private static double Clamp(double value, double min, double max, Reader r, string key)
        {
            if (value < min || value > max)
            {
                double clamped = Math.Max(min, Math.Min(max, value));
                r.Warn($"'{key}' = {Inv(value)} outside [{Inv(min)}, {Inv(max)}]; using {Inv(clamped)}");
                return clamped;
            }

            return value;
        }

        // Both ends finite (else the default) and inside [min, max], with max >= min.
        private static Range ClampRange(Range value, Range fallback, double min, double max, Reader r, string key)
        {
            if (double.IsNaN(value.Min) || double.IsNaN(value.Max) || double.IsInfinity(value.Min) || double.IsInfinity(value.Max))
            {
                r.Warn($"'{key}' = {value} is not finite; using {fallback}");
                return fallback;
            }

            double low = Math.Max(min, Math.Min(max, value.Min));
            double high = Math.Max(low, Math.Min(max, value.Max));
            if (low != value.Min || high != value.Max)
            {
                var clamped = new Range(low, high);
                r.Warn($"'{key}' = {value} outside [{Inv(min)}, {Inv(max)}]; using {clamped}");
                return clamped;
            }

            return value;
        }

        private static string Inv(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Typed reads that warn (with the context) and fall back to the default on bad text.</summary>
        private sealed class Reader
        {
            private readonly string context;
            private readonly List<string> warnings;

            public Reader(string context, List<string> warnings)
            {
                this.context = context;
                this.warnings = warnings;
            }

            public Reader Sub(string name) => new Reader(context + " > " + name, warnings);

            public void Warn(string message) => warnings.Add(context + ": " + message);

            public double Double(CfgNode node, string key, double fallback)
            {
                string text = node.GetValue(key);
                if (text == null)
                {
                    return fallback;
                }

                if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && !double.IsNaN(value))
                {
                    return value;
                }

                Warn($"'{key}' is not a number: {text}; using {Inv(fallback)}");
                return fallback;
            }

            public int Int(CfgNode node, string key, int fallback)
            {
                string text = node.GetValue(key);
                if (text == null)
                {
                    return fallback;
                }

                if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    return value;
                }

                Warn($"'{key}' is not an integer: {text}; using {fallback}");
                return fallback;
            }

            public ulong ULong(CfgNode node, string key, ulong fallback)
            {
                string text = node.GetValue(key);
                if (text == null)
                {
                    return fallback;
                }

                if (ulong.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value))
                {
                    return value;
                }

                Warn($"'{key}' is not a whole number: {text}; using {fallback}");
                return fallback;
            }

            public bool Bool(CfgNode node, string key, bool fallback)
            {
                string text = node.GetValue(key);
                if (text == null)
                {
                    return fallback;
                }

                if (bool.TryParse(text.Trim(), out bool value))
                {
                    return value;
                }

                Warn($"'{key}' is not true/false: {text}; using {fallback}");
                return fallback;
            }

            public T Enum<T>(CfgNode node, string key, T fallback)
                where T : struct
            {
                string text = node.GetValue(key);
                if (text == null)
                {
                    return fallback;
                }

                if (System.Enum.TryParse(text.Trim(), true, out T value) && System.Enum.IsDefined(typeof(T), value))
                {
                    return value;
                }

                Warn($"'{key}' = {text} is not one of {string.Join(", ", System.Enum.GetNames(typeof(T)))}; using {fallback}");
                return fallback;
            }

            public Range Range(CfgNode node, string key, Range fallback)
            {
                string text = node.GetValue(key);
                if (text == null)
                {
                    return fallback;
                }

                List<string> parts = SplitList(text);
                var values = new double[parts.Count];
                bool ok = parts.Count == 1 || parts.Count == 2;
                for (int i = 0; ok && i < parts.Count; i++)
                {
                    ok = double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
                }

                if (!ok)
                {
                    Warn($"'{key}' is not 'min, max': {text}; using {fallback}");
                    return fallback;
                }

                double min = values[0];
                double max = parts.Count == 2 ? values[1] : values[0];
                if (max < min)
                {
                    Warn($"'{key}' has max < min: {text}; swapped");
                    return new Range(max, min);
                }

                return new Range(min, max);
            }
        }
    }
}
