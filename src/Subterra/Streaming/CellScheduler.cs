using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Generates the procedural networks of the cells near the camera (60 km): heights and biomes are
    /// sampled on the main thread in 2 ms slices, generation and compilation run on the worker pool.
    /// Also pumps the pool's main-thread queue every frame.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class CellScheduler : MonoBehaviour
    {
        public const double Radius = 60000;
        private const float SampleBudgetMs = 1f;
        private const int GridSize = 64;
        private const int MaxConcurrentCells = 2;

        private readonly HashSet<(string, CellId)> inProgress = new HashSet<(string, CellId)>();
        private float nextCheck;

        public static CellScheduler Instance { get; private set; }

        public int CellsInProgress => inProgress.Count;

        public int CellsGenerated { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            SubterraClock.Roll();
            using (SubterraClock.Measure("MainQueue"))
            {
                // Half the budget for uploads; cell sampling and the rest share the other half.
                WorkerPool.Shared.PumpMain(Settings.Current.MainThreadBudgetMs * 0.5f);
            }

            using (SubterraClock.Measure("CellScheduler"))
            {
                if (Time.realtimeSinceStartup < nextCheck || !FlightGlobals.ready || FlightCamera.fetch == null)
                {
                    return;
                }

                nextCheck = Time.realtimeSinceStartup + 1f;
                CelestialBody body = FlightGlobals.currentMainBody;
                if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) || runtime.Spec.Fields.Count == 0)
                {
                    return;
                }

                var camera = Frames.ToBody(body, FlightCamera.fetch.mainCamera.transform.position);
                foreach (CellId cell in runtime.Catalog.MissingCellsNear(camera, Radius))
                {
                    if (inProgress.Count >= MaxConcurrentCells)
                    {
                        break;
                    }

                    if (inProgress.Add((body.bodyName, cell)))
                    {
                        StartCoroutine(Generate(runtime, cell));
                    }
                }
            }
        }

        private IEnumerator Generate(BodyRuntime runtime, CellId cell)
        {
            CelestialBody body = runtime.Body;
            NetworkCatalog catalog = runtime.Catalog;
            var key = (body.bodyName, cell);

            // 1. The cell's sample grid (+10 km), on the main thread.
            var rect = catalog.Grid.GridRect(cell, FieldGenerator.CellMargin);
            IReadOnlyList<(double lat, double lon)> points = catalog.Grid.SampleGrid(cell, GridSize, FieldGenerator.CellMargin);
            var radius = new double[points.Count];
            var biome = new string[points.Count];
            yield return KspSampler.Fill(body, points, (i, r, b) => { radius[i] = r; biome[i] = b; }, SampleBudgetMs);
            var grid = new GridSamples(rect.latMin, rect.latMax, rect.lonMin, rect.lonMax, GridSize, radius, biome);

            // 2. Generation on the pool.
            List<SiteSpec> sites = null;
            Exception error = null;
            bool done = false;
            WorkerPool.Shared.Enqueue(() =>
            {
                try
                {
                    // All fields at once: they share the per-cell cap and id counter (unique ids).
                    sites = FieldGenerator.Generate(runtime.Spec.Fields, body.bodyName, runtime.Spec.IdPrefix, catalog.Grid, cell, grid, grid);
                }
                catch (Exception e)
                {
                    error = e;
                }

                done = true;
            });
            yield return new WaitUntil(() => done);

            // 3. Exact heights for the networks' paths, on the main thread.
            var samples = new PrecomputedSamples();
            if (error == null)
            {
                var needed = sites.SelectMany(s => NetworkCompiler.RequiredSamples(s, body.Radius)).ToList();
                yield return KspSampler.Fill(body, needed, (i, r, b) => samples.Add(needed[i].lat, needed[i].lon, r, b), SampleBudgetMs);
            }

            // 4. Compilation on the pool.
            var networks = new List<TubeNetwork>();
            done = false;
            if (error == null)
            {
                WorkerPool.Shared.Enqueue(() =>
                {
                    try
                    {
                        foreach (SiteSpec site in sites)
                        {
                            networks.Add(NetworkCompiler.Compile(site, body.bodyName, body.Radius, samples, null));
                        }
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }

                    done = true;
                });
                yield return new WaitUntil(() => done);
            }

            // 5. Publish on the main thread (an error leaves the cell empty rather than retrying forever).
            if (error != null)
            {
                Log.Error($"{cell} on {body.bodyName} failed: {error}");
                networks.Clear();
            }

            catalog.AddCell(cell, networks);
            inProgress.Remove(key);
            CellsGenerated++;
            Log.Info($"{cell} on {body.bodyName}: {networks.Count} networks{(networks.Count > 0 ? " (" + string.Join(", ", networks.Select(n => n.Id)) + ")" : "")}");
            NetworksAdded?.Invoke(runtime, networks);
        }

        /// <summary>Raised on the main thread when a cell's networks join the catalog.</summary>
        public static event Action<BodyRuntime, List<TubeNetwork>> NetworksAdded;
    }
}
