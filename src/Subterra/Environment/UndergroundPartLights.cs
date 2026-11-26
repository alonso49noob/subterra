using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Lights of craft under the ground are drawn per pixel (Lucas: a halo of light on the ground round a shaft with a rover
    /// in it, and a lighter rectangle round the laser's hole at night). Unity sends the lights it does not count as important
    /// into an approximate, direction-blind ambient term of each object they reach (spotlights as plain point lights): from
    /// down a hole they lit the terrain over it, a whole terrain quad at a time. Per pixel, the ground over a light below it
    /// faces away and stays dark, and the cave around it is lit as it should be. Put back as it was above the ground.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class UndergroundPartLights : MonoBehaviour
    {
        /// <summary>Metres under the ground a light must be to be switched.</summary>
        private const double MinDepth = 0.5;

        // Lights switched, with the mode they had.
        private readonly Dictionary<Light, LightRenderMode> switched = new Dictionary<Light, LightRenderMode>();
        private readonly List<Light> scratch = new List<Light>();
        private float nextCheck;

        private void Update()
        {
            if (Time.time < nextCheck || !FlightGlobals.ready)
            {
                return;
            }

            nextCheck = Time.time + 0.5f;
            using (SubterraClock.Measure("UndergroundPartLights"))
            {
                var seen = new HashSet<Light>();
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v == null || v.mainBody == null || v.mainBody.pqsController == null || !BodyRegistry.Instance.TryGet(v.mainBody, out _))
                    {
                        continue;
                    }

                    foreach (Part p in v.parts)
                    {
                        scratch.Clear();
                        p.GetComponentsInChildren(true, scratch);
                        foreach (Light light in scratch)
                        {
                            if (light == null || light.type == LightType.Directional)
                            {
                                continue;
                            }

                            seen.Add(light);
                            bool under = Underground(v.mainBody, light.transform.position);
                            if (under && !switched.ContainsKey(light))
                            {
                                switched[light] = light.renderMode;
                                light.renderMode = LightRenderMode.ForcePixel;
                            }
                            else if (!under && switched.TryGetValue(light, out LightRenderMode mode))
                            {
                                light.renderMode = mode;
                                switched.Remove(light);
                            }
                        }
                    }
                }

                // Forget lights that went (part destroyed, vessel unloaded).
                if (switched.Count > seen.Count)
                {
                    var gone = new List<Light>();
                    foreach (Light light in switched.Keys)
                    {
                        if (light == null || !seen.Contains(light))
                        {
                            gone.Add(light);
                        }
                    }

                    foreach (Light light in gone)
                    {
                        if (light != null)
                        {
                            light.renderMode = switched[light];
                        }

                        switched.Remove(light);
                    }
                }
            }
        }

        private static bool Underground(CelestialBody body, Vector3d world)
        {
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world);
            return KspSampler.SurfaceRadius(body, lat, lon) - (world - body.position).magnitude > MinDepth;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<Light, LightRenderMode> pair in switched)
            {
                if (pair.Key != null)
                {
                    pair.Key.renderMode = pair.Value;
                }
            }

            switched.Clear();
        }
    }
}
