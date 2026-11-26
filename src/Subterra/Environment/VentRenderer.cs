using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Water;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Hydrothermal vents on the floors of the oceans under ice (Lucas: an ocean more lively and fun to explore). Near the
    /// craft: a rough chimney (with a collider), warm water rising from it as a shimmering column, and a warm glow at its mouth
    /// lighting the dark water round it. Where an ocean's ventChance puts them. [Claude, revisar]
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class VentRenderer : MonoBehaviour
    {
        /// <summary>How far (m) round the craft vents are shown, and how high over the floor the craft may be.</summary>
        private const double Reach = 1200, ShowHeight = 1800;

        private readonly Dictionary<ulong, GameObject> shown = new Dictionary<ulong, GameObject>();
        private CelestialBody shownBody;
        private float nextLook;
        private Material rock, plume;
        private Texture2D streaks;

        private void Update()
        {
            if (plume != null)
            {
                plume.mainTextureOffset = new Vector2(0, -Time.time * 0.12f);
            }

            if (Time.time < nextLook)
            {
                return;
            }

            nextLook = Time.time + 1f;
            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = vessel != null ? vessel.mainBody : null;
            if (body == null || body.pqsController == null || !Settings.Current.WaterPhysics || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime)
                || runtime.Ocean == null || runtime.Ocean.Spec.VentChance <= 0)
            {
                Clear();
                return;
            }

            OceanShell ocean = runtime.Ocean;
            Vec3d at = Frames.ToBody(body, vessel.CoMD);
            BodyFrame.ToLatLon(at.Normalized, out double lat, out _);
            double over = at.Length - ocean.FloorRadius(lat);
            if (over > ShowHeight || over < -300 || at.Length > ocean.CeilingRadius(lat) + 50)
            {
                Clear();
                return;
            }

            if (shownBody != body)
            {
                Clear();
                shownBody = body;
            }

            ulong floorSeed = Hash.Of(body.bodyName + "/seafloor");
            var keep = new HashSet<ulong>();
            foreach (OceanVent vent in OceanFeatures.VentsNear(ocean, at.Normalized, Reach, Hash.Of(body.bodyName + "/vents")))
            {
                keep.Add(vent.Seed);
                if (!shown.ContainsKey(vent.Seed))
                {
                    BodyFrame.ToLatLon(vent.Direction, out double vlat, out _);
                    double floor = ocean.FloorRadius(vlat) + Core.Meshing.SeaFloorMesher.Rise(vent.Direction * ocean.FloorRadius(vlat), floorSeed);
                    shown[vent.Seed] = Make(body, vent, floor);
                }
            }

            var gone = new List<ulong>();
            foreach (KeyValuePair<ulong, GameObject> pair in shown)
            {
                if (!keep.Contains(pair.Key))
                {
                    gone.Add(pair.Key);
                }
            }

            foreach (ulong key in gone)
            {
                Drop(shown[key]);
                shown.Remove(key);
            }
        }

        private GameObject Make(CelestialBody body, OceanVent vent, double floorRadius)
        {
            if (rock == null)
            {
                rock = MaterialFactory.Create("Rock_d", "Rock_n", new Color(0.22f, 0.21f, 0.2f));
                streaks = Streaks();
                Shader shader = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
                plume = new Material(shader) { mainTexture = streaks, color = new Color(0.55f, 0.6f, 0.55f, 0.5f), renderQueue = 3100 };
                if (plume.HasProperty("_TintColor"))
                {
                    plume.SetColor("_TintColor", new Color(0.32f, 0.36f, 0.33f, 0.5f));
                }
            }

            float r = (float)vent.Radius, h = (float)vent.Height;
            var root = new GameObject("SubterraVent " + vent.Seed) { layer = ChunkObject.Layer };
            // A child of the PQS (the body frame): it turns with the body.
            root.transform.SetParent(body.pqsController.transform, false);
            root.transform.localPosition = (Vector3)(vent.Direction * (floorRadius - 1)).ToKsp();
            root.transform.localRotation = Quaternion.FromToRotation(Vector3.up, ((Vector3)vent.Direction.ToKsp()).normalized)
                * Quaternion.Euler(0, (vent.Seed % 360), 0);

            // The chimney: a rough cone, narrowing to its mouth.
            var chimney = new GameObject("chimney") { layer = ChunkObject.Layer };
            chimney.transform.SetParent(root.transform, false);
            Mesh cone = Cone(r, r * 0.3f, h, 14, 6, vent.Seed, rough: 0.18f, alpha: false);
            chimney.AddComponent<MeshFilter>().sharedMesh = cone;
            chimney.AddComponent<MeshRenderer>().sharedMaterial = rock;
            chimney.AddComponent<MeshCollider>().sharedMesh = cone;

            // Warm water rising: a column widening as it goes up and fading out.
            var column = new GameObject("plume") { layer = 0 };
            column.transform.SetParent(root.transform, false);
            column.transform.localPosition = new Vector3(0, h, 0);
            column.AddComponent<MeshFilter>().sharedMesh = Cone(r * 0.3f, r * 2.5f, h * 4, 18, 10, vent.Seed + 1, rough: 0, alpha: true);
            var renderer = column.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = plume;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The glow of the hot water at its mouth.
            var glow = new GameObject("glow").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0, h + 1, 0);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.55f, 0.25f);
            glow.range = Mathf.Clamp(h * 2.5f, 25, 80);
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;
            glow.renderMode = LightRenderMode.ForcePixel;
            return root;
        }

        // A cone (open at both ends) from radius `bottom` to `top` over `height`, bumped by `rough` (share of the radius); with
        // `alpha`, vertex alpha fading from its base to its top (for the plume).
        private static Mesh Cone(float bottom, float top, float height, int segments, int rings, ulong seed, float rough, bool alpha)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            var rng = new System.Random((int)(seed % int.MaxValue));
            var bumps = new float[segments + 1];
            for (int s = 0; s < segments; s++)
            {
                bumps[s] = 1 + rough * (float)(rng.NextDouble() * 2 - 1);
            }

            bumps[segments] = bumps[0];
            for (int ring = 0; ring <= rings; ring++)
            {
                float t = (float)ring / rings;
                float radius = Mathf.Lerp(bottom, top, alpha ? Mathf.Sqrt(t) : t);
                for (int s = 0; s <= segments; s++)
                {
                    float a = 2 * Mathf.PI * s / segments;
                    float k = alpha ? 1 : bumps[s] * (1 + rough * 0.5f * Mathf.Sin(t * 9 + s));
                    vertices.Add(new Vector3(Mathf.Cos(a) * radius * k, height * t, Mathf.Sin(a) * radius * k));
                    uvs.Add(new Vector2((float)s / segments * 2, t * height / Mathf.Max(1, bottom * 4)));
                    colours.Add(new Color(1, 1, 1, alpha ? Mathf.Pow(1 - t, 1.5f) * Mathf.Clamp01(t * 8 + 0.3f) : 1));
                }
            }

            int row = segments + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int i = ring * row + s;
                    triangles.AddRange(new[] { i, i + row, i + 1, i + 1, i + row, i + row + 1 });
                }
            }

            var mesh = new Mesh { name = "SubterraVentCone" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Soft streaks of shimmering water, tiling round and up.
        private static Texture2D Streaks()
        {
            const int w = 32, h = 64;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "SubterraVentStreaks" };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = 2 * Mathf.PI * x / w, v = 2 * Mathf.PI * y / h;
                    float n = Mathf.PerlinNoise(20 + 2 * Mathf.Cos(u) + 0.8f * Mathf.Sin(v), 20 + 2 * Mathf.Sin(u) + 0.8f * Mathf.Cos(v));
                    pixels[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(n * n * 1.8f));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }

        private static void Drop(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Destroy(filter.sharedMesh);
            }

            Destroy(root);
        }

        private void Clear()
        {
            foreach (GameObject root in shown.Values)
            {
                Drop(root);
            }

            shown.Clear();
            shownBody = null;
        }

        private void OnDestroy()
        {
            Clear();
            Destroy(rock);
            Destroy(plume);
            Destroy(streaks);
        }
    }
}
