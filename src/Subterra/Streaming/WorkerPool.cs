using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Subterra
{
    /// <summary>
    /// Background threads (below normal priority, cores − 2) for meshing and generation, plus a queue
    /// of actions that must run on the main thread within a time budget.
    /// </summary>
    public sealed class WorkerPool : IDisposable
    {
        private static WorkerPool shared;
        private readonly Queue<Action> jobs = new Queue<Action>();
        private readonly Queue<Action> mainActions = new Queue<Action>();
        private readonly List<Thread> threads = new List<Thread>();
        private volatile bool disposed;
        private int running;

        public WorkerPool(int threadCount)
        {
            for (int i = 0; i < Math.Max(1, threadCount); i++)
            {
                var thread = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Subterra worker " + i };
                threads.Add(thread);
                thread.Start();
            }
        }

        public static WorkerPool Shared => shared ?? (shared = new WorkerPool(Math.Max(1, Environment.ProcessorCount - 2)));

        /// <summary>Jobs waiting or running.</summary>
        public int Pending
        {
            get
            {
                lock (jobs)
                {
                    return jobs.Count + running;
                }
            }
        }

        public int PendingMain
        {
            get
            {
                lock (mainActions)
                {
                    return mainActions.Count;
                }
            }
        }

        public void Enqueue(Action job)
        {
            lock (jobs)
            {
                jobs.Enqueue(job);
                Monitor.Pulse(jobs);
            }
        }

        public void RunOnMain(Action action)
        {
            lock (mainActions)
            {
                mainActions.Enqueue(action);
            }
        }

        /// <summary>Runs queued main-thread actions until the queue is empty or the budget is spent (at least one runs).</summary>
        public void PumpMain(float budgetMs)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Action action;
                lock (mainActions)
                {
                    if (mainActions.Count == 0)
                    {
                        return;
                    }

                    action = mainActions.Dequeue();
                }

                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log.Error($"Main-thread task failed: {e}");
                }

                if (watch.Elapsed.TotalMilliseconds >= budgetMs)
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            disposed = true;
            lock (jobs)
            {
                Monitor.PulseAll(jobs);
            }
        }

        private void Work()
        {
            while (!disposed)
            {
                Action job;
                lock (jobs)
                {
                    while (jobs.Count == 0 && !disposed)
                    {
                        Monitor.Wait(jobs);
                    }

                    if (disposed)
                    {
                        return;
                    }

                    job = jobs.Dequeue();
                    running++;
                }

                try
                {
                    job();
                }
                catch (Exception e)
                {
                    Log.Error($"Worker task failed: {e}");
                }
                finally
                {
                    lock (jobs)
                    {
                        running--;
                    }
                }
            }
        }
    }
}
