using System;
using HarmonyLib;
using KSP.Localization;
using Subterra.Core.Gameplay;
using Subterra.Core.Model;

namespace Subterra
{
    /// <summary>
    /// Patch <c>science</c>. Experiments ask ScienceUtil for the biome at the vessel's latitude and
    /// longitude; when those match a loaded vessel inside a cave (to 1e-7°) the cave biome is returned
    /// (zone kind, or the named site). Cave subjects are worth the body's cave/site multiplier.
    /// Works for stock and mod experiments that pass the vessel's own coordinates.
    /// </summary>
    public static class SciencePatches
    {
        public const string Id = "science";
        private const double Tolerance = 1e-7;

        public static void Register()
        {
            PatchManager.Register(Id, harmony =>
            {
                Type util = typeof(ScienceUtil);
                harmony.Patch(AccessTools.Method(util, nameof(ScienceUtil.GetExperimentBiome), new[] { typeof(CelestialBody), typeof(double), typeof(double) }),
                    prefix: new HarmonyMethod(typeof(SciencePatches), nameof(BiomePrefix)));
                harmony.Patch(AccessTools.Method(util, nameof(ScienceUtil.GetExperimentBiomeLocalized), new[] { typeof(CelestialBody), typeof(double), typeof(double) }),
                    prefix: new HarmonyMethod(typeof(SciencePatches), nameof(BiomeLocalizedPrefix)));
                harmony.Patch(AccessTools.Method(util, nameof(ScienceUtil.GetBiomedisplayName), new[] { typeof(CelestialBody), typeof(string) }),
                    prefix: new HarmonyMethod(typeof(SciencePatches), nameof(DisplayNamePrefix)));
                harmony.Patch(AccessTools.Method(util, nameof(ScienceUtil.GetBiomedisplayName), new[] { typeof(CelestialBody), typeof(string), typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(SciencePatches), nameof(DisplayNamePrefix)));
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(ResearchAndDevelopment)))
                {
                    if (method.Name == nameof(ResearchAndDevelopment.GetExperimentSubject) && method.ReturnType == typeof(ScienceSubject))
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(SciencePatches), nameof(SubjectPostfix)));
                    }
                }
            });
        }

        /// <summary>The cave biome id of a loaded vessel at exactly that latitude/longitude, or null.</summary>
        public static string CaveBiomeAt(CelestialBody body, double lat, double lon)
        {
            if (body == null || !FlightGlobals.ready)
            {
                return null;
            }

            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.mainBody != body || Math.Abs(v.latitude - lat) > Tolerance || Math.Abs(v.longitude - lon) > Tolerance)
                {
                    continue;
                }

                UndergroundState state = UndergroundTracker.For(v);
                if (state.Inside)
                {
                    return BiomeIds.For(state.Zone);
                }
            }

            return null;
        }

        /// <summary>Localized name of a cave biome id (zone or named site).</summary>
        public static string DisplayName(CelestialBody body, string biomeId)
        {
            if (BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                SiteSpec site = runtime.Spec.Sites.Find(s => s.Name == biomeId);
                if (site != null && !string.IsNullOrEmpty(site.DisplayName))
                {
                    return Localizer.Format(site.DisplayName);
                }
            }

            return Localizer.Format(BiomeIds.LocKey(biomeId));
        }

        private static bool BiomePrefix(CelestialBody body, double lat, double lon, ref string __result)
        {
            try
            {
                string biome = CaveBiomeAt(body, lat, lon);
                if (biome == null)
                {
                    return true;
                }

                __result = biome;
                return false;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
                return true;
            }
        }

        private static bool BiomeLocalizedPrefix(CelestialBody body, double lat, double lon, ref string __result)
        {
            try
            {
                string biome = CaveBiomeAt(body, lat, lon);
                if (biome == null)
                {
                    return true;
                }

                __result = DisplayName(body, biome);
                return false;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
                return true;
            }
        }

        private static bool DisplayNamePrefix(CelestialBody body, string biome, ref string __result)
        {
            try
            {
                if (!IsCave(body, biome))
                {
                    return true;
                }

                __result = DisplayName(body, biome);
                return false;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
                return true;
            }
        }

        private static void SubjectPostfix(ScienceSubject __result, ScienceExperiment experiment, ExperimentSituations situation, CelestialBody body, string biome)
        {
            try
            {
                if (__result == null || experiment == null || !IsCave(body, biome) || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
                {
                    return;
                }

                // Idempotent: computed from the body's base value, not from the current subject value.
                float value = (float)(SituationValue(body.scienceValues, situation) * BiomeIds.Multiplier(biome, runtime.Spec));
                __result.subjectValue = value;
                __result.scienceCap = experiment.scienceCap * value;
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        private static bool IsCave(CelestialBody body, string biome) =>
            BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) && BiomeIds.IsSubterra(biome, runtime.Spec);

        public static float SituationValue(CelestialBodyScienceParams p, ExperimentSituations situation)
        {
            switch (situation)
            {
                case ExperimentSituations.SrfLanded: return p.LandedDataValue;
                case ExperimentSituations.SrfSplashed: return p.SplashedDataValue;
                case ExperimentSituations.FlyingLow: return p.FlyingLowDataValue;
                case ExperimentSituations.FlyingHigh: return p.FlyingHighDataValue;
                case ExperimentSituations.InSpaceLow: return p.InSpaceLowDataValue;
                default: return p.InSpaceHighDataValue;
            }
        }
    }
}
