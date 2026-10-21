using System;
using System.Collections.Generic;
using HarmonyLib;
using Subterra.Core.Gameplay;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;

namespace Subterra
{
    /// <summary>
    /// Patch <c>resources</c>: abundance requests whose point (latitude, longitude, altitude) lies in a
    /// cave get the body's ENRICHMENT rules for that zone and rock layer. Stock drills work inside.
    /// Rules for resources the game does not know are dropped at load (one warning each).
    /// </summary>
    public static class ResourcePatch
    {
        public const string Id = "resources";
        private static readonly Dictionary<string, List<EnrichmentSpec>> Rules = new Dictionary<string, List<EnrichmentSpec>>();

        public static void Register()
        {
            PatchManager.Register(Id, harmony => harmony.Patch(
                AccessTools.Method(typeof(ResourceMap), nameof(ResourceMap.GetAbundance), new[] { typeof(AbundanceRequest) }),
                postfix: new HarmonyMethod(typeof(ResourcePatch), nameof(Postfix))));
        }

        /// <summary>Keeps the rules whose resource exists (called by the loader once resources are loaded).</summary>
        public static void Prepare(BodyRuntime runtime)
        {
            var kept = new List<EnrichmentSpec>();
            var warned = new HashSet<string>();
            foreach (EnrichmentSpec rule in runtime.Spec.Enrichment)
            {
                if (PartResourceLibrary.Instance != null && PartResourceLibrary.Instance.GetDefinition(rule.Resource) != null)
                {
                    kept.Add(rule);
                }
                else if (warned.Add(rule.Resource))
                {
                    Log.Warn($"{runtime.Body.bodyName}: ignoring enrichment for unknown resource '{rule.Resource}'");
                }
            }

            Rules[runtime.Body.bodyName] = kept;
        }

        /// <summary>The enriched abundance for a request (the base value outside caves).</summary>
        public static double Enrich(AbundanceRequest request, double baseAbundance)
        {
            if (request.BodyId < 0 || request.BodyId >= FlightGlobals.Bodies.Count)
            {
                return baseAbundance;
            }

            CelestialBody body = FlightGlobals.Bodies[request.BodyId];
            if (!Rules.TryGetValue(body.bodyName, out List<EnrichmentSpec> rules) || rules.Count == 0
                || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                return baseAbundance;
            }

            Vec3d position = BodyFrame.Direction(request.Latitude, request.Longitude) * (body.Radius + request.Altitude);
            TubeNetwork net = runtime.Catalog.FindContaining(position);
            if (net == null)
            {
                return baseAbundance;
            }

            Vec3 local = net.ToLocal(position);
            ZoneKind zone = net.ZoneAt(local).Kind;
            double depth = Math.Max(0, net.SurfaceRadiusNear(local) - position.Length);
            // The surface biome from the map: request.BiomeName may already be a cave biome (science patch).
            string layer = runtime.Strata.LayerAt(depth, KspSampler.Biome(body, request.Latitude, request.Longitude))?.Name;
            return Enrichment.Apply(rules, request.ResourceName, zone, layer, baseAbundance);
        }

        private static void Postfix(AbundanceRequest request, ref float __result)
        {
            try
            {
                __result = (float)Enrich(request, __result);
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }
    }
}
