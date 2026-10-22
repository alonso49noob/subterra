using System;
using System.Collections.Generic;
using System.Diagnostics;
using Subterra.Core.Environment;
using Subterra.Core.Procedural;
using Subterra.Core.Radar;
using Subterra.Core.Tubes;
using UnityEngine;
using static System.FormattableString;

namespace Subterra
{
    /// <summary>
    /// Lazy detection (spec 4.4): turns orbital coverage into radar findings without generating the whole body.
    ///
    /// Which cells to look at: per body, a <see cref="CellCoverageIndex"/> keeps one signature per 50 km network
    /// cell, the sum of both instruments' coverage bytes over the cell's box (the cell plus the margin its networks
    /// may stick out by). <see cref="OrbitalScanner.CoverageChanged"/> (or a grid Version that moved) marks the body
    /// dirty; a worker then recomputes the signatures and lists the cells whose coverage improved. This is right
    /// for a warp catch-up of many orbits in one tick as well as for one pass, since it reads the grids rather than
    /// the scanner's last sub-point, and costs one read of each coverage byte (about twice, the boxes overlap) on a
    /// worker, at most once a second per body (decidido por Claude, revisar).
    ///
    /// What to do with them: a cell whose networks exist (or a body without procedural fields) is evaluated on the
    /// worker straight away; a missing one is requested from <see cref="CellScheduler.Request"/> at low priority and
    /// evaluated when it is ready (its named networks, which always exist, are evaluated at once). Evaluating a cell runs every target of its networks, and of the named networks
    /// with targets in it, through an <see cref="OrbitalSurvey"/> with the best altitude of each instrument's grid at
    /// the target (Eco and Plomada); the survey skips targets whose coverage did not improve. Instruments: the Core
    /// factory values (<see cref="Instrument.Eco"/>, <see cref="Instrument.Plomada"/>): the grids do not record which
    /// part scanned, and the parts' cfg values are the factory ones (radarInstrument assertion; decidido por
    /// Claude, revisar). The detections are applied to <see cref="RadarScenario.Detections"/> on the main thread
    /// within a time budget, and the networks that reach Hint are announced through <see cref="RadarMessages"/>.
    ///
    /// Flight scene only (the cell scheduler lives there): coverage gained in the Tracking Station or at the Space
    /// Center is picked up by the sweep of every body with coverage when the flight scene starts.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class LazyDetector : MonoBehaviour
    {
        /// <summary>Margin (m) of a cell's coverage box: networks stay within the cell's 10 km generation margin, plus half a tube.</summary>
        public const double BoxMargin = FieldGenerator.CellMargin + 1000;

        private const float TickSeconds = 1f;

        /// <summary>Main-thread time per frame for applying detections to the store.</summary>
        private const double ApplyBudgetMs = 1;

        private const int ClockStride = 32;

        private readonly Dictionary<string, BodyState> bodies = new Dictionary<string, BodyState>();
        private readonly object finishedGate = new object();
        private readonly Queue<JobResult> finished = new Queue<JobResult>();
        private readonly Queue<JobResult> toApply = new Queue<JobResult>();
        private readonly List<int> scratch = new List<int>();
        private readonly Stopwatch watch = new Stopwatch();
        private DetectionStore subscribed;
        private bool applying;
        private string applyingBody;
        private InstrumentKind applyingKind;
        private bool swept;
        private float nextTick;

        public static LazyDetector Instance { get; private set; }

        /// <summary>Cells of the body whose procedural networks have been evaluated in this scene (diagnostics).</summary>
        public int CellsEvaluated(string body) => bodies.TryGetValue(body, out BodyState s) ? s.CellsEvaluated : 0;

        /// <summary>Cells of the body requested from the cell scheduler and not generated yet.</summary>
        public int CellsWaiting(string body) => bodies.TryGetValue(body, out BodyState s) ? s.Waiting.Count : 0;

        private void Awake()
        {
            Instance = this;
            OrbitalScanner.CoverageChanged += OnCoverageChanged;
            CellScheduler.NetworksAdded += OnNetworksAdded;
        }

        private void OnDestroy()
        {
            OrbitalScanner.CoverageChanged -= OnCoverageChanged;
            CellScheduler.NetworksAdded -= OnNetworksAdded;
            Subscribe(null);
            RadarMessages.Update(true);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnCoverageChanged(string body, double lat, double lon)
        {
            BodyState state = StateOf(body);
            if (state != null)
            {
                state.Dirty = true;
            }
        }

        private void OnNetworksAdded(BodyRuntime runtime, List<TubeNetwork> networks)
        {
            if (runtime != null && bodies.TryGetValue(runtime.Body.bodyName, out BodyState state))
            {
                CollectReady(state);
            }
        }

        private void Update()
        {
            using (SubterraClock.Measure("LazyDetector"))
            {
                RadarScenario scenario = RadarScenario.Instance;
                if (scenario == null)
                {
                    return;
                }

                Subscribe(scenario.Detections);
                TakeFinished();
                Apply(scenario.Detections);
                RadarMessages.Update();
                if (Time.realtimeSinceStartup >= nextTick)
                {
                    nextTick = Time.realtimeSinceStartup + TickSeconds;
                    Tick(scenario);
                }
            }
        }

        private void Tick(RadarScenario scenario)
        {
            if (!swept)
            {
                // Coverage from before this scene (the save, the Tracking Station): look at every body that has some.
                swept = true;
                foreach (string name in BodyRegistry.Instance.Bodies.Keys)
                {
                    if (scenario.TryCoverage(name, InstrumentKind.OrbitalRadar, out _) || scenario.TryCoverage(name, InstrumentKind.Gravimeter, out _))
                    {
                        StateOf(name).Dirty = true;
                    }
                }
            }

            foreach (BodyState state in bodies.Values)
            {
                scenario.TryCoverage(state.Name, InstrumentKind.OrbitalRadar, out CoverageGrid echo);
                scenario.TryCoverage(state.Name, InstrumentKind.Gravimeter, out CoverageGrid gravity);
                if (!ReferenceEquals(echo, state.Echo) || !ReferenceEquals(gravity, state.Gravity))
                {
                    // A grid that existed was replaced: a reload, which also reset the detections. Start this body
                    // over. (A grid that only appears now is the first scan of that instrument.)
                    if (state.Echo != null && !ReferenceEquals(echo, state.Echo) || state.Gravity != null && !ReferenceEquals(gravity, state.Gravity))
                    {
                        state.Reset();
                    }

                    state.Echo = echo;
                    state.Gravity = gravity;
                    state.Dirty = true;
                }

                if (echo != null && echo.Version != state.EchoVersion || gravity != null && gravity.Version != state.GravityVersion)
                {
                    state.Dirty = true;
                }

                CollectReady(state);
                if (!state.Busy && (state.Dirty || state.Ready.Count > 0) && (echo != null || gravity != null))
                {
                    StartJob(state);
                }
            }
        }

        private BodyState StateOf(string body)
        {
            if (body == null)
            {
                return null;
            }

            if (!bodies.TryGetValue(body, out BodyState state))
            {
                if (!BodyRegistry.Instance.Bodies.TryGetValue(body, out BodyRuntime runtime) || runtime.NetworkTargets == null)
                {
                    return null;
                }

                state = new BodyState(body, runtime);
                bodies[body] = state;
            }

            return state;
        }

        // Moves the requested cells that are generated now to the cells to evaluate.
        private void CollectReady(BodyState state)
        {
            if (state.Waiting.Count == 0)
            {
                return;
            }

            scratch.Clear();
            NetworkCatalog catalog = state.Runtime.Catalog;
            foreach (int index in state.Waiting)
            {
                if (catalog.IsCellReady(CellId.FromIndex(index, catalog.Grid.N)))
                {
                    scratch.Add(index);
                }
            }

            foreach (int index in scratch)
            {
                state.Waiting.Remove(index);
                state.Ready.Add(index);
            }
        }

        private void StartJob(BodyState state)
        {
            // Versions are read before the worker reads the cells (CoverageGrid's threading contract): a change that
            // lands while the job runs moves the version again and makes the next tick look once more.
            state.EchoVersion = state.Echo?.Version ?? 0;
            state.GravityVersion = state.Gravity?.Version ?? 0;
            var job = new JobResult
            {
                State = state,
                Generation = state.Generation,
                Scan = state.Dirty,
                Echo = state.Echo,
                Gravity = state.Gravity,
                Index = state.Index,
                Signatures = state.Signatures,
                Survey = state.Survey,
                Cells = new List<int>(state.Ready),
                HasFields = state.Runtime.Spec.Fields.Count > 0,
                Runtime = state.Runtime,
                Body = state.Runtime.Body,
            };
            state.Dirty = false;
            state.Ready.Clear();
            state.Busy = true;
            WorkerPool.Shared.Enqueue(() =>
            {
                try
                {
                    Run(job);
                }
                catch (Exception e)
                {
                    job.Error = e;
                }

                lock (finishedGate)
                {
                    finished.Enqueue(job);
                }
            });
        }

        // On a worker: finds the cells whose coverage improved and evaluates the ready ones. Touches no Unity object
        // (KspSampler.Biome is plain managed code under its own lock, ruling R22).
        private static void Run(JobResult job)
        {
            NetworkCatalog catalog = job.Runtime.Catalog;
            CellGrid grid = catalog.Grid;
            job.Index = job.Index ?? new CellCoverageIndex(grid, BoxMargin);
            job.Signatures = job.Signatures ?? new long[job.Index.CellCount];
            if (job.Scan)
            {
                foreach (int index in job.Index.Changed(job.Echo, job.Gravity, job.Signatures))
                {
                    if (!job.HasFields || catalog.IsCellReady(CellId.FromIndex(index, grid.N)))
                    {
                        job.Cells.Add(index);
                    }
                    else
                    {
                        job.Missing.Add(index);
                    }
                }
            }

            if (job.Cells.Count == 0 && job.Missing.Count == 0)
            {
                return;
            }

            Dictionary<int, List<TubeNetwork>> named = NamedByCell(catalog, job.Runtime.NetworkTargets);
            NetworkTargetSource source = job.Runtime.NetworkTargets;
            Strata strata = job.Runtime.Strata;
            CelestialBody body = job.Body;
            Func<SubsurfaceTarget, IReadOnlyList<LayerSpan>> spansOf = t => strata.Spans(KspSampler.Biome(body, t.Lat, t.Lon));
            var done = new HashSet<int>();
            foreach (int index in job.Cells)
            {
                if (!done.Add(index))
                {
                    continue;
                }

                if (job.HasFields && catalog.TryGetCell(CellId.FromIndex(index, grid.N), out IReadOnlyList<TubeNetwork> networks))
                {
                    job.CellsEvaluated++;
                    foreach (TubeNetwork net in networks)
                    {
                        Survey(job, source.TargetsOf(net), spansOf);
                    }
                }

                SurveyNamed(job, named, index, spansOf);
            }

            // Named networks exist from the start: they need not wait for their cell's procedural networks.
            foreach (int index in job.Missing)
            {
                if (done.Add(index))
                {
                    SurveyNamed(job, named, index, spansOf);
                }
            }
        }

        private static void SurveyNamed(JobResult job, Dictionary<int, List<TubeNetwork>> named, int index,
            Func<SubsurfaceTarget, IReadOnlyList<LayerSpan>> spansOf)
        {
            if (named.TryGetValue(index, out List<TubeNetwork> sites))
            {
                foreach (TubeNetwork net in sites)
                {
                    Survey(job, job.Runtime.NetworkTargets.TargetsOf(net), spansOf);
                }
            }
        }

        private static void Survey(JobResult job, IReadOnlyList<SubsurfaceTarget> targets, Func<SubsurfaceTarget, IReadOnlyList<LayerSpan>> spansOf)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (job.Survey.Evaluate(targets[i], job.Echo, job.Gravity, spansOf, job.Detections))
                {
                    job.TargetsEvaluated++;
                }
            }
        }

        // The named networks (evaluated like the procedural ones) by the cells their targets are in.
        private static Dictionary<int, List<TubeNetwork>> NamedByCell(NetworkCatalog catalog, NetworkTargetSource source)
        {
            var byCell = new Dictionary<int, List<TubeNetwork>>();
            CellGrid grid = catalog.Grid;
            foreach (TubeNetwork net in catalog.Named)
            {
                foreach (SubsurfaceTarget t in source.TargetsOf(net))
                {
                    int index = grid.CellOf(t.Position.Normalized).Index(grid.N);
                    if (!byCell.TryGetValue(index, out List<TubeNetwork> list))
                    {
                        list = new List<TubeNetwork>();
                        byCell[index] = list;
                    }

                    if (!list.Contains(net))
                    {
                        list.Add(net);
                    }
                }
            }

            return byCell;
        }

        // Main thread: takes the finished jobs, requests their missing cells and queues their detections.
        private void TakeFinished()
        {
            while (true)
            {
                JobResult job;
                lock (finishedGate)
                {
                    if (finished.Count == 0)
                    {
                        return;
                    }

                    job = finished.Dequeue();
                }

                BodyState state = job.State;
                if (job.Generation != state.Generation)
                {
                    continue; // The body was started over while this job ran.
                }

                state.Busy = false;
                state.Index = job.Index ?? state.Index;
                if (job.Error != null)
                {
                    // The job's signatures (and what its survey remembered) may claim cells and targets whose
                    // results were lost: forget both so every covered cell is looked at again (requested again if
                    // missing) on the next tick.
                    state.Signatures = null;
                    state.Survey = BodyState.NewSurvey();
                    state.Dirty = true;
                    if (!state.Failed)
                    {
                        state.Failed = true;
                        Log.Error($"Lazy detection on {state.Name} failed: {job.Error}");
                    }

                    continue;
                }

                state.Signatures = job.Signatures ?? state.Signatures;

                CellScheduler scheduler = CellScheduler.Instance;
                CellGrid grid = state.Runtime.Catalog.Grid;
                int requested = 0;
                foreach (int index in job.Missing)
                {
                    if (state.Waiting.Add(index))
                    {
                        requested++;
                        scheduler?.Request(state.Runtime, CellId.FromIndex(index, grid.N));
                    }
                }

                state.CellsEvaluated += job.CellsEvaluated;
                if (requested > 0 || job.Detections.Count > 0)
                {
                    Log.Info(Invariant($"Lazy detection on {state.Name}: {job.CellsEvaluated} cell(s) evaluated, {job.TargetsEvaluated} target(s), {job.Detections.Count} detection(s), {requested} cell(s) requested ({state.Waiting.Count} waiting)"));
                }

                if (job.Detections.Count > 0)
                {
                    toApply.Enqueue(job);
                }
            }
        }

        // Main thread: folds queued detections into the store within the budget.
        private void Apply(DetectionStore store)
        {
            if (toApply.Count == 0)
            {
                return;
            }

            watch.Reset();
            watch.Start();
            int n = 0;
            applying = true;
            try
            {
                while (toApply.Count > 0)
                {
                    JobResult job = toApply.Peek();
                    if (job.Generation != job.State.Generation)
                    {
                        toApply.Dequeue();
                        continue;
                    }

                    applyingBody = job.State.Name;
                    while (job.Applied < job.Detections.Count)
                    {
                        if (++n % ClockStride == 0 && watch.Elapsed.TotalMilliseconds > ApplyBudgetMs)
                        {
                            return;
                        }

                        SurveyResult r = job.Detections[job.Applied++];
                        applyingKind = r.Kind;
                        store.Apply(job.State.Name, r.Target, r.Detection);
                    }

                    toApply.Dequeue();
                }
            }
            finally
            {
                applying = false;
            }
        }

        private void Subscribe(DetectionStore store)
        {
            if (ReferenceEquals(store, subscribed))
            {
                return;
            }

            if (subscribed != null)
            {
                subscribed.LevelRaised -= OnLevelRaised;
            }

            subscribed = store;
            if (store != null)
            {
                store.LevelRaised += OnLevelRaised;
            }
        }

        private void OnLevelRaised(NetworkDetection record, DetectionLevel level)
        {
            if (applying && level == DetectionLevel.Hint)
            {
                RadarMessages.Hint(applyingBody, 1, applyingKind);
            }
        }

        /// <summary>What lazy detection keeps for one body.</summary>
        private sealed class BodyState
        {
            public readonly string Name;
            public readonly BodyRuntime Runtime;

            /// <summary>Requested cells not generated yet, by cell index.</summary>
            public readonly HashSet<int> Waiting = new HashSet<int>();

            /// <summary>Requested cells generated since the last job, to evaluate in the next one.</summary>
            public readonly List<int> Ready = new List<int>();

            public CoverageGrid Echo, Gravity;
            public int EchoVersion, GravityVersion;
            public CellCoverageIndex Index;
            public long[] Signatures;
            public OrbitalSurvey Survey = NewSurvey();
            public int Generation;
            public bool Dirty, Busy, Failed;
            public int CellsEvaluated;

            public BodyState(string name, BodyRuntime runtime)
            {
                Name = name;
                Runtime = runtime;
            }

            /// <summary>Forgets what was evaluated (the index of cell boxes stays: it only depends on the body).</summary>
            public void Reset()
            {
                Generation++;
                Signatures = null;
                Survey = NewSurvey();
                Ready.Clear();
                Busy = false;
                EchoVersion = GravityVersion = 0;
            }

            public static OrbitalSurvey NewSurvey() => new OrbitalSurvey(Instrument.Eco(), Instrument.Plomada());
        }

        /// <summary>One worker job for one body: its inputs and, once done, its results.</summary>
        private sealed class JobResult
        {
            public BodyState State;
            public int Generation;
            public bool Scan;
            public bool HasFields;
            public BodyRuntime Runtime;
            public CelestialBody Body;
            public CoverageGrid Echo, Gravity;
            public CellCoverageIndex Index;
            public long[] Signatures;
            public OrbitalSurvey Survey;

            /// <summary>Cells to evaluate (requested cells now ready, then the changed ready cells).</summary>
            public List<int> Cells;

            /// <summary>Changed cells whose networks do not exist yet.</summary>
            public readonly List<int> Missing = new List<int>();

            public readonly List<SurveyResult> Detections = new List<SurveyResult>();
            public int Applied;
            public int CellsEvaluated, TargetsEvaluated;
            public Exception Error;
        }
    }
}
