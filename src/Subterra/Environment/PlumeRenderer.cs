using System;
using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Cryovolcanic plumes (Lucas: lakes under the ice of airless moons give themselves away at a glance, by cryovolcanism).
    /// Over each lake with a vent (a lake node with a skylight radius) on a body without air, a column of ice and vapour rises
    /// from the vent and fans out, as Enceladus's do: a translucent mesh whose streaks drift upwards, lit by the sun and faint
    /// at night. Taller the weaker the gravity (a ballistic plume rises as v²/2g). Only for the body the active vessel orbits.
    /// [Claude, revisar]
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class PlumeRenderer : MonoBehaviour
    {
        /// <summary>Plume height per metre of vent radius, at 1 g (taller under weaker gravity), and its limits (m).</summary>
        private const double HeightPerVentMetre = 40, MinHeight = 300, MaxHeight = 6000;

        /// <summary>Top radius as a fraction of the height: the plume fans out as it rises.</summary>
        private const float Spread = 0.3f;

        private const int Segments = 28, Rings = 14;

        /// <summary>Speed (texture lengths per second) the streaks drift up.</summary>
        private const float Drift = 0.05f;

        private static readonly Color IceColour = new Color(0.86f, 0.93f, 1f, 1f);

        private readonly List<Plume> plumes = new List<Plume>();
        private CelestialBody built;
        private float nextCheck;
        private Material material, frostMaterial;
        private Texture2D streaks;

        private sealed class Plume
        {
            public GameObject Root;
            public Vec3d Up;
            public double Radius;
        }

        private void Update()
        {
            if (!FlightGlobals.ready || !Settings.Current.CryovolcanoPlumes)
            {
                Clear();
                return;
            }

            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 2f;
                CelestialBody body = FlightGlobals.currentMainBody;
                if (body != built)
                {
                    Clear();
                    Build(body);
                }
            }

            if (material == null || plumes.Count == 0)
            {
                return;
            }

            material.mainTextureOffset = new Vector2(0, -Time.time * Drift);
            // Ice lit by the sun: bright by day, a faint glow by night (light from the planet, or the stars).
            Vector3d sun = FlightGlobals.Bodies[0].position;
            foreach (Plume p in plumes)
            {
                if (p.Root == null)
                {
                    continue;
                }

                Vector3d world = p.Root.transform.position;
                float day = Mathf.Clamp01((float)Vector3d.Dot((sun - world).normalized, (world - built.position).normalized) * 3f + 0.3f);
                // Brighter than at first (Lucas saw no sign of cryovolcanism): sunlit ice grains, and a glow by night.
                foreach (MeshRenderer renderer in p.Root.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.GetPropertyBlock(Block);
                    Color c = IceColour * Mathf.Lerp(0.3f, 1f, day);
                    Block.SetColor("_TintColor", c);
                    Block.SetColor("_Color", c);
                    renderer.SetPropertyBlock(Block);
                }
            }
        }

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        private void Build(CelestialBody body)
        {
            built = body;
            if (body == null || body.atmosphere || body.pqsController == null || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                return;
            }

            double g = body.gravParameter / (body.Radius * body.Radius);
            foreach (SiteSpec site in runtime.Spec.Sites)
            {
                foreach (NodeSpec node in site.Nodes)
                {
                    if (node.Feature != NodeFeature.Lake || node.SkylightRadius <= 0)
                    {
                        continue;
                    }

                    double height = Math.Max(MinHeight, Math.Min(MaxHeight, HeightPerVentMetre * node.SkylightRadius * Math.Sqrt(9.81 / Math.Max(0.01, g))));
                    Vec3d up = BodyFrame.Direction(node.Lat, node.Lon);
                    double radius = KspSampler.SurfaceRadius(body, node.Lat, node.Lon);
                    plumes.Add(Make(body, site.Name, up, radius, (float)node.SkylightRadius, (float)height));
                }
            }

            if (plumes.Count > 0)
            {
                Log.Info($"{plumes.Count} cryovolcanic plume(s) over {body.bodyName}'s lakes");
            }
        }

        private Plume Make(CelestialBody body, string name, Vec3d up, double radius, float vent, float height)
        {
            if (material == null)
            {
                streaks = Streaks();
                Shader shader = Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
                material = new Material(shader) { mainTexture = streaks, renderQueue = 3100 };
                // The frost lies still (the plume's streaks drift up).
                frostMaterial = new Material(material);
            }

            var root = new GameObject("SubterraPlume " + name) { layer = 0 };
            // A child of the PQS (the body frame): the plume turns with the ground.
            root.transform.SetParent(body.pqsController.transform, false);
            Vector3 axis = ((Vector3)up.ToKsp()).normalized;
            root.transform.localPosition = (Vector3)(up * radius).ToKsp();
            root.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            root.AddComponent<MeshFilter>().sharedMesh = Cone(vent, height);
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // A dense core jetting straight up inside the fan.
            var core = new GameObject("core") { layer = 0 };
            core.transform.SetParent(root.transform, false);
            core.AddComponent<MeshFilter>().sharedMesh = Cone(vent * 0.6f, height * 0.65f, 0.07f);
            var coreRenderer = core.AddComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = material;
            coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
            // The frost the plume snows back down: a bright fan of fresh ice on the ground round the vent.
            var frost = new GameObject("frost") { layer = 0 };
            frost.transform.SetParent(root.transform, false);
            frost.AddComponent<MeshFilter>().sharedMesh = Frost(body, up, radius, root.transform, vent * 5, Mathf.Min(height * 0.4f, 1500f));
            var frostRenderer = frost.AddComponent<MeshRenderer>();
            frostRenderer.sharedMaterial = frostMaterial;
            frostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            frostRenderer.receiveShadows = false;
            return new Plume { Root = root, Up = up, Radius = radius };
        }

        // A disc laid on the ground from the crater's edge (inner) out to outer, 1.5 m over the terrain, opaque at the rim and
        // fading out, in the plume's frame (root).
        private static Mesh Frost(CelestialBody body, Vec3d up, double radius, Transform root, float inner, float outer)
        {
            const int segments = 48, rings = 14;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            Vec3d e1 = Vec3d.Cross(new Vec3d(0, 1, 0), up);
            e1 = e1.Length < 1e-6 ? new Vec3d(1, 0, 0) : e1.Normalized;
            Vec3d e2 = Vec3d.Cross(up, e1);
            Quaternion toRoot = Quaternion.Inverse(root.localRotation);
            Vector3 origin = root.localPosition;
            for (int ring = 0; ring <= rings; ring++)
            {
                float t = (float)ring / rings;
                double r = inner + (outer - inner) * t * t;
                for (int s = 0; s <= segments; s++)
                {
                    double a = 2 * Math.PI * s / segments;
                    Vec3d dir = (up * radius + (e1 * Math.Cos(a) + e2 * Math.Sin(a)) * r).Normalized;
                    BodyFrame.ToLatLon(dir, out double lat, out double lon);
                    Vec3d p = dir * (KspSampler.SurfaceRadius(body, lat, lon) + 1.5);
                    vertices.Add(toRoot * ((Vector3)p.ToKsp() - origin));
                    uvs.Add(new Vector2((float)s / segments * 6, t * 3));
                    colours.Add(new Color(1, 1, 1, Mathf.Pow(1 - t, 1.3f) * 0.9f));
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

            var mesh = new Mesh { name = "SubterraFrost" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // An open cone from the vent, fanning out to Spread × height at the top; alpha highest just over the vent, fading up and
        // out; the texture repeats every few vent widths up the column.
        private static Mesh Cone(float vent, float height, float spread = Spread)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            float top = Mathf.Max(vent * 2, height * spread);
            float repeats = Mathf.Max(1, height / (vent * 12));
            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = (float)ring / Rings;
                // Most of the rings low down, where the plume changes fastest.
                float h = height * t * t;
                float r = Mathf.Lerp(vent, top, Mathf.Sqrt(t));
                float alpha = Mathf.Pow(1 - t, 1.6f) * Mathf.Clamp01(t * 12f + 0.25f);
                for (int s = 0; s <= Segments; s++)
                {
                    float a = 2 * Mathf.PI * s / Segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
                    uvs.Add(new Vector2((float)s / Segments * 3, t * t * repeats));
                    colours.Add(new Color(1, 1, 1, alpha));
                }
            }

            int row = Segments + 1;
            for (int ring = 0; ring < Rings; ring++)
            {
                for (int s = 0; s < Segments; s++)
                {
                    int i = ring * row + s;
                    // One face: the particle shaders draw both sides.
                    triangles.AddRange(new[] { i, i + row, i + 1, i + 1, i + row, i + row + 1 });
                }
            }

            var mesh = new Mesh { name = "SubterraPlume" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Soft vertical streaks: jets of ice grains, brighter and dimmer, wrapping round the cone.
        private static Texture2D Streaks()
        {
            const int w = 64, h = 128;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "SubterraPlumeStreaks" };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Columns vary slowly along the height (tileable: sampled on circles).
                    float u = 2 * Mathf.PI * x / w, v = 2 * Mathf.PI * y / h;
                    float jets = Mathf.PerlinNoise(10 + 3 * Mathf.Cos(u), 10 + 3 * Mathf.Sin(u) + 0.6f * Mathf.Sin(v));
                    float puffs = Mathf.PerlinNoise(40 + 1.5f * Mathf.Cos(v) + 0.7f * Mathf.Cos(u), 40 + 1.5f * Mathf.Sin(v));
                    float a = Mathf.Clamp01(Mathf.Pow(jets, 2.2f) * 1.6f * (0.55f + 0.6f * puffs));
                    pixels[y * w + x] = new Color(1, 1, 1, a);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }

        private void Clear()
        {
            foreach (Plume p in plumes)
            {
                if (p.Root != null)
                {
                    foreach (MeshFilter filter in p.Root.GetComponentsInChildren<MeshFilter>())
                    {
                        Destroy(filter.sharedMesh);
                    }

                    Destroy(p.Root);
                }
            }

            plumes.Clear();
            built = null;
        }

        private void OnDestroy()
        {
            Clear();
            if (material != null)
            {
                Destroy(material);
            }

            if (frostMaterial != null)
            {
                Destroy(frostMaterial);
            }

            if (streaks != null)
            {
                Destroy(streaks);
            }
        }
    }
}
