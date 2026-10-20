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
        private static bool applied;

        public static IReadOnlyDictionary<string, PatchStatus> Status => status;

        public static bool IsActive(string id) => status.TryGetValue(id, out PatchStatus s) && s == PatchStatus.Applied;

        public static void Register(string id, Action<Harmony> apply)
        {
            if (Registered.Exists(r => r.id == id))
            {
                return;
            }

            Registered.Add((id, apply));
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
