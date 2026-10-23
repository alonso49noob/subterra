using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Subterra.Core.Environment;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Radar;

namespace Subterra
{
    /// <summary>A network the ground radar has just taken to the Detected level, for the message on screen.</summary>
    public readonly struct GroundFinding
    {
        public GroundFinding(string networkId, double distance, double bearing)
        {
            NetworkId = networkId;
            Distance = distance;
            Bearing = bearing;
        }

        public string NetworkId { get; }

        /// <summary>Metres from the instrument to the centre of the nearest target found of the network.</summary>
        public double Distance { get; }

        /// <summary>Degrees clockwise from north, from the instrument to that target.</summary>
        public double Bearing { get; }
    }

    /// <summary>
    /// One sample of the Zahori ground radar: what the instrument sees from where it stands (spec 4.2 and 4.3 c).
    /// Stateless; main thread (the biome map and the terrain are read from KSP).
    /// </summary>
    public static class GroundScan
    {
        /// <summary>What the last <see cref="Sample"/> looked at (for the in-game tests and the debug window).</summary>
        public static string LastSummary { get; private set; } = "";

        private static int samples;
        private static double worstMs;

        /// <summary>The slowest sample so far, as <see cref="LastSummary"/> describes it (for the in-game tests).</summary>
        public static string WorstSummary { get; private set; } = "";

        // Prefetch (decidido por Claude, revisar): the first sample near a network builds the targets of all its
        // primitives (a biome lookup and a layer lookup each), which took ~10 ms on the main thread. A worker builds
        // them ahead for a disc around the vehicle, and samples wait until the disc covers the vehicle.
        private static readonly object PrefetchGate = new object();
        private static BodyRuntime prefetchedFor;
        private static Vec3d prefetchedAt;
        private static bool prefetching;

        /// <summary>Radius of the disc whose targets are built ahead; samples reach at most <see cref="RangeCap"/> from the centre plus <see cref="PrefetchSlack"/>.</summary>
        public const double PrefetchRadius = RangeCap + PrefetchSlack;

        /// <summary>How far the vehicle may drift from the centre of the prefetched disc before a new one is built.</summary>
        public const double PrefetchSlack = 300;

        /// <summary>
        /// Whether a sample from <paramref name="origin"/> would find its targets already built. If not, starts building
        /// them on a worker thread (one at a time) and returns false: the caller skips this sample and asks again at
        /// the next one.
        /// </summary>
        public static bool Prepare(BodyRuntime rt, Instrument inst, Vec3d origin)
        {
            lock (PrefetchGate)
            {
                if (ReferenceEquals(prefetchedFor, rt) && Vec3d.Distance(origin, prefetchedAt) <= PrefetchSlack)
                {
                    return true;
                }

                if (prefetching)
                {
                    return false;
                }

                prefetching = true;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool done = false;
                try
                {
                    ScanGeometry geometry = ScanGeometry.Ground(origin, origin.Normalized * -1);
                    int warmed = 0;
                    foreach (SubsurfaceTarget t in rt.Targets.Near(origin, PrefetchRadius))
                    {
                        // Evaluating a few also compiles the detection code before the main thread needs it.
                        if (warmed++ < 8)
                        {
                            DetectionModel.Evaluate(inst, geometry, t, null);
                        }
                    }

                    // The column targets and their code too (the terrain point is approximated by the vehicle's).
                    BodyFrame.ToLatLon(origin, out double lat, out double lon);
                    string biome = KspSampler.Biome(rt.Body, lat, lon);
                    ColumnTargets.Boundaries(rt.Strata, biome, origin, RangeCap);
                    ColumnTargets.Gas(rt.Body.bodyName, lat, lon, rt.Strata, biome, origin, RangeCap);
                    done = true;
                }
                catch (Exception e)
                {
                    Log.Warn($"Ground radar prefetch failed: {e.Message}");
                }
                finally
                {
                    lock (PrefetchGate)
                    {
                        prefetching = false;
                        if (done)
                        {
                            prefetchedFor = rt;
                            prefetchedAt = origin;
                        }
                    }
                }
            });
            return false;
        }

        // decidido por Claude, revisar: MaxDepth is infinite where no layer attenuates the signal; the target query
        // still stays within this many metres.
        public const double RangeCap = 3000;

        /// <summary>
        /// Evaluates every target the instrument could see from <paramref name="origin"/> (body coordinates) pointing
        /// along <paramref name="axis"/>: the targets of the registry within the instrument's range, plus the layer
        /// boundaries and gas pockets of the column under the point (<paramref name="lat"/>, <paramref name="lon"/>).
        /// The range is the instrument's maximum depth in that column. Only targets that were seen are returned.
        /// </summary>
        public static IReadOnlyList<(SubsurfaceTarget t, Detection d)> Sample(BodyRuntime rt, Instrument inst, Vec3d origin, Vec3d axis,
            double lat, double lon)
        {
            if (rt == null)
            {
                throw new ArgumentNullException(nameof(rt));
            }

            if (inst == null)
            {
                throw new ArgumentNullException(nameof(inst));
            }

            var watch = Stopwatch.StartNew();
            string biome = KspSampler.Biome(rt.Body, lat, lon);
            double biomeMs = watch.Elapsed.TotalMilliseconds;
            IReadOnlyList<LayerSpan> spans = rt.Strata.Spans(biome);
            double maxRange = Math.Min(RangeCap, RadarPhysics.MaxDepth(spans, inst.FrequencyMHz, inst.DynamicRangeDb));
            ScanGeometry geometry = ScanGeometry.Ground(origin, axis);
            var seen = new List<(SubsurfaceTarget t, Detection d)>();
            int considered = 0;
            SubsurfaceTarget closest = null;
            double closestDistance = double.MaxValue;
            foreach (SubsurfaceTarget t in rt.Targets.Near(origin, maxRange))
            {
                considered++;
                double distance = Vec3d.Distance(t.Position, origin);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = t;
                }

                Add(seen, inst, geometry, t, spans);
            }

            double nearMs = watch.Elapsed.TotalMilliseconds - biomeMs;
            double angle = closest == null || !(axis.Length > 0) ? 0
                : Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(closest.Position - origin, axis) / (closestDistance * axis.Length)))) * 180 / Math.PI;
            double elevation = 90 - Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(origin, axis) / Math.Max(1e-9, origin.Length * axis.Length)))) * 180 / Math.PI;
            samples++;
            LastSummary = $"sample {samples}, axis {elevation:F0} deg above the horizon, range {maxRange:F0} m, {considered} target(s) near, closest {(closest == null ? "none" : closest.Id)} at {closestDistance:F0} m, {angle:F0} deg off the axis, {seen.Count} seen";

            Vec3d surfacePoint = BodyFrame.Direction(lat, lon) * KspSampler.SurfaceRadius(rt.Body, lat, lon);
            double surfaceMs = watch.Elapsed.TotalMilliseconds - biomeMs - nearMs;
            foreach (SubsurfaceTarget t in ColumnTargets.Boundaries(rt.Strata, biome, surfacePoint, maxRange))
            {
                Add(seen, inst, geometry, t, spans);
            }

            foreach (SubsurfaceTarget t in ColumnTargets.Gas(rt.Body.bodyName, lat, lon, rt.Strata, biome, surfacePoint, maxRange))
            {
                Add(seen, inst, geometry, t, spans);
            }

            double totalMs = watch.Elapsed.TotalMilliseconds;
            LastSummary += $" [biome {biomeMs:F1} ms, targets {nearMs:F1} ms, terrain {surfaceMs:F1} ms, total {totalMs:F1} ms]";
            if (totalMs > worstMs)
            {
                worstMs = totalMs;
                WorstSummary = LastSummary;
            }

            return seen;
        }

        private static void Add(List<(SubsurfaceTarget t, Detection d)> seen, Instrument inst, ScanGeometry geometry, SubsurfaceTarget t,
            IReadOnlyList<LayerSpan> spans)
        {
            Detection d = DetectionModel.Evaluate(inst, geometry, t, spans);
            if (d.Level != DetectionLevel.None)
            {
                seen.Add((t, d));
            }
        }

        /// <summary>
        /// Puts the results of a sample into the store. Returns one finding per network that this sample took to the
        /// Detected level for the first time (so the caller shows one message per network per scan, not one per
        /// primitive): how far and in which direction its nearest target found is.
        /// </summary>
        public static List<GroundFinding> Apply(DetectionStore store, string body, IReadOnlyList<(SubsurfaceTarget t, Detection d)> seen,
            Vec3d origin, double lat, double lon)
        {
            var findings = new List<GroundFinding>();
            if (store == null || seen.Count == 0)
            {
                return findings;
            }

            // The level of each network before this sample touched it.
            var before = new Dictionary<string, DetectionLevel>();
            foreach ((SubsurfaceTarget t, Detection d) in seen)
            {
                if (string.IsNullOrEmpty(t.NetworkId) || before.ContainsKey(t.NetworkId))
                {
                    continue;
                }

                before[t.NetworkId] = store.Get(t.NetworkId)?.Level ?? DetectionLevel.None;
            }

            foreach ((SubsurfaceTarget t, Detection d) in seen)
            {
                store.Apply(body, t, d);
            }

            foreach (KeyValuePair<string, DetectionLevel> pair in before)
            {
                if (pair.Value == DetectionLevel.Detected || store.Get(pair.Key)?.Level != DetectionLevel.Detected)
                {
                    continue;
                }

                SubsurfaceTarget nearest = null;
                double best = double.MaxValue;
                foreach ((SubsurfaceTarget t, Detection d) in seen)
                {
                    if (t.NetworkId != pair.Key || d.Level != DetectionLevel.Detected)
                    {
                        continue;
                    }

                    double distance = Vec3d.Distance(origin, t.Position);
                    if (distance < best)
                    {
                        best = distance;
                        nearest = t;
                    }
                }

                if (nearest != null)
                {
                    findings.Add(new GroundFinding(pair.Key, best, Bearing.Degrees(lat, lon, nearest.Lat, nearest.Lon)));
                }
            }

            return findings;
        }
    }
}
