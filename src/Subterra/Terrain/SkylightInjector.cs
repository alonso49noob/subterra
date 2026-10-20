using System.Collections.Generic;
using HarmonyLib;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Adds <see cref="PQSMod_SubterraSkylight"/> to bodies with skylights (at load) and feeds it the
    /// skylights of named sites and of procedural cells as they are generated. Existing quads are cut
    /// directly, so no sphere rebuild is needed.
    /// </summary>
    public static class SkylightInjector
    {
        private static readonly Dictionary<PQS, PQSMod_SubterraSkylight> Mods = new Dictionary<PQS, PQSMod_SubterraSkylight>();
        private static readonly HashSet<string> Added = new HashSet<string>();
        private static readonly Dictionary<(string, int), int> HoleIndex = new Dictionary<(string, int), int>();
        private static bool subscribed;

        public static IReadOnlyDictionary<PQS, PQSMod_SubterraSkylight> All => Mods;

        public static void Inject(BodyRuntime runtime)
        {
            if (!subscribed)
            {
                subscribed = true;
                CellScheduler.NetworksAdded += (r, nets) =>
                {
                    foreach (TubeNetwork net in nets)
                    {
                        AddNetwork(r, net);
                    }
                };
            }

            foreach (TubeNetwork net in runtime.Catalog.Named)
            {
                AddNetwork(runtime, net);
            }
        }

        /// <summary>True while the terrain over that skylight is cut and on screen.</summary>
        public static bool IsOpen(CelestialBody body, TubeNetwork net, Skylight skylight)
        {
            PQSMod_SubterraSkylight mod = ModFor(body.pqsController);
            return mod != null && HoleIndex.TryGetValue((net.Id, skylight.Index), out int index) && mod.IsOpen(index);
        }

        public static PQSMod_SubterraSkylight ModFor(PQS pqs) => pqs != null && Mods.TryGetValue(pqs, out PQSMod_SubterraSkylight mod) ? mod : null;

        private static void AddNetwork(BodyRuntime runtime, TubeNetwork net)
        {
            if (net.Skylights.Count == 0 || !Added.Add(runtime.Body.bodyName + "/" + net.Id))
            {
                return;
            }

            using (SubterraClock.Measure("SkylightInjector"))
            {
                AddHoles(runtime, net);
            }
        }

        private static void AddHoles(BodyRuntime runtime, TubeNetwork net)
        {
            PQSMod_SubterraSkylight mod = Ensure(runtime.Body.pqsController);
            foreach (Skylight skylight in net.Skylights)
            {
                int index = mod.AddHole(net.ToBody(skylight.SurfaceCenter).ToKsp(), skylight.Up.ToUnity(), skylight.Radius);
                HoleIndex[(net.Id, skylight.Index)] = index;
            }
        }

        private static PQSMod_SubterraSkylight Ensure(PQS pqs)
        {
            if (Mods.TryGetValue(pqs, out PQSMod_SubterraSkylight mod) && mod != null)
            {
                return mod;
            }

            var go = new GameObject("SubterraSkylights");
            go.transform.SetParent(pqs.transform, false);
            mod = go.AddComponent<PQSMod_SubterraSkylight>();
            mod.sphere = pqs;
            mod.order = PQSMod_SubterraSkylight.ModOrder;
            mod.modEnabled = true;
            AccessTools.Method(typeof(PQS), "SetupMods").Invoke(pqs, null);
            Mods[pqs] = mod;
            return mod;
        }
    }
}
