using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Subterra
{
    public static partial class KspSampler
    {
        // The slice is shared by every Fill running in the same frame (several cells generate at once).
        private static int budgetFrame = -1;
        private static double usedThisFrame;

        /// <summary>
        /// Samples height and biome at each point, at most <paramref name="budgetMs"/> per frame for all the
        /// fills running together (counted on <see cref="SubterraClock"/>). Run as a coroutine on the main thread.
        /// </summary>
        public static IEnumerator Fill(CelestialBody body, IReadOnlyList<(double lat, double lon)> pts, Action<int, double, string> onSample, float budgetMs)
        {
            int i = 0;
            while (i < pts.Count)
            {
                int frame = UnityEngine.Time.frameCount;
                if (frame != budgetFrame)
                {
                    budgetFrame = frame;
                    usedThisFrame = 0;
                }

                using (SubterraClock.Measure("KspSampler"))
                {
                    var watch = Stopwatch.StartNew();
                    while (i < pts.Count && usedThisFrame + watch.Elapsed.TotalMilliseconds < budgetMs)
                    {
                        var (lat, lon) = pts[i];
                        onSample(i, SurfaceRadius(body, lat, lon), Biome(body, lat, lon));
                        i++;
                    }

                    usedThisFrame += watch.Elapsed.TotalMilliseconds;
                }

                if (i < pts.Count)
                {
                    yield return null;
                }
            }
        }
    }
}
