using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Subterra.Core.Config;
using Subterra.Core.Geo;
using Subterra.Core.Model;
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
            CameraPatch.Register();
            ThermalPatches.Register();
            SciencePatches.Register();
            ResourcePatch.Register();
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
            BodyRegistry.Instance.Add(runtime);
            Log.Info($"Loaded body {spec.Body}: {runtime.Catalog.Named.Count} sites, {spec.Fields.Count} fields");
        }

        private sealed class LiveHeights : Core.Geo.IHeightSampler
        {
            private readonly CelestialBody body;

            public LiveHeights(CelestialBody body) => this.body = body;

            public double SurfaceRadius(double latDeg, double lonDeg) => KspSampler.SurfaceRadius(body, latDeg, lonDeg);
        }

        // Heights are read straight from the PQS on the main thread: named sites are few and small.
        internal static TubeNetwork CompileNamed(SiteSpec site, CelestialBody body, Core.Geo.ReliefField relief = null)
        {
            try
            {
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
