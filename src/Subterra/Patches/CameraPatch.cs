using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Patch <c>camera</c>. FlightCamera.UpdateCameraTransform keeps the camera above the first terrain
    /// hit of a ray cast from <c>distance</c> above the pivot; under ground that hit is the surface
    /// overhead and the camera is pushed out. When the pivot is in a cave the ray starts at the pivot
    /// (the cave floor below becomes the reference) and the minimum heights are lifted for that call.
    /// </summary>
    public static class CameraPatch
    {
        public const string Id = "camera";
        private static float savedMin, savedMax;
        private static bool overridden;
        private static bool pivotUnderground;

        public static void Register()
        {
            PatchManager.Register(Id, harmony =>
            {
                MethodInfo update = AccessTools.Method(typeof(FlightCamera), "UpdateCameraTransform")
                    ?? throw new System.MissingMethodException("FlightCamera", "UpdateCameraTransform");
                harmony.Patch(update,
                    prefix: new HarmonyMethod(typeof(CameraPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(CameraPatch), nameof(Postfix)),
                    finalizer: new HarmonyMethod(typeof(CameraPatch), nameof(Finalizer)),
                    transpiler: new HarmonyMethod(typeof(CameraPatch), nameof(Transpiler)));
            });
        }

        private static void Prefix(FlightCamera __instance)
        {
            try
            {
                pivotUnderground = FlightGlobals.ready
                    && Underground.Locate(FlightGlobals.currentMainBody, __instance.GetPivot().position, out _, out _);
            }
            catch (System.Exception e)
            {
                pivotUnderground = false;
                PatchManager.Fault(Id, e);
            }

            if (!pivotUnderground)
            {
                return;
            }

            savedMin = __instance.minHeightAtMinDist;
            savedMax = __instance.minHeightAtMaxDist;
            __instance.minHeightAtMinDist = -10000f;
            __instance.minHeightAtMaxDist = -10000f;
            overridden = true;
        }

        private static void Postfix(FlightCamera __instance)
        {
            if (overridden)
            {
                __instance.minHeightAtMinDist = savedMin;
                __instance.minHeightAtMaxDist = savedMax;
                overridden = false;
            }

            pivotUnderground = false;
        }

        // Runs even when UpdateCameraTransform throws (the postfix would not): never leave the minimum heights at -10000.
        private static System.Exception Finalizer(FlightCamera __instance, System.Exception __exception)
        {
            Postfix(__instance);
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo raycast = AccessTools.Method(typeof(Physics), nameof(Physics.Raycast),
                new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
            MethodInfo replacement = AccessTools.Method(typeof(CameraPatch), nameof(TerrainRaycast));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(raycast))
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, replacement) { labels = instruction.labels, blocks = instruction.blocks };
                    continue;
                }

                yield return instruction;
            }

            if (replaced != 1)
            {
                throw new System.InvalidOperationException($"expected one terrain raycast in FlightCamera.UpdateCameraTransform, found {replaced}");
            }
        }

        // Same signature as the replaced Physics.Raycast call.
        private static bool TerrainRaycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction query)
        {
            if (pivotUnderground)
            {
                Vector3 pivot = origin + direction * (maxDistance * 0.5f);
                return Physics.Raycast(pivot - direction * 0.5f, direction, out hit, maxDistance, layerMask, query);
            }

            return Physics.Raycast(origin, direction, out hit, maxDistance, layerMask, query);
        }
    }
}
