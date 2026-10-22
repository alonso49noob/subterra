using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Subterra.Core.Radar;

namespace Subterra
{
    /// <summary>
    /// The abundance of the <c>SubterraCavity</c> resource (spec 4.7b): the detection confidence of this save's radar
    /// at a point, 0 where Subterra never found anything. <see cref="ResourcePatch"/> returns it from
    /// <c>ResourceMap.GetAbundance</c>, which is how SCANsat draws its "Subterra" layer.
    ///
    /// SCANsat asks for thousands of points when it draws a map, so <see cref="At"/> is a lookup in a raster per body
    /// (<see cref="Width"/> x <see cref="Height"/> bytes, the same cells as the coverage grid, made by
    /// <see cref="OverlayPainter.Confidence"/>). The raster is rebuilt, on the calling (main) thread, the first time a
    /// body is asked for and again the first time it is asked for after the detections changed
    /// (<see cref="DetectionStore.Version"/>, or another store because the scene or the save changed); never per call.
    /// Bodies without detections cost no raster at all.
    /// </summary>
    public static class CavityAbundance
    {
        public const string ResourceName = "SubterraCavity";

        public const int Width = CoverageGrid.Width;
        public const int Height = CoverageGrid.Height;

        private sealed class Entry
        {
            public DetectionStore Store;
            public int Version = -1;

            /// <summary>Null while the body has no detections (every point is 0).</summary>
            public byte[] Raster;
        }

        /// <summary>The exception <see cref="FailNext"/> makes <see cref="At"/> throw (tests of the failure path).</summary>
        public sealed class InjectedFault : Exception
        {
            public InjectedFault() : base("cavity lookup failure requested by a test")
            {
            }
        }

        private static int debugFailures;

        /// <summary>Makes the next <paramref name="count"/> calls of <see cref="At"/> throw an <see cref="InjectedFault"/>.</summary>
        public static void FailNext(int count) => System.Threading.Interlocked.Exchange(ref debugFailures, count);

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();

        /// <summary>Rasters built since the game started (diagnostics for the tests).</summary>
        public static int Builds { get; private set; }

        /// <summary>Milliseconds the last build took (diagnostics for the tests).</summary>
        public static double LastBuildMs { get; private set; }

        /// <summary>
        /// The confidence (0 to 1; a hint is <see cref="OverlayPainter.HintConfidence"/> / 255, a detection 1) of the
        /// body at that latitude and longitude (degrees; any longitude range). 0 for a body without detections,
        /// without a Subterra radar save (no <see cref="RadarScenario"/>) or unknown.
        /// </summary>
        public static float At(string body, double lat, double lon)
        {
            if (System.Threading.Interlocked.Decrement(ref debugFailures) >= 0)
            {
                throw new InjectedFault();
            }

            System.Threading.Interlocked.Exchange(ref debugFailures, 0);
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null || string.IsNullOrEmpty(body) || double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lon))
            {
                return 0f;
            }

            byte[] raster = RasterOf(body, scenario.Detections);
            if (raster == null)
            {
                return 0f;
            }

            return raster[OverlayPainter.RowOf(Height, lat) * Width + OverlayPainter.ColumnOf(Width, lon)] / 255f;
        }

        /// <summary>Forgets every raster (tests; the next call rebuilds).</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        private static byte[] RasterOf(string body, DetectionStore store)
        {
            lock (Gate)
            {
                if (!Cache.TryGetValue(body, out Entry entry))
                {
                    entry = new Entry();
                    Cache[body] = entry;
                }

                // The version is read before the records: a change between the two raises it again and the next
                // call rebuilds, so the raster is never newer than the version it is filed under.
                int version = store.Version;
                if (entry.Store != store || entry.Version != version)
                {
                    Rebuild(entry, body, store, version);
                }

                return entry.Raster;
            }
        }

        private static void Rebuild(Entry entry, string body, DetectionStore store, int version)
        {
            var watch = Stopwatch.StartNew();
            using (SubterraClock.Measure("CavityRaster"))
            {
                List<NetworkDetection> detections = store.All.Where(d => d.Body == body && d.Level != DetectionLevel.None).ToList();
                CelestialBody celestial = detections.Count > 0 ? FlightGlobals.GetBodyByName(body) : null;
                byte[] raster = null;
                if (celestial != null)
                {
                    // The radius turns an uncertainty in metres into a size on the sphere, as the map overlay does.
                    raster = OverlayPainter.Confidence(Width, Height, detections, celestial.Radius);
                }

                entry.Store = store;
                entry.Version = version;
                entry.Raster = raster;
            }

            Builds++;
            LastBuildMs = watch.Elapsed.TotalMilliseconds;
            if (entry.Raster != null && (Builds <= 1 || LastBuildMs > 5))
            {
                Log.Info(FormattableString.Invariant($"CavityAbundance: raster of {body} rebuilt in {LastBuildMs:F2} ms"));
            }
        }
    }
}
