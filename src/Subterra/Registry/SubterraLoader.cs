using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Subterra.Core.Config;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Radar;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Reads every SUBTERRA_BODY once the game database is loaded (first main menu), merges nodes of
    /// the same body, compiles the named sites and applies the Harmony patches. Problems are logged
    /// and skipped; nothing here throws into KSP.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class SubterraLoader : MonoBehaviour
    {
        public static bool Loaded { get; private set; }

        private void Start()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                Settings.Load();
                Core.Tubes.NetworkCompiler.Ramps = Settings.Current.Ramps;
                LoadBodies();
            }
            catch (Exception e)
            {
                Log.Error($"Loader failed: {e}");
            }

            try
            {
                RegisterPatches();
                PatchManager.ApplyAll();
            }
            catch (Exception e)
            {
                Log.Error($"Patches failed: {e}");
            }

            Loaded = true;
            Log.Info($"Loader finished in {watch.ElapsedMilliseconds} ms");

            // Procedural rock textures (about half a second) after the timed part: not part of reading the configs.
            try
            {
                MaterialFactory.Prewarm();
            }
            catch (Exception e)
            {
                Log.Error($"Textures failed: {e}");
            }

            Destroy(gameObject);
        }

        // Every Harmony patch group, applied one by one by PatchManager.
        private static void RegisterPatches()
        {
            PqsHolePatch.Register();
            TerrainAltitudePatch.Register();
            CaveSeaPatch.Register();
            CameraPatch.Register();
            ThermalPatches.Register();
            WarpPatch.Register();
            SciencePatches.Register();
            ResourcePatch.Register();
            ScattererPatch.Register();
            SetPositionPatch.Register();
            InteriorGravityPatch.Register();
            UndergroundAirPatch.Register();
        }

        private static void LoadBodies()
        {
            BodyRegistry.Instance.Clear();
            IEnumerable<CfgNode> raw = GameDatabase.Instance.GetConfigNodes("SUBTERRA_BODY").Select(CfgAdapter.ToCore);
            foreach (CfgNode node in BodyMerge.MergeBodies(raw))
            {
                // One broken body must not stop the others.
                try
                {
                    LoadBody(node);
                }
                catch (Exception e)
                {
                    Log.Error($"SUBTERRA_BODY '{node.GetValue("body")}' skipped: {e}");
                }
            }

            // Sources registered for bodies that did not load have nowhere to go.
            SubterraRadarApi.WarnUnattached();
        }

        private static void LoadBody(CfgNode node)
        {
            ParseResult<BodySpec> parsed = BodySpecParser.Parse(node);
            foreach (string warning in parsed.Warnings)
            {
                Log.Warn(warning);
            }

            BodySpec spec = parsed.Value;
            if (spec == null)
            {
                return;
            }

            CelestialBody body = FlightGlobals.GetBodyByName(spec.Body);
            if (body == null)
            {
                Log.Warn($"Skipped SUBTERRA_BODY '{spec.Body}': body not found");
                return;
            }

            if (!spec.Enabled || Settings.Current.DisabledBodies.Contains(spec.Body))
            {
                Log.Info($"Body {spec.Body} disabled");
                return;
            }

            if (body.pqsController == null)
            {
                Log.Warn($"Skipped SUBTERRA_BODY '{spec.Body}': it has no terrain (PQS)");
                return;
            }

            var runtime = new BodyRuntime(spec, body);
            // Relief first: the named sites below sample the terrain it shapes.
            PQSMod_SubterraRelief.Attach(runtime);
            AddCoreChamber(spec, body);
            foreach (SiteSpec site in spec.Sites)
            {
                TubeNetwork network = CompileNamed(site, body, runtime.Relief);
                if (network != null)
                {
                    runtime.Catalog.AddNamed(network);
                }
            }

            // Registered only once fully set up.
            ResourcePatch.Prepare(runtime);
            PropLibrary.Preload(spec.Scatters);
            SkylightInjector.Inject(runtime);
            AnomalyRegistrar.Register(runtime);
            runtime.NetworkTargets = new NetworkTargetSource(runtime.Catalog, runtime.Strata, new KspBiomeSampler(body));
            runtime.Targets.Register(runtime.NetworkTargets);
            // Water in the caves is a radar target too (ruling R27), next to the networks themselves.
            runtime.Targets.Register(new CaveWaterTargetSource(runtime.Catalog, runtime.Strata, new KspBiomeSampler(body)));
            SubterraRadarApi.Attach(runtime);
            BodyRegistry.Instance.Add(runtime);
            Log.Info($"Loaded body {spec.Body}: {runtime.Catalog.Named.Count} sites, {spec.Fields.Count} fields");
        }

        private sealed class LiveHeights : Core.Geo.IHeightSampler
        {
            private readonly CelestialBody body;

            public LiveHeights(CelestialBody body) => this.body = body;

            public double SurfaceRadius(double latDeg, double lonDeg) => KspSampler.SurfaceRadius(body, latDeg, lonDeg);
        }

        /// <summary>The name of a body's core chamber site.</summary>
        public static string CoreChamberName(string body) => body + "CoreChamber";

        /// <summary>Radius (m) of a body's core chamber: 0.04 % of the body's, 40 to 250 m.</summary>
        public static double CoreChamberRadius(CelestialBody body) => Math.Max(40, Math.Min(250, 0.0004 * body.Radius));

        // The Core Chamber (Lucas: something more fascinating than science at the core, as Astroneer has): a round hollow at the
        // very centre of every body, where gravity is gone, the rock glows white-hot and a giant crystal floats (CoreCrystal).
        // A site of its own, so a drill or a blast reaching it opens into it, and its own biome for science. [Claude, revisar]
        private static void AddCoreChamber(BodySpec spec, CelestialBody body)
        {
            string name = CoreChamberName(spec.Body);
            if (spec.Sites.Exists(s => s.Name == name))
            {
                return;
            }

            double radius = CoreChamberRadius(body);
            double ground = KspSampler.SurfaceRadius(body, 0, 0);
            var site = new SiteSpec { Name = name, DisplayName = "#Subterra_Site_CoreChamber", IsNamed = true, Network = false };
            // A geode-like bubble whose floor is a radius under the centre: the chamber is centred on it.
            site.Nodes.Add(new NodeSpec
            {
                Lat = 0,
                Lon = 0,
                Depth = ground - radius,
                Width = radius,
                Height = 2 * radius,
                Feature = NodeFeature.Geode,
            });
            spec.Sites.Add(site);
        }

        /// <summary>Farthest (m) a site is moved to reach its biome, and the steps of the search.</summary>
        private const double SnapReach = 60000, SnapStep = 500;

        // A site that must lie in a biome (snapBiome) and does not: moved, every node by the same shift, to the lowest ground of
        // that biome among the nearest places in it (a canyon's floor, not its rim). [Claude, revisar]
        private static void SnapToBiome(SiteSpec site, CelestialBody body)
        {
            if (string.IsNullOrEmpty(site.SnapBiome) || site.Nodes.Count == 0 || body.BiomeMap == null)
            {
                return;
            }

            NodeSpec first = site.Nodes[0];
            bool In(double la, double lo) => string.Equals(KspSampler.Biome(body, la, lo), site.SnapBiome, StringComparison.OrdinalIgnoreCase);
            if (In(first.Lat, first.Lon))
            {
                return;
            }

            double bestLat = double.NaN, bestLon = double.NaN, bestRadius = double.MaxValue;
            double found = -1;
            for (double d = SnapStep; d <= SnapReach; d += SnapStep)
            {
                // Once a ring reaches the biome, a few more rings (2 km) are searched for its lowest ground.
                if (found > 0 && d > found + 2000)
                {
                    break;
                }

                int bearings = Math.Max(12, (int)(2 * Math.PI * d / SnapStep));
                for (int k = 0; k < bearings; k++)
                {
                    double a = 2 * Math.PI * k / bearings;
                    double la = first.Lat + d * Math.Cos(a) / body.Radius * 180 / Math.PI;
                    double lo = first.Lon + d * Math.Sin(a) / (body.Radius * Math.Max(0.05, Math.Cos(first.Lat * Math.PI / 180))) * 180 / Math.PI;
                    if (la > 89 || la < -89 || !In(la, lo))
                    {
                        continue;
                    }

                    if (found < 0)
                    {
                        found = d;
                    }

                    double radius = KspSampler.SurfaceRadius(body, la, lo);
                    if (radius < bestRadius)
                    {
                        bestRadius = radius;
                        bestLat = la;
                        bestLon = lo;
                    }
                }
            }

            if (double.IsNaN(bestLat))
            {
                Log.Warn($"Site {site.Name}: no {site.SnapBiome} within {SnapReach / 1000:F0} km; left where it is");
                return;
            }

            double dLat = bestLat - first.Lat, dLon = bestLon - first.Lon;
            foreach (NodeSpec n in site.Nodes)
            {
                n.Lat = Math.Max(-89.9, Math.Min(89.9, n.Lat + dLat));
                n.Lon += dLon;
            }

            Log.Info($"Site {site.Name} moved {dLat:F4}, {dLon:F4} degrees into {site.SnapBiome} (now at {site.Nodes[0].Lat:F4}, {site.Nodes[0].Lon:F4})");
        }

        // Heights are read straight from the PQS on the main thread: named sites are few and small.
        internal static TubeNetwork CompileNamed(SiteSpec site, CelestialBody body, Core.Geo.ReliefField relief = null)
        {
            try
            {
                SnapToBiome(site, body);
                // A named site grows into a network around its own nodes first (its new nodes need terrain samples too).
                Core.Procedural.FieldGenerator.Elaborate(site, body.Radius, new LiveHeights(body));
                var samples = new PrecomputedSamples();
                foreach (var (lat, lon) in NetworkCompiler.RequiredSamples(site, body.Radius))
                {
                    if (!samples.Has(lat, lon))
                    {
                        samples.Add(lat, lon, KspSampler.SurfaceRadius(body, lat, lon), null);
                    }
                }

                var warnings = new List<string>();
                Func<double, double, double> delta = relief == null || relief.IsEmpty ? null : (Func<double, double, double>)((lat, lon) => relief.HeightDelta(Core.Geo.BodyFrame.Direction(lat, lon)));
                TubeNetwork network = NetworkCompiler.Compile(site, body.bodyName, body.Radius, samples, warnings, delta);
                foreach (string warning in warnings)
                {
                    Log.Warn(warning);
                }

                return network;
            }
            catch (Exception e)
            {
                Log.Error($"Site {site.Name} on {body.bodyName} skipped: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }
    }
}
