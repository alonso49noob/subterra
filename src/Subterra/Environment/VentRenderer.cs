using System;
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

            // Lucas: the chimney did not fit the floor at all, and its particles looked odd. Now a black smoker as on Earth's
            // ridges: a wide mound of the floor's own rock, a main chimney and two shorter spires of dark sulphide, each closed
            // by a crater-like mouth, and dark mineral smoke rising from them. [Claude, revisar]
            BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime);
            Material floorRock = FloorRock(body, runtime, floorRadius) ?? rock;
            AddPiece(root, "mound", Mound(r * 3.2f, h * 0.22f, vent.Seed), floorRock, Vector3.zero, true);

            var rng = new System.Random((int)(vent.Seed & 0x7fffffff));
            var mouths = new List<(Vector3 at, float radius)>();
            for (int k = 0; k < 3; k++)
            {
                float share = k == 0 ? 1f : 0.45f + 0.25f * (float)rng.NextDouble();
                float cr = r * (k == 0 ? 1f : 0.55f), ch = h * share;
                float angle = (float)(rng.NextDouble() * Math.PI * 2);
                Vector3 at = k == 0 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * r * (1.2f + 0.6f * (float)rng.NextDouble());
                at.y = h * 0.12f;
                AddPiece(root, "chimney" + k, Chimney(cr, cr * 0.32f, ch, vent.Seed + (ulong)k * 7), rock, at, true);
                mouths.Add((at + new Vector3(0, ch, 0), cr * 0.32f));
            }

            // Mineral smoke from each mouth: dark, billowing, widening and fading as it rises (local space: it stays with the
            // vent as the body turns).
            foreach (var mouth in mouths)
            {
                Smoke(root, mouth.at, mouth.radius, mouth.radius == mouths[0].radius ? 1f : 0.4f);
            }

            // The glow of the hot water at the main mouth: dim, lighting the chimney and the smoke round it.
            var glow = new GameObject("glow").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = mouths[0].at + new Vector3(0, 1, 0);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.5f, 0.22f);
            glow.range = Mathf.Clamp(h * 1.6f, 18, 50);
            glow.intensity = 0.9f;
            glow.shadows = LightShadows.None;
            return root;
        }

        private static void AddPiece(GameObject root, string name, Mesh mesh, Material material, Vector3 at, bool collide)
        {
            var go = new GameObject(name) { layer = ChunkObject.Layer };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collide)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }

        // The rock of the layer at the floor (the sea floor's own material).
        private static Material FloorRock(CelestialBody body, BodyRuntime runtime, double floorRadius)
        {
            if (runtime == null)
            {
                return null;
            }

            double depth = Math.Max(0, body.Radius - floorRadius);
            Core.Model.LayerSpec layer = runtime.Strata.LayerAt(depth, "");
            return MaterialFactory.ForLayer(layer, body, runtime.Geotherm.Temperature(depth, ""), depth);
        }

        // A low, lumpy dome of rubble round the chimneys (closed at its top).
        private static Mesh Mound(float radius, float height, ulong seed)
        {
            const int segments = 20, rings = 6;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var rng = new System.Random((int)(seed & 0x7fffffff) ^ 0x3c3c);
            var bumps = new float[segments];
            for (int s = 0; s < segments; s++)
            {
                bumps[s] = 0.8f + 0.4f * (float)rng.NextDouble();
            }

            for (int ring = 0; ring <= rings; ring++)
            {
                float t = (float)ring / rings;
                float rr = radius * (1 - t * 0.92f);
                float y = height * Mathf.Sin(t * Mathf.PI * 0.5f) - 0.5f;
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2 / segments;
                    float bump = bumps[s % segments];
                    verts.Add(new Vector3(Mathf.Cos(a) * rr * bump, y * (0.85f + 0.3f * bump - 0.15f), Mathf.Sin(a) * rr * bump));
                    uvs.Add(new Vector2((float)s / segments * radius * 0.1f, t * radius * 0.05f));
                }
            }

            for (int ring = 0; ring < rings; ring++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = ring * (segments + 1) + s, b = a + segments + 1;
                    tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }

            var mesh = new Mesh { name = "SubterraVentMound" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A rough chimney narrowing to a closed mouth: its rim, and a shallow crater inside it down to a small vent hole.
        private static Mesh Chimney(float bottom, float top, float height, ulong seed)
        {
            Mesh body = Cone(bottom, top, height, 12, 7, seed, rough: 0.22f, alpha: false);
            var verts = new List<Vector3>(body.vertices);
            var uvs = new List<Vector2>(body.uv);
            var tris = new List<int>(body.triangles);
            const int segments = 12;
            // The rim folds inward and down into the crater, ending in a small dark hole.
            int start = verts.Count;
            for (int ring = 0; ring <= 2; ring++)
            {
                float rr = top * (ring == 0 ? 1.08f : ring == 1 ? 0.7f : 0.25f);
                float y = height + (ring == 0 ? 0.15f * top : -0.35f * top * ring);
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2 / segments;
                    verts.Add(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr));
                    uvs.Add(new Vector2((float)s / segments, ring * 0.2f));
                }
            }

            for (int ring = 0; ring < 2; ring++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = start + ring * (segments + 1) + s, b = a + segments + 1;
                    tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            }

            var mesh = new Mesh { name = "SubterraVentChimney" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            UnityEngine.Object.Destroy(body);
            return mesh;
        }

        private static Material smokeMaterial;
        private static Texture2D puff;

        private static void Smoke(GameObject root, Vector3 at, float mouth, float strength)
        {
            if (smokeMaterial == null)
            {
                Shader shader = Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
                smokeMaterial = new Material(shader) { mainTexture = Puff(), renderQueue = 3100 };
                if (smokeMaterial.HasProperty("_TintColor"))
                {
                    smokeMaterial.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                }
            }

            var go = new GameObject("smoke") { layer = 0 };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * strength + 0.8f, 3f * strength + 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(mouth * 1.6f, mouth * 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.09f, 0.08f, 0.07f, 0.55f), new Color(0.16f, 0.13f, 0.1f, 0.4f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = (int)(90 * strength) + 20;
            main.gravityModifier = 0;

            var emission = ps.emission;
            emission.rateOverTime = 9f * strength + 2f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6;
            shape.radius = mouth * 0.6f;

            // Billowing out as it rises and slows, and drifting a little.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.4f, 2.2f), new Keyframe(1, 4.5f)));
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 4f), new Keyframe(1, 0.6f)));
            limit.dampen = 0.15f;
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.8f, 0.78f, 0.75f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0, 1) });
            colour.color = new ParticleSystem.MinMaxGradient(fade);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Simulate(8f, true, true);
            ps.Play(true);
        }

        // A soft round puff with a lumpy edge.
        private static Texture2D Puff()
        {
            if (puff != null)
            {
                return puff;
            }

            const int size = 64;
            puff = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "SubterraVentPuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float a = Mathf.Atan2(dy, dx);
                    float edge = 0.78f + 0.12f * Mathf.Sin(a * 5 + 1.3f) + 0.06f * Mathf.Sin(a * 11);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    float alpha = Mathf.Clamp01(1 - d);
                    alpha = alpha * alpha * (3 - 2 * alpha);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
                }
            }

            puff.SetPixels32(px);
            puff.Apply(true);
            return puff;
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
