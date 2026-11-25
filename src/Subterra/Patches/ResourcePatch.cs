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
    /// The same postfix gives <c>SubterraCavity</c> (the radar's layer for SCANsat) its abundance from this save's
    /// detections, see <see cref="CavityAbundance"/>.
    /// </summary>
    public static class ResourcePatch
    {
        public const string Id = "resources";
        private static readonly Dictionary<string, List<EnrichmentSpec>> Rules = new Dictionary<string, List<EnrichmentSpec>>();

        public static void Register()
        {
            PatchManager.Inactive += id =>
            {
                if (id == Id)
                {
                    // R31: SubterraCavity's distribution is not empty (SCANsat would hide it), so without this patch
                    // stock's own value (0.1 to 0.8 everywhere) is what SCANsat and the KerbNet chart would show.
                    Log.Warn("The 'resources' patch is not active: the SubterraCavity layer will show stock values (not radar detections) in SCANsat and the KerbNet chart until the patch is active again");
                }
            };
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

        /// <summary>
        /// The confidence of the radar's detections at the request's point. decidido por Claude, revisar: a request
        /// with ExcludeVariance is the stock summary (ResourceCache builds the KerbNet resource chart from requests at
        /// latitude 0, longitude 0 with it); answering 0 keeps SubterraCavity out of that chart, which lists only
        /// resources with a positive summary. SCANsat and the part readouts ask without it.
        /// </summary>
        private static float Cavity(AbundanceRequest request)
        {
            if (request.ExcludeVariance || request.BodyId < 0 || request.BodyId >= FlightGlobals.Bodies.Count)
            {
                return 0f;
            }

            return CavityAbundance.At(FlightGlobals.Bodies[request.BodyId].bodyName, request.Latitude, request.Longitude);
        }

        private static void Postfix(AbundanceRequest request, ref float __result)
        {
            try
            {
                if (request.ResourceName == CavityAbundance.ResourceName)
                {
                    __result = Cavity(request);
                    return;
                }

                __result = (float)Enrich(request, __result);
            }
            catch (Exception e)
            {
                // Always answer 0 for SubterraCavity (R31): the value the original computed is stock's, not detections.
                if (request.ResourceName == CavityAbundance.ResourceName)
                {
                    __result = 0f;
                }

                if (e is CavityAbundance.InjectedFault)
                {
                    // A failure a test asked for is not an error (the scenario checks the log for errors).
                    Log.Warn("ResourcePatch: " + e.Message);
                }
                else
                {
                    PatchManager.Fault(Id, e);
                }
            }
        }
    }
}
