using System;
using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The Core Crystal (Lucas: the core should hold something more fascinating than science, as Astroneer's does): a giant
    /// cluster of crystal floating at the very centre of every body, in its Core Chamber, where nothing pulls it any way. It
    /// glows from within with a colour of the body's own (its core rock's), lights the white-hot walls round it and turns
    /// slowly; a craft can touch it (a collider at its heart). Reaching it is the Centre reward (CentreReward), and its
    /// chamber is a biome of its own with its story in the science logs. Shown only while the active vessel is near the centre. [Claude, revisar]
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class CoreCrystal : MonoBehaviour
    {
        /// <summary>Metres from the chamber's wall within which the crystal is built.</summary>
        private const double ShowReach = 3000;

        /// <summary>Degrees per second it turns.</summary>
        private const float Spin = 1.5f;

        private GameObject crystal;
        private CelestialBody shownBody;
        private Material material;
        private float nextLook;

        /// <summary>Whether the crystal is shown (for tests).</summary>
        public static bool Shown { get; private set; }

        private void Update()
        {
            if (crystal != null)
            {
                crystal.transform.Rotate(Vector3.up, Spin * Time.deltaTime, Space.Self);
            }

            if (Time.time < nextLook || !FlightGlobals.ready)
            {
                return;
            }

            nextLook = Time.time + 1f;
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = v != null ? v.mainBody : null;
            bool near = body != null && body.pqsController != null && BodyRegistry.Instance.TryGet(body, out _)
                && (v.CoMD - body.position).magnitude < SubterraLoader.CoreChamberRadius(body) + ShowReach;
            if (!near)
            {
                Drop();
                return;
            }

            if (crystal == null || shownBody != body)
            {
                Drop();
                Build(body);
            }
        }

        private void Build(CelestialBody body)
        {
            shownBody = body;
            float radius = (float)SubterraLoader.CoreChamberRadius(body);
            Color tint = CoreTint(body);
            material = new Material(MaterialFactory.Crystal(tint)) { name = "Subterra core crystal" };
            // Its own light from within: the rock's glow at the core's heat, tinted with the crystal's colour.
            if (material.HasProperty("_RimColor"))
            {
                material.SetColor("_RimColor", tint * 1.6f);
                if (material.HasProperty("_RimFalloff"))
                {
                    material.SetFloat("_RimFalloff", 0.6f);
                }
            }

            if (material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", tint * 0.8f);
            }

            crystal = new GameObject("SubterraCoreCrystal " + body.bodyName) { layer = ChunkObject.VisualLayer };
            // At the centre of the body, turning with it.
            crystal.transform.SetParent(body.pqsController.transform, false);
            crystal.transform.localPosition = Vector3.zero;
            float size = radius * 0.22f;
            crystal.transform.localScale = Vector3.one * size;

            // A big cluster pointing every way: the geode prisms, mirrored so they grow out of one heart.
            Mesh half = MeshUploader.Upload(Core.Scatter.PropGenerator.Build("GeodeCrystal01", 0), "SubterraCoreCrystal", false);
            for (int k = 0; k < 2; k++)
            {
                var part = new GameObject(k == 0 ? "upper" : "lower") { layer = ChunkObject.VisualLayer };
                part.transform.SetParent(crystal.transform, false);
                part.transform.localRotation = k == 0 ? Quaternion.identity : Quaternion.Euler(180, 37, 0);
                part.transform.localPosition = new Vector3(0, k == 0 ? -0.1f : 0.1f, 0);
                part.AddComponent<MeshFilter>().sharedMesh = half;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // A slight collider: a kerbal can touch it (and read its story) without the cluster's every spike.
            var touch = crystal.AddComponent<SphereCollider>();
            touch.radius = 0.6f;
            crystal.layer = ChunkObject.Layer;

            var glow = new GameObject("glow").AddComponent<Light>();
            glow.transform.SetParent(crystal.transform, false);
            glow.type = LightType.Point;
            glow.color = tint;
            glow.range = radius * 1.8f;
            glow.intensity = 2.2f;
            glow.shadows = LightShadows.None;
            Shown = true;
            Log.Info($"Core crystal shown at the centre of {body.bodyName} ({radius:F0} m chamber)");
        }

        // A colour of the body's own: from its deepest layer's tint, made bright and saturated (a crystal, not rock).
        private static Color CoreTint(CelestialBody body)
        {
            float[] t = { 0.6f, 0.8f, 1f };
            if (BodyRegistry.Instance.TryGet(body, out BodyRuntime runtime) && runtime.Strata.Layers.Count > 0)
            {
                float[] deepest = runtime.Strata.Layers[runtime.Strata.Layers.Count - 1].Tint;
                if (deepest != null && deepest.Length >= 3)
                {
                    t = deepest;
                }
            }

            Color.RGBToHSV(new Color(t[0], t[1], t[2]), out float h, out float s, out float _);
            // Each body its own hue, shifted from its core rock's (so iron cores do not all give the same orange).
            h = (h + (Math.Abs(body.bodyName.GetHashCode()) % 360) / 360f * 0.5f) % 1f;
            return Color.HSVToRGB(h, Mathf.Clamp(s + 0.35f, 0.45f, 0.85f), 1f);
        }

        private void Drop()
        {
            if (crystal != null)
            {
                var meshes = new HashSet<Mesh>();
                foreach (MeshFilter filter in crystal.GetComponentsInChildren<MeshFilter>())
                {
                    meshes.Add(filter.sharedMesh);
                }

                foreach (Mesh mesh in meshes)
                {
                    Destroy(mesh);
                }

                Destroy(crystal);
                crystal = null;
            }

            if (material != null)
            {
                Destroy(material);
                material = null;
            }

            shownBody = null;
            Shown = false;
        }

        private void OnDestroy() => Drop();
    }
}
