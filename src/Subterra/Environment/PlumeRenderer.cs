using System;
using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Cryovolcanic plumes, as Enceladus's (Lucas: the plume was ugly and nothing like one; "hazlo como encelado"). Along
    /// each fissure of a tiger-stripe relief (<see cref="ReliefKind.Fissures"/>) on a body without air: a row of narrow
    /// jets of ice grains rising from the crack, widening as they slow, a faint broad fan over the whole field where the jets
    /// merge, and bright fresh frost laid along the cracks. Taller the weaker the gravity (a ballistic jet rises as v²/2g);
    /// lit by the sun, faint by night. Only for the body the active vessel is at. [Claude, revisar]
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class PlumeRenderer : MonoBehaviour
    {
        /// <summary>Jet height per metre of fissure width, at 1 g (taller under weaker gravity), and its limits (m).</summary>
        private const double HeightPerWidthMetre = 12, MinHeight = 300, MaxHeight = 6000;

        /// <summary>Jets along each fissure.</summary>
        private const int JetsPerFissure = 6;

        private static readonly Color IceColour = new Color(0.86f, 0.93f, 1f, 1f);

        private readonly List<GameObject> roots = new List<GameObject>();
        private CelestialBody built;
        private float nextCheck;
        private Material jetMaterial, frostMaterial;
        private Texture2D puff, white;

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

                Light();
            }
        }

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        // Ice lit by the sun: bright by day, a faint glow by night (light from the planet, or the stars).
        private void Light()
        {
            if (built == null || roots.Count == 0)
            {
                return;
            }

            Vector3d sun = FlightGlobals.Bodies[0].position;
            foreach (GameObject root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                Vector3d world = root.transform.position;
                float day = Mathf.Clamp01((float)Vector3d.Dot((sun - world).normalized, (world - built.position).normalized) * 3f + 0.3f);
                Color c = IceColour * Mathf.Lerp(0.3f, 1f, day);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                {
                    renderer.GetPropertyBlock(Block);
                    Block.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0.5f));
                    Block.SetColor("_Color", c);
                    renderer.SetPropertyBlock(Block);
                }
            }
        }

        private void Build(CelestialBody body)
        {
            built = body;
            if (body == null || body.atmosphere || body.pqsController == null || !BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime))
            {
                return;
            }

            double g = body.gravParameter / (body.Radius * body.Radius);
            foreach (ReliefSpec relief in runtime.Spec.Reliefs)
            {
                if (relief.Kind != ReliefKind.Fissures)
                {
                    continue;
                }

                double height = Math.Max(MinHeight, Math.Min(MaxHeight, HeightPerWidthMetre * relief.Width * Math.Sqrt(9.81 / Math.Max(0.01, g))));
                roots.Add(Make(body, relief, (float)height));
            }

            if (roots.Count > 0)
            {
                Log.Info($"{roots.Count} field(s) of cryovolcanic jets on {body.bodyName}");
            }
        }

        private GameObject Make(CelestialBody body, ReliefSpec relief, float height)
        {
            if (jetMaterial == null)
            {
                puff = Puff();
                white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "SubterraWhite" };
                var px = new Color32[16];
                for (int i = 0; i < 16; i++)
                {
                    px[i] = new Color32(255, 255, 255, 255);
                }

                white.SetPixels32(px);
                white.Apply();
                Shader shader = Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
                jetMaterial = new Material(shader) { mainTexture = puff, renderQueue = 3100 };
                frostMaterial = new Material(shader) { mainTexture = white, renderQueue = 3050 };
            }

            Vec3d centre = BodyFrame.Direction(relief.Lat, relief.Lon);
            var root = new GameObject("SubterraJets " + relief.Name) { layer = 0 };
            // A child of the PQS (the body frame): the jets turn with the ground.
            Transform pqs = body.pqsController.transform;
            root.transform.SetParent(pqs, false);
            root.transform.localPosition = (Vector3)(centre * KspSampler.SurfaceRadius(body, relief.Lat, relief.Lon)).ToKsp();
            root.transform.localRotation = Quaternion.FromToRotation(Vector3.up, ((Vector3)centre.ToKsp()).normalized);

            var rng = new System.Random(relief.Name.GetHashCode());
            foreach (Vec3d[] line in Fissures.Lines(relief, body.Radius))
            {
                var surface = new double[line.Length];
                for (int i = 0; i < line.Length; i++)
                {
                    BodyFrame.ToLatLon(line[i], out double la, out double lo);
                    surface[i] = KspSampler.SurfaceRadius(body, la, lo);
                }

                // Fresh frost along the crack.
                var frost = new GameObject("frost") { layer = 0 };
                frost.transform.SetParent(root.transform, false);
                frost.AddComponent<MeshFilter>().sharedMesh = FrostStrip(body, root.transform, line, surface, (float)relief.Width * 3.5f);
                var fr = frost.AddComponent<MeshRenderer>();
                fr.sharedMaterial = frostMaterial;
                fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fr.receiveShadows = false;

                // Jets at intervals along it (not at its fading ends), each its own height.
                for (int j = 0; j < JetsPerFissure; j++)
                {
                    double t = 0.12 + 0.76 * (j + 0.5 * rng.NextDouble()) / JetsPerFissure;
                    double f = t * (line.Length - 1);
                    int i0 = Math.Min(line.Length - 2, (int)f);
                    double u = f - i0;
                    Vec3d dir = (line[i0] * (1 - u) + line[i0 + 1] * u).Normalized;
                    double r = surface[i0] * (1 - u) + surface[i0 + 1] * u - relief.Depth * 0.6;
                    Vector3 local = root.transform.InverseTransformPoint(pqs.TransformPoint((Vector3)(dir * r).ToKsp()));
                    Vector3 up = root.transform.InverseTransformDirection(pqs.TransformDirection(((Vector3)dir.ToKsp()).normalized));
                    float h = height * (0.55f + 0.45f * (float)rng.NextDouble());
                    Jet(root, local, up, (float)relief.Width * 0.35f, h, 0.55f, 6, 9f);
                }
            }

            // The faint fan where the jets merge, high over the field.
            Jet(root, Vector3.zero, Vector3.up, height * 0.12f, height * 1.3f, 0.12f, 22, 1.2f, wide: true);
            return root;
        }

        // A jet of ice grains: fast and narrow at the crack, slowing and spreading as it rises, fading out at its top.
        private void Jet(GameObject root, Vector3 at, Vector3 up, float width, float height, float alpha, float angle, float rate, bool wide = false)
        {
            var go = new GameObject(wide ? "fan" : "jet") { layer = 0 };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float life = wide ? 26f : 14f;
            var main = ps.main;
            main.loop = true;
            main.duration = 5;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life);
            // Slowing to nothing at its top: on average about half its start speed.
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f * height / life, 2.1f * height / life);
            main.startSize = new ParticleSystem.MinMaxCurve(width * 0.8f, width * 1.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, 1, 1, alpha), new Color(0.9f, 0.95f, 1, alpha * 0.7f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = wide ? 60 : 140;
            main.gravityModifier = 0;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = width * 0.3f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.5f, wide ? 3f : 4f), new Keyframe(1, wide ? 5f : 8f)));
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 2.2f * height / life), new Keyframe(1, 0.05f * height / life)));
            limit.dampen = 0.08f;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0, 1) });
            colour.color = new ParticleSystem.MinMaxGradient(fade);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = jetMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 10f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Simulate(life, true, true);
            ps.Play(true);
        }

        // A strip of frost along a fissure, 1.5 m over the ground: bright over the crack, fading to nothing at its sides and ends.
        private static Mesh FrostStrip(CelestialBody body, Transform root, Vec3d[] line, double[] surface, float width)
        {
            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Transform pqs = body.pqsController.transform;
            for (int i = 0; i < line.Length; i++)
            {
                Vec3d along = line[Math.Min(i + 1, line.Length - 1)] - line[Math.Max(i - 1, 0)];
                Vec3d across = Vec3d.Cross(line[i], along).Normalized;
                double end = Math.Min(i, line.Length - 1 - i) / 2.0;
                float a = (float)Math.Min(1, end) * 0.85f;
                for (int k = -1; k <= 1; k++)
                {
                    Vec3d dir = (line[i] + across * (k * width * 0.5 / body.Radius)).Normalized;
                    BodyFrame.ToLatLon(dir, out double la, out double lo);
                    double r = (k == 0 ? surface[i] : KspSampler.SurfaceRadius(body, la, lo)) + 1.5;
                    verts.Add(root.InverseTransformPoint(pqs.TransformPoint((Vector3)(dir * r).ToKsp())));
                    colours.Add(new Color(1, 1, 1, k == 0 ? a : 0));
                    uvs.Add(new Vector2(k * 0.5f + 0.5f, i));
                }
            }

            for (int i = 0; i + 1 < line.Length; i++)
            {
                int b = i * 3, n = b + 3;
                tris.AddRange(new[] { b, n, b + 1, b + 1, n, n + 1, b + 1, n + 1, b + 2, b + 2, n + 1, n + 2 });
            }

            // Both faces (the strip is seen from above; its winding depends on the line's direction).
            int count = tris.Count;
            for (int i = 0; i < count; i += 3)
            {
                tris.Add(tris[i]);
                tris.Add(tris[i + 2]);
                tris.Add(tris[i + 1]);
            }

            var mesh = new Mesh { name = "SubterraFrost" };
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A soft round puff with a lumpy edge (ice grains in sunlight).
        private static Texture2D Puff()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "SubterraJetPuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 0.8f + 0.1f * Mathf.Sin(ang * 6 + 0.7f) + 0.06f * Mathf.Sin(ang * 13);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    float alpha = Mathf.Clamp01(1 - d);
                    alpha = alpha * alpha * (3 - 2 * alpha);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private void Clear()
        {
            foreach (GameObject root in roots)
            {
                if (root != null)
                {
                    foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        Destroy(filter.sharedMesh);
                    }

                    Destroy(root);
                }
            }

            roots.Clear();
            built = null;
        }

        private void OnDestroy()
        {
            Clear();
            foreach (UnityEngine.Object o in new UnityEngine.Object[] { jetMaterial, frostMaterial, puff, white })
            {
                if (o != null)
                {
                    Destroy(o);
                }
            }
        }
    }
}
