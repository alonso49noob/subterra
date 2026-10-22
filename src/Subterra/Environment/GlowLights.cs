using System.Collections.Generic;
using Subterra.Core.Model;
using Subterra.Core.Scatter;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Light from glowing scatters (CAVE_SCATTER with <c>glow</c>): a few point lights among the glowing props nearest the
    /// camera (only <see cref="MaxLights"/>, spread out, re-picked a few times a second), and the daylight that thin roofs
    /// let through: scatters marked <c>daylight</c> dim with the sun's height over the ground above the camera, to nothing
    /// at night.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class GlowLights : MonoBehaviour
    {
        public const int MaxLights = 6;
        private const float PickInterval = 0.25f;

        private readonly List<Light> lights = new List<Light>();
        private readonly List<(Light light, float intensity, bool daylight)> lit = new List<(Light, float, bool)>();
        private readonly List<Candidate> candidates = new List<Candidate>();
        private float nextPick;

        /// <summary>Light level of daylight glows this frame (0 = night, 1 = sun high).</summary>
        public static float Daylight { get; private set; } = 1;

        /// <summary>Glow lights switched on (for tests and the debug window).</summary>
        public static int ActiveLights { get; private set; }

        private struct Candidate
        {
            public Vector3 Position;
            public float Distance;
            public ScatterSpec Spec;
        }

        private void Update()
        {
            Camera camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (camera == null)
            {
                return;
            }

            using (SubterraClock.Measure("GlowLights"))
            {
                Vector3 eye = camera.transform.position;
                Daylight = DaylightAt(eye);
                for (int i = 0; i < PropLibrary.Glow.Count; i++)
                {
                    PropLibrary.Glow.Apply(i, PropLibrary.Glow[i].Daylight ? Daylight : 1);
                }

                if (Time.time >= nextPick)
                {
                    nextPick = Time.time + PickInterval;
                    Pick(eye);
                }

                foreach (var (light, intensity, daylight) in lit)
                {
                    light.intensity = intensity * (daylight ? Daylight : 1);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (Light light in lights)
            {
                if (light != null)
                {
                    Destroy(light.gameObject);
                }
            }

            ActiveLights = 0;
        }

        // Nearest glowing props first, each light at least half its range from the others.
        private void Pick(Vector3 eye)
        {
            candidates.Clear();
            ChunkStreamer streamer = ChunkStreamer.Instance;
            if (streamer != null && UndergroundTracker.Camera.Inside)
            {
                foreach (ChunkObject chunk in streamer.Chunks)
                {
                    if (chunk.IsEmpty || !chunk.Visible)
                    {
                        continue;
                    }

                    foreach (ChunkObject.ScatterPart part in chunk.Scatters)
                    {
                        ScatterSpec spec = part.Data.Spec;
                        if (spec?.Glow == null || spec.GlowLight <= 0 || part.Object == null || part.Data.Placed == null)
                        {
                            continue;
                        }

                        bool hanging = spec.Placement == ScatterPlacement.Ceiling;
                        Transform frame = part.Object.transform;
                        foreach (ScatterInstance s in part.Data.Placed)
                        {
                            // Into the cave from the prop: under a ceiling patch, over a fungus's caps.
                            var local = s.Position + s.Up * (hanging ? -1.5f : 0.8f * s.Scale);
                            Vector3 p = frame.TransformPoint(local.ToUnity());
                            float d = (p - eye).magnitude;
                            if (d < spec.GlowLight * 3)
                            {
                                candidates.Add(new Candidate { Position = p, Distance = d, Spec = spec });
                            }
                        }
                    }
                }
            }

            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            lit.Clear();
            foreach (Candidate c in candidates)
            {
                if (lit.Count == MaxLights)
                {
                    break;
                }

                float spacing = (float)c.Spec.GlowLight * 0.5f;
                bool crowded = false;
                foreach (var (light, _, _) in lit)
                {
                    crowded |= (light.transform.position - c.Position).sqrMagnitude < spacing * spacing;
                }

                if (crowded)
                {
                    continue;
                }

                Light l = LightAt(lit.Count);
                l.transform.position = c.Position;
                l.color = new Color(c.Spec.Glow[0], c.Spec.Glow[1], c.Spec.Glow[2]);
                l.range = (float)c.Spec.GlowLight;
                l.enabled = true;
                lit.Add((l, 2f, c.Spec.Daylight));
            }

            for (int i = lit.Count; i < lights.Count; i++)
            {
                lights[i].enabled = false;
            }

            ActiveLights = lit.Count;
        }

        private Light LightAt(int i)
        {
            while (lights.Count <= i)
            {
                var go = new GameObject("SubterraGlowLight " + lights.Count);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                lights.Add(light);
            }

            return lights[i];
        }

        // The sun's height over the horizon of the ground above `eye`: 0 below it, 1 from about 15° up.
        private static float DaylightAt(Vector3 eye)
        {
            CelestialBody body = FlightGlobals.currentMainBody;
            CelestialBody star = FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0 ? FlightGlobals.Bodies[0] : null;
            if (body == null || star == null || body == star)
            {
                return 1;
            }

            Vector3d up = ((Vector3d)eye - body.position).normalized;
            Vector3d toSun = (star.position - (Vector3d)eye).normalized;
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.02f, 0.26f, (float)Vector3d.Dot(up, toSun)));
        }
    }
}
