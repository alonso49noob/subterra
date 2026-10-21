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
            line.material = new Material(Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = colour;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vec3d up = BodyFrame.Direction(blast.Lat, blast.Lon);
            Vec3d east = Vec3d.Cross(new Vec3d(0, 1, 0), up);
            east = east.Length < 1e-6 ? new Vec3d(1, 0, 0) : east.Normalized;
            Vec3d north = Vec3d.Cross(up, east);
            double surface = KspSampler.SurfaceRadius(body, blast.Lat, blast.Lon);
            for (int i = 0; i < Points; i++)
            {
                double a = 2 * System.Math.PI * i / Points;
                Vec3d dir = (up * surface + (east * System.Math.Cos(a) + north * System.Math.Sin(a)) * radius).Normalized;
                BodyFrame.ToLatLon(dir, out double lat, out double lon);
                Vec3d p = dir * (KspSampler.SurfaceRadius(body, lat, lon) + 0.5);
                line.SetPosition(i, (Vector3)p.ToKsp());
            }
        }
    }
}
