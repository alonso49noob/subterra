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

        // Lucas: the beam was simple; more immersive. Three nested cones (a bright core inside a soft, wide glow: no hard edge
        // to the light) and specks drifting in it, the particles in the water that scatter the light (marine snow).
        private static readonly float[] Layers = { 0.35f, 0.65f, 1f };
        private static readonly float[] LayerShare = { 0.55f, 0.3f, 0.18f };

        private GameObject Make(Light light)
        {
            if (material == null)
            {
                Shader shader = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
                material = new Material(shader) { renderQueue = 3100 };
                cone = Cone();
                speck = Speck();
                speckMaterial = new Material(shader) { mainTexture = speck, renderQueue = 3101 };
            }

            var beam = new GameObject("SubterraBeam") { layer = 0 };
            beam.transform.SetParent(light.transform, false);
            for (int k = 0; k < Layers.Length; k++)
            {
                var layer = new GameObject("glow" + k) { layer = 0 };
                layer.transform.SetParent(beam.transform, false);
                layer.AddComponent<MeshFilter>().sharedMesh = cone;
                var renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var motes = new GameObject("motes") { layer = 0 };
            motes.transform.SetParent(beam.transform, false);
            var ps = motes.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 220;
            main.gravityModifier = 0;
            var emission = ps.emission;
            emission.rateOverTime = 40;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.ConeVolume;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.1f;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            colour.color = new ParticleSystem.MinMaxGradient(fade);
            var pr = motes.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = speckMaterial;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play(true);
            return beam;
        }

        // The unit cone (apex at the light, along +Z, 1 long, radius 1 at its end) scaled to the light's range and angle, and
        // tinted with its colour, a little of the water's, and its brightness.
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        private static void Fit(GameObject beam, Light light, Color water)
        {
            float length = Mathf.Clamp(light.range * 0.7f, 5, 80);
            float half = Mathf.Clamp(light.spotAngle, 5, 150) * 0.5f;
            float radius = length * Mathf.Tan(Mathf.Deg2Rad * half);
            Color c = Color.Lerp(light.color, water, 0.35f) * (Strength * Mathf.Clamp(light.intensity, 0, 8));
            for (int k = 0; k < Layers.Length; k++)
            {
                Transform layer = beam.transform.GetChild(k);
                layer.localScale = new Vector3(radius * Layers[k], radius * Layers[k], length);
                var renderer = layer.GetComponent<MeshRenderer>();
                Color lc = c * (LayerShare[k] * 1.6f);
                renderer.GetPropertyBlock(Block);
                Block.SetColor("_TintColor", new Color(lc.r, lc.g, lc.b, 0.5f));
                Block.SetColor("_Color", new Color(lc.r, lc.g, lc.b, 0.5f));
                renderer.SetPropertyBlock(Block);
            }

            // The specks fill the cone out to most of its length, lit in the light's colour.
            Transform motes = beam.transform.GetChild(Layers.Length);
            var ps = motes.GetComponent<ParticleSystem>();
            var shape = ps.shape;
            shape.angle = half * 0.85f;
            shape.radius = 0.1f;
            shape.length = length * 0.75f;
            var main = ps.main;
            Color speckColour = Color.Lerp(light.color, water, 0.2f) * Mathf.Clamp(light.intensity, 0.3f, 3f) * 0.5f;
            main.startColor = new Color(speckColour.r, speckColour.g, speckColour.b, 0.6f);
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Clamp(length * radius * 0.4f, 10, 60);
        }

        private Texture2D speck;
        private Material speckMaterial;

        // A tiny soft dot.
        private static Texture2D Speck()
        {
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SubterraSpeck", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    byte v = (byte)(255 * a * a);
                    px[y * size + x] = new Color32(v, v, v, v);
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return tex;
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
            if (speckMaterial != null)
            {
                Destroy(speckMaterial);
                Destroy(speck);
            }
        }
    }
}
