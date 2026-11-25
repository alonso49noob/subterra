using System.Collections.Generic;
using Subterra.Core.Radar;

namespace Subterra
{
    /// <summary>
    /// The hook parts 4 (subsurface oceans) and 5 (deep oceans) use to give the radar their own targets. A source
    /// registered here is queried along with the cave networks of the body.
    /// </summary>
    public static class SubterraRadarApi
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, List<ISubsurfaceTargetSource>> Pending =
            new Dictionary<string, List<ISubsurfaceTargetSource>>();

        /// <summary>
        /// Adds a target source to a body. Safe to call before Subterra has loaded: the source then waits until the
        /// body's runtime is created. A body that Subterra does not run on gets a warning and the source is dropped.
        /// </summary>
        public static void RegisterSource(string bodyName, ISubsurfaceTargetSource source)
        {
            if (string.IsNullOrEmpty(bodyName) || source == null)
            {
                Log.Warn("RegisterSource ignored: body name or source is null");
                return;
            }

            lock (Gate)
            {
                if (BodyRegistry.Instance.Bodies.TryGetValue(bodyName, out BodyRuntime runtime))
                {
                    runtime.Targets.Register(source);
                    return;
                }

                if (SubterraLoader.Loaded)
                {
                    Log.Warn($"RegisterSource: Subterra is not active on body '{bodyName}' (unknown or disabled); source ignored");
                    return;
                }

                // Before the loader has run the body list may not exist yet: wait, and report leftovers when it is done.
                if (!Pending.TryGetValue(bodyName, out List<ISubsurfaceTargetSource> list))
                {
                    list = new List<ISubsurfaceTargetSource>();
                    Pending[bodyName] = list;
                }

                list.Add(source);
            }
        }

        /// <summary>Called by the loader when a body's runtime is created: attaches the sources that were waiting.</summary>
        internal static void Attach(BodyRuntime runtime)
        {
            lock (Gate)
            {
                string name = runtime.Body.bodyName;
                if (Pending.TryGetValue(name, out List<ISubsurfaceTargetSource> list))
                {
                    foreach (ISubsurfaceTargetSource source in list)
                    {
                        runtime.Targets.Register(source);
                    }

                    Pending.Remove(name);
                }
            }
        }

        /// <summary>Called by the loader when all bodies are loaded: sources still waiting have no body to go to.</summary>
        internal static void WarnUnattached()
        {
            lock (Gate)
            {
                foreach (KeyValuePair<string, List<ISubsurfaceTargetSource>> pair in Pending)
                {
                    Log.Warn($"RegisterSource: {pair.Value.Count} source(s) for body '{pair.Key}' dropped (unknown body, or Subterra is not active on it)");
                }

                Pending.Clear();
            }
        }
    }
}
