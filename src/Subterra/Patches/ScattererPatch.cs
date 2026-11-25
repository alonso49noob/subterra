using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Scatterer (optional) counts the camera as "underwater" whenever it is below the body's sea level. In a dry cave or
    /// tunnel dug under Kerbin's (or Laythe's, Eve's) sea level that turned the view sea-blue and dimmed the sun, as if the
    /// rock were water. Under the ground (in a cave's void) or in a Subterra ocean, Scatterer's underwater mode is kept off:
    /// a cave's own water, and an ocean under ice, are drawn by <see cref="UnderwaterView"/> in their own colour.
    /// Only applied when Scatterer is installed; nothing of it is referenced at build time.
    /// </summary>
    public static class ScattererPatch
    {
        public const string Id = "scatterer";

        // OceanNode.height: the camera's height over the sea, set before each frame is drawn; UpdateNode compares it
        // with the water's to set isUnderwater.
        private static FieldInfo height;

        // OceanNode.waterMeshRenderer: its ocean surface, switched on every UpdateNode; off while the camera is under the land
        // below sea level (seen from there it was a blue surface across a shaft, or a blue sky around a cave).
        private static FieldInfo waterMesh;

        public static void Register()
        {
            Type node = AccessTools.TypeByName("Scatterer.OceanNode");
            if (node == null)
            {
                return;
            }

            height = AccessTools.Field(node, "height");
            waterMesh = AccessTools.Field(node, "waterMeshRenderer");
            MethodInfo update = AccessTools.Method(node, "UpdateNode");
            if (height == null || height.FieldType != typeof(double) || update == null)
            {
                Log.Info("Scatterer found but its ocean is not as expected: its underwater view may show in dry caves under sea level");
                return;
            }

            PatchManager.Register(Id, harmony => harmony.Patch(update,
                prefix: new HarmonyMethod(typeof(ScattererPatch), nameof(UpdateNodePrefix)),
                postfix: new HarmonyMethod(typeof(ScattererPatch), nameof(UpdateNodePostfix))));
            Log.Info("Scatterer found: its underwater view is kept out of caves and Subterra oceans");
        }

        // Under the ground the camera is, for this call only, far over the sea: Scatterer leaves (or never enters) its
        // underwater mode. The real height is put back after it.
        private static void UpdateNodePrefix(object __instance, out double? __state)
        {
            __state = null;
            try
            {
                if (Underground_())
                {
                    __state = (double)height.GetValue(__instance);
                    height.SetValue(__instance, double.MaxValue);
                }
            }
            catch (Exception e)
            {
                PatchManager.Fault(Id, e);
            }
        }

        private static void UpdateNodePostfix(object __instance, double? __state)
        {
            if (__state != null)
            {
                height.SetValue(__instance, __state.Value);
            }

            if (OceanHider.CameraUnderSeaLevelInRock && waterMesh?.GetValue(__instance) is MeshRenderer mesh && mesh != null)
            {
                mesh.enabled = false;
            }
        }

        // The flight camera under the ground (in a cave's void, or below the terrain's surface: the sea is always over the
        // sea floor), or in a Subterra ocean.
        private static bool Underground_()
        {
            if (!HighLogic.LoadedSceneIsFlight || FlightCamera.fetch == null || FlightCamera.fetch.mainCamera == null)
            {
                return false;
            }

            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null || body.pqsController == null)
            {
                return false;
            }

            Vector3d at = FlightCamera.fetch.mainCamera.transform.position;
            Core.Numerics.Vec3d local = Frames.ToBody(body, at);
            Core.Geo.BodyFrame.ToLatLon(local, out double lat, out double lon);
            return local.Length < KspSampler.SurfaceRadius(body, lat, lon) - 1
                || Underground.Locate(body, at, out _, out _) || Underground.OceanAt(body, at) != null;
        }
    }
}
