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

            // Captured for the workers: no KSP objects are touched off the main thread.
            string bodyName = body.bodyName;
            double bodyRadius = body.Radius;
            string idPrefix = runtime.Spec.IdPrefix;
            List<FieldSpec> fields = runtime.Spec.Fields;
            var job = new CellJob();

            // 1. The cell's sample grid (+10 km), on the main thread.
            var rect = catalog.Grid.GridRect(cell, FieldGenerator.CellMargin);
            IReadOnlyList<(double lat, double lon)> points = catalog.Grid.SampleGrid(cell, GridSize, FieldGenerator.CellMargin);
            var radius = new double[points.Count];
            var biome = new string[points.Count];
            yield return Guarded(KspSampler.Fill(body, points, (i, r, b) => { radius[i] = r; biome[i] = b; }, SampleBudgetMs), job);

            // 2. Generation on the pool.
            List<SiteSpec> sites = null;
            if (job.Error == null)
            {
                var grid = new GridSamples(rect.latMin, rect.latMax, rect.lonMin, rect.lonMax, GridSize, radius, biome);
                job.Done = false;
                WorkerPool.Shared.Enqueue(() =>
                {
                    try
                    {
                        // All fields at once: they share the per-cell cap and id counter (unique ids).
                        sites = FieldGenerator.Generate(fields, bodyName, idPrefix, catalog.Grid, cell, grid, grid);
                    }
                    catch (Exception e)
                    {
                        job.Error = e;
                    }

                    job.Done = true;
                });
                yield return new WaitUntil(() => job.Done);
            }

            // 3. Exact heights for the networks' paths, on the main thread.
            var samples = new PrecomputedSamples();
            if (job.Error == null)
            {
                var needed = sites.SelectMany(s => NetworkCompiler.RequiredSamples(s, bodyRadius)).ToList();
                yield return Guarded(KspSampler.Fill(body, needed, (i, r, b) => samples.Add(needed[i].lat, needed[i].lon, r, b), SampleBudgetMs), job);
            }

            // 4. Compilation on the pool.
            var networks = new List<TubeNetwork>();
            if (job.Error == null)
            {
                job.Done = false;
                WorkerPool.Shared.Enqueue(() =>
                {
                    try
                    {
                        foreach (SiteSpec site in sites)
                        {
                            networks.Add(NetworkCompiler.Compile(site, bodyName, bodyRadius, samples, null));
                        }
                    }
                    catch (Exception e)
                    {
                        job.Error = e;
                    }

                    job.Done = true;
                });
                yield return new WaitUntil(() => job.Done);
            }

            // 5. Publish on the main thread (an error leaves the cell empty rather than retrying forever).
            inProgress.Remove(key);
            if (job.Error != null)
            {
                Log.Error($"{cell} on {bodyName} failed: {job.Error}");
                networks.Clear();
            }

            catalog.AddCell(cell, networks);
            CellsGenerated++;
            Log.Info($"{cell} on {bodyName}: {networks.Count} networks{(networks.Count > 0 ? " (" + string.Join(", ", networks.Select(n => n.Id)) + ")" : "")}");
            NetworksAdded?.Invoke(runtime, networks);
        }

        // Runs a nested coroutine, turning an exception into job.Error instead of killing the caller.
        private static IEnumerator Guarded(IEnumerator inner, CellJob job)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext())
                    {
                        yield break;
                    }

                    current = inner.Current;
                }
                catch (Exception e)
                {
                    job.Error = e;
                    yield break;
                }

                yield return current;
            }
        }

        private sealed class CellJob
        {
            public volatile bool Done;
            public volatile Exception Error;

        }

        /// <summary>Raised on the main thread when a cell's networks join the catalog.</summary>
        public static event Action<BodyRuntime, List<TubeNetwork>> NetworksAdded;
    }
}
