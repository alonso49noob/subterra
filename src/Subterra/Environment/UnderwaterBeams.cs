using System.Collections.Generic;
using Subterra.Core.Numerics;
using Subterra.Core.Tubes;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Light beams under water (Lucas: lights working in the oceans, like the Tyndall effect): every spotlight of a loaded craft
    /// that is in water (an ocean under ice, a flooded cave) shows its cone as a soft shaft of light, the light scattered by
    /// the water, fading with distance and taking a little of the water's colour. Gone out of the water. [Claude, revisar]
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class UnderwaterBeams : MonoBehaviour
    {
        /// <summary>Brightness of a beam (its additive colour) for a light of intensity 1.</summary>
        private const float Strength = 0.06f;

        private readonly Dictionary<Light, GameObject> beams = new Dictionary<Light, GameObject>();
        private readonly List<Light> scratch = new List<Light>();
        private Material material;
        private Mesh cone;
        private float nextLook;

        private void Update()
        {
            if (Time.time < nextLook || !FlightGlobals.ready)
            {
                return;
            }

            nextLook = Time.time + 0.25f;
            var seen = new HashSet<Light>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.mainBody == null || !BodyRegistry.Instance.TryGet(v.mainBody, out _))
                {
                    continue;
                }

                foreach (Part p in v.parts)
                {
                    scratch.Clear();
                    p.GetComponentsInChildren(false, scratch);
                    foreach (Light light in scratch)
                    {
                        if (light == null || light.type != LightType.Spot)
                        {
                            continue;
                        }

                        if (!light.enabled || light.intensity <= 0 || !InWater(v.mainBody, light.transform.position, out Color water))
                        {
                            continue;
                        }

                        seen.Add(light);
                        if (!beams.TryGetValue(light, out GameObject beam) || beam == null)
                        {
                            beams[light] = beam = Make(light);
                        }

                        Fit(beam, light, water);
                    }
                }
            }

            var gone = new List<Light>();
            foreach (KeyValuePair<Light, GameObject> pair in beams)
            {
                if (pair.Key == null || !seen.Contains(pair.Key))
                {
                    gone.Add(pair.Key);
                }
            }

            foreach (Light light in gone)
            {
                if (beams[light] != null)
                {
                    Destroy(beams[light]);
                }

                beams.Remove(light);
            }
        }

        // In an ocean under ice or in a cave's water, with the water's colour.
        private static bool InWater(CelestialBody body, Vector3d world, out Color colour)
        {
            colour = new Color(0.5f, 0.7f, 0.8f);
            if (Underground.OceanAt(body, world) != null)
            {
                if (BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) && runtime.Ocean?.Spec.Colour is float[] c && c.Length >= 3)
                {
                    colour = new Color(c[0], c[1], c[2]);
                }

                return true;
            }

            return Underground.Locate(body, world, out TubeNetwork net, out Vec3 local) && net.Waters.Count > 0 && net.WaterAt(local, margin: 0f, above: 0f) != null;
        }

        private GameObject Make(Light light)
        {
            if (material == null)
            {
                Shader shader = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
                material = new Material(shader) { renderQueue = 3100 };
                cone = Cone();
            }

            var beam = new GameObject("SubterraBeam") { layer = 0 };
            beam.transform.SetParent(light.transform, false);
            beam.AddComponent<MeshFilter>().sharedMesh = cone;
            var renderer = beam.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return beam;
        }

        // The unit cone (apex at the light, along +Z, 1 long, radius 1 at its end) scaled to the light's range and angle, and
        // tinted with its colour, a little of the water's, and its brightness.
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        private static void Fit(GameObject beam, Light light, Color water)
        {
            float length = Mathf.Clamp(light.range * 0.7f, 5, 80);
            float radius = length * Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(light.spotAngle, 5, 150) * 0.5f);
            beam.transform.localScale = new Vector3(radius, radius, length);
            Color c = Color.Lerp(light.color, water, 0.35f) * (Strength * Mathf.Clamp(light.intensity, 0, 8));
            var renderer = beam.GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(Block);
            Block.SetColor("_TintColor", new Color(c.r, c.g, c.b, 0.5f));
            Block.SetColor("_Color", new Color(c.r, c.g, c.b, 0.5f));
            renderer.SetPropertyBlock(Block);
        }

        private static Mesh Cone()
        {
            const int segments = 24, rings = 8;
            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            for (int ring = 0; ring <= rings; ring++)
            {
                float t = (float)ring / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float a = 2 * Mathf.PI * s / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * t, Mathf.Sin(a) * t, t));
                    // Brightest near the lamp, gone at the end of the range.
                    colours.Add(new Color(1, 1, 1, Mathf.Pow(1 - t, 1.8f) * Mathf.Clamp01(t * 10)));
                }
            }

            int row = segments + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int i = ring * row + s;
                    triangles.AddRange(new[] { i, i + 1, i + row, i + 1, i + row + 1, i + row });
                }
            }

            var mesh = new Mesh { name = "SubterraBeamCone" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            foreach (GameObject beam in beams.Values)
            {
                if (beam != null)
                {
                    Destroy(beam);
                }
            }

            beams.Clear();
            Destroy(material);
            Destroy(cone);
        }
    }
}
