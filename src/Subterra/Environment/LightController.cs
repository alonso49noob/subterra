using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Darkness under ground: ambient light scaled by the camera's sky visibility, and the sun switched
    /// off when the camera is farther from every opening than the shadow distance (otherwise light would
    /// leak through long tubes). KSP's DynamicAmbientLight rewrites the ambient light in every Update, so
    /// it is scaled again in each LateUpdate (after it, before rendering) from KSP's fresh value; if KSP did
    /// not write it this frame, the last outside value is reused so the scaling never compounds. Originals
    /// are restored when leaving the cave, on scene change and on destroy.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class LightController : MonoBehaviour
    {
        private static Color? outside;
        private static Color? applied;
        private static float? savedSun;
        private static Light sun;

        /// <summary>Ambient light KSP wanted this frame, before the cave darkening (null when idle).</summary>
        public static Color? OutsideAmbient => outside;

        /// <summary>Ambient light the controller rendered with this frame (null when idle).</summary>
        public static Color? AppliedAmbient => applied;

        public static float? SavedSunIntensity => savedSun;

        public static bool Active => applied.HasValue;

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

                if (!applied.HasValue)
                {
                    sun = Sun();
                }

                // A value other than the one written last frame is KSP's new outside light.
                Color current = RenderSettings.ambientLight;
                if (!outside.HasValue || !applied.HasValue || current != applied.Value)
                {
                    outside = current;
                }

                applied = outside.Value * Mathf.Clamp01(camera.Sky);
                RenderSettings.ambientLight = applied.Value;

                // The sun is only touched while it is switched off; near an opening KSP keeps control of it.
                bool off = sun != null && FarFromOpenings(camera);
                if (off && !savedSun.HasValue)
                {
                    savedSun = sun.intensity;
                }

                if (off)
                {
                    sun.intensity = 0f;
                }
                else if (savedSun.HasValue)
                {
                    sun.intensity = savedSun.Value;
                    savedSun = null;
                }
            }
        }

        public static void Restore()
        {
            // Only undo our own darkening: if KSP already wrote a new value, keep it.
            if (outside.HasValue && applied.HasValue && RenderSettings.ambientLight == applied.Value)
            {
                RenderSettings.ambientLight = outside.Value;
            }

            if (sun != null && savedSun.HasValue)
            {
                sun.intensity = savedSun.Value;
            }

            outside = null;
            applied = null;
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
