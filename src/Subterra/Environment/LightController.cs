using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Darkness under ground: ambient light scaled by the camera's sky visibility, and the sun switched
    /// off when the camera is farther from every opening than the shadow distance (otherwise light would
    /// leak through long tubes). Originals are restored when leaving the cave, on scene change and on destroy.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class LightController : MonoBehaviour
    {
        private static Color? savedAmbient;
        private static float? savedSun;
        private static Light sun;

        public static Color? SavedAmbient => savedAmbient;

        public static float? SavedSunIntensity => savedSun;

        public static bool Active => savedAmbient.HasValue;

        private void Start()
        {
            GameEvents.onGameSceneLoadRequested.Add(OnSceneChange);
        }

        private void OnDestroy()
        {
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
            Restore();
        }

        private void OnSceneChange(GameScenes scene) => Restore();

        private void LateUpdate()
        {
            using (SubterraClock.Measure("LightController"))
            {
                UndergroundState camera = UndergroundTracker.Camera;
                if (!camera.Inside)
                {
                    Restore();
                    return;
                }

                if (!savedAmbient.HasValue)
                {
                    savedAmbient = RenderSettings.ambientLight;
                    sun = Sun();
                    savedSun = sun != null ? sun.intensity : (float?)null;
                }

                RenderSettings.ambientLight = savedAmbient.Value * Mathf.Clamp01(camera.Sky);
                if (sun != null && savedSun.HasValue)
                {
                    sun.intensity = FarFromOpenings(camera) ? 0f : savedSun.Value;
                }
            }
        }

        public static void Restore()
        {
            if (savedAmbient.HasValue)
            {
                RenderSettings.ambientLight = savedAmbient.Value;
            }

            if (sun != null && savedSun.HasValue)
            {
                sun.intensity = savedSun.Value;
            }

            savedAmbient = null;
            savedSun = null;
            sun = null;
        }

        private static bool FarFromOpenings(UndergroundState camera)
        {
            float shadow = QualitySettings.shadowDistance;
            foreach (var skylight in camera.Net.Skylights)
            {
                if ((skylight.SurfaceCenter - camera.Local).Length < shadow + skylight.Radius)
                {
                    return false;
                }
            }

            return true;
        }

        // KSP's sun light.
        private static Light Sun()
        {
            if (global::Sun.Instance != null && global::Sun.Instance.sunLight != null)
            {
                return global::Sun.Instance.sunLight;
            }

            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional)
                {
                    return light;
                }
            }

            return null;
        }
    }
}
