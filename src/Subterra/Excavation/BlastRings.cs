using System.Collections.Generic;
using Subterra.Core.Excavation;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// The danger rings of an armed blast hole, drawn on the ground while it is armed and during its fuse: red where
    /// vessels are destroyed, yellow where they are damaged (<see cref="BlastSafety"/>).
    /// </summary>
    public static class BlastRings
    {
        private const int Points = 96;

        private static readonly Dictionary<string, GameObject> Shown = new Dictionary<string, GameObject>();

        public static void Show(ExcavationService.ArmedBlast blast)
        {
            CelestialBody body = blast == null ? null : FlightGlobals.GetBodyByName(blast.Body);
            if (body?.pqsController == null || Shown.TryGetValue(blast.Id, out GameObject old) && old != null)
            {
                return;
            }

            (double destroy, double damage) = BlastSafety.Radii(blast.TopDeckKg, true);
            var root = new GameObject("SubterraBlastRings " + blast.Id);
            // A child of the PQS (its frame is the body frame): the rings move with the ground.
            root.transform.SetParent(body.pqsController.transform, false);
            Ring(root, body, blast, destroy, new Color(1f, 0.15f, 0.1f, 0.9f));
            Ring(root, body, blast, damage, new Color(1f, 0.8f, 0.1f, 0.8f));
            Shown[blast.Id] = root;
        }

        public static void Hide(string blastId)
        {
            if (blastId != null && Shown.TryGetValue(blastId, out GameObject root))
            {
                if (root != null)
                {
                    Object.Destroy(root);
                }

                Shown.Remove(blastId);
            }
        }

        // A circle of that radius on the terrain around the hole, half a metre over it.
        private static void Ring(GameObject root, CelestialBody body, ExcavationService.ArmedBlast blast, double radius, Color colour)
        {
            if (radius <= 0)
            {
                return;
            }

            var go = new GameObject("ring") { layer = 0 };
            go.transform.SetParent(root.transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Points;
            line.widthMultiplier = (float)Mathf.Clamp((float)radius * 0.02f, 0.3f, 3f);
            // A hole begun under the ground (in a cave, a shaft or a pocket) gets a level circle at its top, drawn over the rock
            // so it shows from down there; they were always laid on the surface (Lucas).
            bool under = blast.Stacked;
            line.material = under ? OverRock() : new Material(Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = colour;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vec3d up = BodyFrame.Direction(blast.Lat, blast.Lon);
            Vec3d east = Vec3d.Cross(new Vec3d(0, 1, 0), up);
            east = east.Length < 1e-6 ? new Vec3d(1, 0, 0) : east.Normalized;
            Vec3d north = Vec3d.Cross(up, east);
            double surface = KspSampler.SurfaceRadius(body, blast.Lat, blast.Lon);
            double level = surface - blast.HoleTop + 0.5;
            for (int i = 0; i < Points; i++)
            {
                double a = 2 * System.Math.PI * i / Points;
                Vec3d dir = (up * surface + (east * System.Math.Cos(a) + north * System.Math.Sin(a)) * radius).Normalized;
                Vec3d p;
                if (under)
                {
                    p = dir * level;
                }
                else
                {
                    BodyFrame.ToLatLon(dir, out double lat, out double lon);
                    p = dir * (KspSampler.SurfaceRadius(body, lat, lon) + 0.5);
                }

                line.SetPosition(i, (Vector3)p.ToKsp());
            }
        }

        // Unity's built-in line shader, blended and drawn over everything (no depth test): a ring through the rock.
        private static Material OverRock()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return new Material(Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default"));
            }

            var material = new Material(shader);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = 4000;
            return material;
        }
    }
}
