using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Procedural;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Generates the procedural networks of the cells near the camera (60 km): heights and biomes are
    /// sampled on the main thread in 2 ms slices, generation and compilation run on the worker pool.
    /// Also pumps the pool's main-thread queue every frame. Cells of any body can also be asked for with
    /// <see cref="Request"/> (the radar's lazy detection): those wait in a low-priority queue served only when no
    /// cell near the camera is missing.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class CellScheduler : MonoBehaviour
    {
        public const double Radius = 60000;
        private const float SampleBudgetMs = 1f;
        private const int GridSize = 64;
        private const int MaxConcurrentCells = 2;

        /// <summary>No new cells above this time-warp rate.</summary>
        private const float MaxWarpRate = 4f;

        /// <summary>No new cells while the active vessel is higher than this over the ground (m).</summary>
        private const double MaxAltitude = 25000;

        private readonly HashSet<(string, CellId)> inProgress = new HashSet<(string, CellId)>();

        // Low-priority requests: the active body's cells nearest the active vessel first, then the other bodies'.
        private readonly CellRequestQueue<BodyRuntime> requests = new CellRequestQueue<BodyRuntime>();
        private float nextCheck;

        public static CellScheduler Instance { get; private set; }

        public int CellsInProgress => inProgress.Count;

        public int CellsGenerated { get; private set; }

        /// <summary>Requested cells still waiting in the low-priority queue.</summary>
        public int CellsRequested => requests.Count;

        /// <summary>
        /// Asks for a cell's networks at low priority: the request waits until no cell near the camera is missing,
        /// fewer than <see cref="MaxConcurrentCells"/> cells are being generated and time runs at most
        /// <see cref="MaxWarpRate"/>x. It ignores <see cref="MaxAltitude"/> and may be for any body (sampling a body
        /// that is not the current one gives the same results, M0 task 4 / ruling R11). A cell already generated,
        /// being generated or already waiting is not queued again; the cell's networks arrive through
        /// <see cref="NetworksAdded"/> like any other cell's.
        /// </summary>
        public void Request(BodyRuntime runtime, CellId cell)
        {
            if (runtime == null || runtime.Spec.Fields.Count == 0 || runtime.Catalog.IsCellReady(cell))
            {
                return;
            }

            string body = runtime.Body.bodyName;
            if (!inProgress.Contains((body, cell)))
            {
                requests.Add(body, cell, runtime.Catalog.Grid, runtime);
            }
        }

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

                // Not while time runs fast (the ground track sweeps cell after cell: the sampling and the terrain cuts
                // dropped frames); this holds for requested cells too.
                if (TimeWarp.CurrentRate > MaxWarpRate)
                {
                    return;
                }

                if (StartNearbyCells())
                {
                    return;
                }

                StartRequestedCells();
            }
        }

        // Starts the missing cells near the camera, as many as the cap allows. True if some cell near the camera is
        // still missing (being generated or waiting for a free slot): requested cells wait for those.
        private bool StartNearbyCells()
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            if (!BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) || runtime.Spec.Fields.Count == 0)
            {
                return false;
            }

            // Not high above the ground (nothing there is close enough to need networks yet).
            Vessel active = FlightGlobals.ActiveVessel;
            if (active != null && active.altitude - Math.Max(0, active.terrainAltitude) > MaxAltitude)
            {
                return false;
            }

            bool missing = false;
            var camera = Frames.ToBody(body, FlightCamera.fetch.mainCamera.transform.position);
            foreach (CellId cell in runtime.Catalog.MissingCellsNear(camera, Radius))
            {
                missing = true;
                if (inProgress.Count >= MaxConcurrentCells)
                {
                    break;
                }

                if (inProgress.Add((body.bodyName, cell)))
                {
                    StartCoroutine(Generate(runtime, cell));
                }
            }

            return missing;
        }

        // Starts one requested cell if there is a free slot: the current body's first, nearest the active vessel (or
        // the camera) first; other bodies' in the order they were asked for (ruling R26). One per check (a second):
        // starting a cell samples its first slice in the same frame, and a cell takes about a second anyway
        // (decidido por Claude, revisar).
        private void StartRequestedCells()
        {
            if (requests.Count == 0 || inProgress.Count >= MaxConcurrentCells)
            {
                return;
            }

            // Only a Subterra body can have queued cells (and a PQS to measure from); around any other body (Jool) the
            // queue is served in the order asked.
            CelestialBody main = FlightGlobals.currentMainBody;
            string activeBody = null;
            Vec3d direction = Vec3d.Zero;
            if (BodyRegistry.Instance.TryGet(main, out _) && main.pqsController != null)
            {
                Vessel active = FlightGlobals.ActiveVessel;
                Vector3d world = active != null ? active.GetWorldPos3D() : (Vector3d)FlightCamera.fetch.mainCamera.transform.position;
                direction = Frames.ToBody(main, world);
                activeBody = main.bodyName;
            }

            while (inProgress.Count < MaxConcurrentCells
                && requests.TryTake(activeBody, direction, out string body, out CellId cell, out BodyRuntime runtime))
            {
                if (runtime.Catalog.IsCellReady(cell) || !inProgress.Add((body, cell)))
                {
                    continue;
                }

                StartCoroutine(Generate(runtime, cell));
                return;
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
