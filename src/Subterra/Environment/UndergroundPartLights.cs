using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Lights of craft under the ground leave the terrain out (Lucas: a halo of light on the ground round a shaft with a rover
    /// in it, a lighter patch round the laser's hole at night): a light down a hole lit the ground over it, through the rock.
    /// The terrain is on its own layer and the caves are drawn on another (<see cref="ChunkObject.VisualLayer"/>), so a light
    /// under the ground drops the terrain's layer from its mask and still lights the cave and the craft. (Making them
    /// per-pixel instead left the caves dark: Lucas.) Put back as it was above the ground.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class UndergroundPartLights : MonoBehaviour
    {
        /// <summary>Metres under the ground a light must be to leave the terrain out.</summary>
        private const double MinDepth = 0.5;

        private const int TerrainMask = 1 << ChunkObject.Layer;

        // Lights changed, with the mask they had.
        private readonly Dictionary<Light, int> changed = new Dictionary<Light, int>();
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
                            if (light != null && light.type != LightType.Directional)
                            {
                                seen.Add(light);
                                Apply(light, Underground(v.mainBody, light.transform.position));
                                Boost(light, light.enabled && InWater(v.mainBody, light.transform.position));
                            }
                        }
                    }
                }

                // Lights that went (part destroyed, vessel unloaded) are forgotten.
                var gone = new List<Light>();
                foreach (Light light in changed.Keys)
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
                        light.cullingMask = changed[light];
                    }

                    changed.Remove(light);
                }

                var unboosted = new List<Light>();
                foreach (Light light in boosted.Keys)
                {
                    if (light == null || !seen.Contains(light))
                    {
                        unboosted.Add(light);
                    }
                }

                foreach (Light light in unboosted)
                {
                    Boost(light, false);
                    boosted.Remove(light);
                }
            }
        }

        // Lamps under water reach farther and shine brighter (Lucas: the lights did not make it easier to see under water):
        // half again their range, a third more light. Put back out of the water.
        private const float WaterRange = 1.5f, WaterIntensity = 1.35f;
        private readonly Dictionary<Light, (float range, float intensity)> boosted = new Dictionary<Light, (float, float)>();

        private void Boost(Light light, bool wet)
        {
            if (light == null)
            {
                return;
            }

            if (wet && !boosted.ContainsKey(light))
            {
                boosted[light] = (light.range, light.intensity);
                light.range *= WaterRange;
                light.intensity *= WaterIntensity;
            }
            else if (!wet && boosted.TryGetValue(light, out var was))
            {
                light.range = was.range;
                light.intensity = was.intensity;
                boosted.Remove(light);
            }
        }

        private static bool InWater(CelestialBody body, Vector3d world)
        {
            Core.Water.WaterSample? sea = Subterra.Underground.OceanAt(body, world);
            if (sea.HasValue && sea.Value.Under)
            {
                return true;
            }

            if (!Subterra.Underground.Locate(body, world, out Core.Tubes.TubeNetwork net, out Core.Numerics.Vec3 local) || net.Waters.Count == 0)
            {
                return false;
            }

            Core.Water.WaterSample? w = net.WaterAt(local, margin: 0f, above: 0f);
            return w.HasValue && w.Value.Under;
        }

        private void Apply(Light light, bool under)
        {
            if (under && !changed.ContainsKey(light))
            {
                changed[light] = light.cullingMask;
                light.cullingMask &= ~TerrainMask;
            }
            else if (!under && changed.TryGetValue(light, out int mask))
            {
                light.cullingMask = mask;
                changed.Remove(light);
            }
        }

        /// <summary>True if a point is more than half a metre under the terrain straight above it.</summary>
        public static bool Underground(CelestialBody body, Vector3d world)
        {
            double lat = body.GetLatitude(world), lon = body.GetLongitude(world);
            return KspSampler.SurfaceRadius(body, lat, lon) - (world - body.position).magnitude > MinDepth;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<Light, (float range, float intensity)> pair in boosted)
            {
                if (pair.Key != null)
                {
                    pair.Key.range = pair.Value.range;
                    pair.Key.intensity = pair.Value.intensity;
                }
            }

            boosted.Clear();
            foreach (KeyValuePair<Light, int> pair in changed)
            {
                if (pair.Key != null)
                {
                    pair.Key.cullingMask = pair.Value;
                }
            }

            changed.Clear();
        }
    }
}
