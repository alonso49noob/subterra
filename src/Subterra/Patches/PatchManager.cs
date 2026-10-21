using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Subterra
{
    public enum PatchStatus
    {
        Applied,
        Failed,
        Disabled,
    }

    /// <summary>
    /// Each Harmony patch group is applied on its own (one Harmony id each); a group that throws is
    /// unpatched and marked Failed, the others stay. Groups listed in Settings.DisabledPatches are skipped.
    /// </summary>
    public static class PatchManager
    {
        private static readonly List<(string id, Action<Harmony> apply)> Registered = new List<(string, Action<Harmony>)>();
        private static readonly Dictionary<string, PatchStatus> status = new Dictionary<string, PatchStatus>();
        private static readonly Dictionary<string, Harmony> instances = new Dictionary<string, Harmony>();
        private static readonly Dictionary<string, int> faults = new Dictionary<string, int>();

        /// <summary>Faults after which a patch group is switched off.</summary>
        public const int MaxFaults = 20;
        private static bool applied;

        public static IReadOnlyDictionary<string, PatchStatus> Status => status;

        /// <summary>
        /// Called by a patch body that caught an exception: logs the first few, and after
        /// <see cref="MaxFaults"/> switches the group off (the game then runs unpatched code).
        /// </summary>
        public static void Fault(string id, Exception e)
        {
            faults.TryGetValue(id, out int n);
            faults[id] = ++n;
            if (n <= 3)
            {
                Log.Error($"Patch '{id}' threw ({n}): {e}");
            }

            if (n == MaxFaults && instances.TryGetValue(id, out Harmony harmony))
            {
                status[id] = PatchStatus.Failed;
                Log.Error($"Patch '{id}' switched off after {MaxFaults} errors");
                try
                {
                    harmony.UnpatchAll(harmony.Id);
                }
                catch (Exception)
                {
                    // Nothing more to undo.
                }
            }
        }

        public static bool IsActive(string id) => status.TryGetValue(id, out PatchStatus s) && s == PatchStatus.Applied;

        public static void Register(string id, Action<Harmony> apply)
        {
            if (Registered.Exists(r => r.id == id))
            {
                return;
            }

            Registered.Add((id, apply));
        }

        /// <summary>Registers and applies one group now (for optional assemblies loaded after ApplyAll).</summary>
        public static void ApplyNow(string id, Action<Harmony> apply)
        {
            if (Settings.Current.DisabledPatches.Contains(id))
            {
                status[id] = PatchStatus.Disabled;
                Log.Info($"Patch '{id}' disabled in Settings.cfg");
                return;
            }

            var harmony = new Harmony("Subterra." + id);
            try
            {
                apply(harmony);
                status[id] = PatchStatus.Applied;
                instances[id] = harmony;
            }
            catch (Exception e)
            {
                status[id] = PatchStatus.Failed;
                Log.Error($"Patch '{id}' failed and is off: {e.GetType().Name}: {e.Message}");
                try
                {
                    harmony.UnpatchAll(harmony.Id);
                }
                catch (Exception)
                {
                    // Nothing more to undo.
                }
            }
        }

        public static void ApplyAll()
        {
            if (applied)
            {
                return;
            }

            applied = true;
            foreach (var (id, apply) in Registered)
            {
                if (Settings.Current.DisabledPatches.Contains(id))
                {
                    status[id] = PatchStatus.Disabled;
                    Log.Info($"Patch '{id}' disabled in Settings.cfg");
                    continue;
                }

                var harmony = new Harmony("Subterra." + id);
                try
                {
                    apply(harmony);
                    status[id] = PatchStatus.Applied;
                    instances[id] = harmony;
                }
                catch (Exception e)
                {
                    status[id] = PatchStatus.Failed;
                    Log.Error($"Patch '{id}' failed and is off: {e.GetType().Name}: {e.Message}");
                    try
                    {
                        harmony.UnpatchAll(harmony.Id);
                    }
                    catch (Exception)
                    {
                        // Nothing more to undo.
                    }
                }
            }
        }
    }
}
