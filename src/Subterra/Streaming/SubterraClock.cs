using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Main-thread time spent by Subterra per frame. Components wrap their per-frame work in
    /// <c>using (SubterraClock.Measure("Name")) { … }</c>; the last 600 frames are kept, and each
    /// component's worst single frame since <see cref="ResetComponents"/>.
    /// </summary>
    public static class SubterraClock
    {
        private const int Window = 600;
        private static readonly double[] Frames = new double[Window];
        private static readonly float[] Times = new float[Window];
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        private static readonly Dictionary<string, double> ComponentFrame = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> ComponentMax = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> ComponentTotal = new Dictionary<string, double>();
        private static int count;
        private static int head;
        private static int currentFrame = -1;
        private static double currentMs;
        private static int depth;

        public static Scope Measure(string name = "Subterra")
        {
            Roll();
            depth++;
            return new Scope(Watch.Elapsed.TotalMilliseconds, name, depth == 1);
        }

        /// <summary>Average and maximum ms per frame over the last <paramref name="seconds"/> (real time).</summary>
        public static (double avg, double max, int frames) Stats(float seconds)
        {
            Roll();
            float since = Time.realtimeSinceStartup - seconds;
            double sum = 0, max = 0;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                int index = (head - 1 - i + Window) % Window;
                if (Times[index] < since)
                {
                    break;
                }

                sum += Frames[index];
                max = Math.Max(max, Frames[index]);
                n++;
            }

            return (n == 0 ? 0 : sum / n, max, n);
        }

        /// <summary>"Name max/total ms" for the components that cost most, worst first.</summary>
        public static string Breakdown(int top = 6) =>
            string.Join(", ", ComponentMax.OrderByDescending(p => p.Value).Take(top)
                .Select(p => FormattableString.Invariant($"{p.Key} {p.Value:F1}/{ComponentTotal[p.Key]:F0}")));

        public static void ResetComponents()
        {
            ComponentMax.Clear();
            ComponentTotal.Clear();
        }

        /// <summary>Closes the previous frame's total; call at least once per frame (any Measure does).</summary>
        public static void Roll()
        {
            int frame = Time.frameCount;
            if (frame == currentFrame)
            {
                return;
            }

            if (currentFrame >= 0)
            {
                // Frames without any Subterra work count as 0 ms.
                int skipped = Math.Min(Window, frame - currentFrame - 1);
                Push(currentMs);
                for (int i = 0; i < skipped; i++)
                {
                    Push(0);
                }

                foreach (var pair in ComponentFrame)
                {
                    ComponentMax.TryGetValue(pair.Key, out double max);
                    ComponentMax[pair.Key] = Math.Max(max, pair.Value);
                    ComponentTotal.TryGetValue(pair.Key, out double total);
                    ComponentTotal[pair.Key] = total + pair.Value;
                }

                ComponentFrame.Clear();
            }

            currentFrame = frame;
            currentMs = 0;
        }

        private static void Push(double ms)
        {
            Frames[head] = ms;
            Times[head] = Time.realtimeSinceStartup;
            head = (head + 1) % Window;
            count = Math.Min(Window, count + 1);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly double start;
            private readonly string name;
            private readonly bool outer;

            public Scope(double start, string name, bool outer)
            {
                this.start = start;
                this.name = name;
                this.outer = outer;
            }

            public void Dispose()
            {
                depth--;
                double elapsed = Watch.Elapsed.TotalMilliseconds - start;
                ComponentFrame.TryGetValue(name, out double sofar);
                ComponentFrame[name] = sofar + elapsed;

                // Nested scopes are already inside their parent's time.
                if (outer)
                {
                    currentMs += elapsed;
                }
            }
        }
    }
}
