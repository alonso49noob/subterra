using System;
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
                CellScheduler.NetworksAdded += AddNetworks;
            }

            AddNetworks(runtime, runtime.Catalog.Named);
        }

        /// <summary>True while the terrain over that skylight is cut and on screen.</summary>
        public static bool IsOpen(CelestialBody body, TubeNetwork net, Skylight skylight)
        {
            PQSMod_SubterraSkylight mod = ModFor(body.pqsController);
            return mod != null && HoleIndex.TryGetValue((net.Id, skylight.Index), out int index) && mod.IsOpen(index);
        }

        public static PQSMod_SubterraSkylight ModFor(PQS pqs) => pqs != null && Mods.TryGetValue(pqs, out PQSMod_SubterraSkylight mod) ? mod : null;

        // All new skylights of a batch of networks (a cell, or the named sites) cut in one pass over the quads.
        internal static void AddNetworks(BodyRuntime runtime, IEnumerable<TubeNetwork> nets)
        {
            using (SubterraClock.Measure("SkylightInjector"))
            {
                var added = new List<(Vector3d, Vector3, float, bool, Func<Vector3d, float>)>();
                var keys = new List<(string, int)>();
                foreach (TubeNetwork net in nets)
                {
                    if (net.Skylights.Count == 0 || !Added.Add(runtime.Body.bodyName + "/" + net.Id))
                    {
                        continue;
                    }

                    foreach (Skylight skylight in net.Skylights)
                    {
                        // Openings are clipped into the terrain itself: round ones (natural skylights, blast shafts) as circles, dug
                        // mouths along the line where their tunnel meets the ground (which follows the tunnel as it grows).
                        // With terrainClip off, whole terrain triangles are removed and a rim closes them.
                        bool clip = Settings.Current.TerrainClip;
                        Func<Vector3d, float> field = null;
                        float bound = skylight.Radius;
                        if (clip && skylight.Smooth)
                        {
                            var mouth = new Mouth { Net = net, Up = skylight.Up };
                            Mouths[MouthKey(runtime, skylight.Owner ?? net.Id, skylight.Index)] = mouth;
                            field = mouth.Field;
                            bound = skylight.Radius + MouthGrowth;
                        }

                        added.Add((net.ToBody(skylight.SurfaceCenter).ToKsp(), skylight.Up.ToUnity(), bound, clip, field));
                        keys.Add((net.Id, skylight.Index));
                    }
                }

                if (added.Count == 0)
                {
                    return;
                }

                var watch = System.Diagnostics.Stopwatch.StartNew();
                PQSMod_SubterraSkylight mod = Ensure(runtime.Body.pqsController);
                int[] indices = mod.AddHoles(added);
                for (int i = 0; i < keys.Count; i++)
                {
                    HoleIndex[keys[i]] = indices[i];
                }

                if (watch.Elapsed.TotalMilliseconds > 5)
                {
                    Log.Info($"Skylights: {added.Count} added on {runtime.Body.bodyName} ({mod.HoleCount} total, {mod.CutQuads} quads cut) in {watch.Elapsed.TotalMilliseconds:F1} ms");
                }
            }
        }

        /// <summary>How far (m) a dug mouth's opening may grow past its first size (the trench lengthens as the tunnel goes on).</summary>
        private const float MouthGrowth = 30f;

        /// <summary>Depth (m) under the ground the tunnel must reach for the ground to open (its walls are clipped just under it).</summary>
        private const float OpenDepth = 0.15f;

        private static readonly Dictionary<string, Mouth> Mouths = new Dictionary<string, Mouth>();

        private static string MouthKey(BodyRuntime runtime, string owner, int index) => runtime.Body.bodyName + "/" + owner + "/" + index;

        // A dug mouth's shape: where its tunnel (as it is now) reaches OpenDepth under the ground.
        private sealed class Mouth
        {
            public TubeNetwork Net;
            public Core.Numerics.Vec3 Up;

            public float Field(Vector3d bodyPos)
            {
                TubeNetwork net = Net;
                Core.Numerics.Vec3d p = bodyPos.ToCore() - Core.Numerics.Vec3d.FromVec3(Up) * OpenDepth;
                return net.Sdf.Distance(net.ToLocal(p));
            }
        }

        /// <summary>The tunnel of a dug mouth changed: its opening follows it (the ground is cut again around it).</summary>
        internal static void RefreshMouth(BodyRuntime runtime, TubeNetwork tunnel)
        {
            foreach (Skylight skylight in tunnel.Skylights)
            {
                string owner = skylight.Owner ?? tunnel.Id;
                if (!skylight.Smooth || !Mouths.TryGetValue(MouthKey(runtime, owner, skylight.Index), out Mouth mouth))
                {
                    continue;
                }

                mouth.Net = tunnel;
                PQSMod_SubterraSkylight mod = ModFor(runtime.Body.pqsController);
                if (mod != null && HoleIndex.TryGetValue((tunnel.Id, skylight.Index), out int index))
                {
                    using (SubterraClock.Measure("SkylightInjector"))
                    {
                        mod.Refresh(index);
                    }
                }
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
