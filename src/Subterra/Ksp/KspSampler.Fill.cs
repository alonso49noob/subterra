using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Subterra
{
    public static partial class KspSampler
    {
        /// <summary>
        /// Samples height and biome at each point, a slice of <paramref name="budgetMs"/> per frame
        /// (counted on <see cref="SubterraClock"/>). Run as a coroutine on the main thread.
        /// </summary>
        public static IEnumerator Fill(CelestialBody body, IReadOnlyList<(double lat, double lon)> pts, Action<int, double, string> onSample, float budgetMs)
        {
            int i = 0;
            while (i < pts.Count)
            {
                using (SubterraClock.Measure("KspSampler"))
                {
                    var watch = Stopwatch.StartNew();
                    while (i < pts.Count && watch.Elapsed.TotalMilliseconds < budgetMs)
                    {
                        var (lat, lon) = pts[i];
                        onSample(i, SurfaceRadius(body, lat, lon), Biome(body, lat, lon));
                        i++;
                    }
                }

                if (i < pts.Count)
                {
                    yield return null;
                }
            }
        }
    }
}
