using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using static System.FormattableString;

namespace Subterra.DevTools
{
    /// <summary>
    /// M0 spike (Task 3): cuts a round hole in the PQS terrain under the active vessel with a
    /// runtime-injected PQSMod, keeps it cut when KSP re-stitches quad edges, and measures the result.
    /// </summary>
    [StepProvider]
    internal static class PqsHoleSpike
    {
        private static SpikeHoleMod mod;
        private static Harmony harmony;
        private static double holeLat;
        private static double holeLon;
        private static string holeBody;

        internal static void Register()
        {
            StepRegistry.RegisterSpike("PqsHole", Hole);
            StepRegistry.RegisterSpike("PqsHoleMeasure", Measure);
        }

        // spike name = PqsHole order = 50 radius = 60 — hole centred under the active vessel.
        private static IEnumerator Hole(ConfigNode node, TestContext ctx)
        {
            Vessel vessel = FlightGlobals.ActiveVessel ?? throw new InvalidOperationException("no active vessel");
            CelestialBody body = vessel.mainBody;
            PQS pqs = body.pqsController ?? throw new InvalidOperationException(body.bodyName + " has no PQS");
            int order = (int)node.ArgDouble("order", 50);
            float radius = (float)node.ArgDouble("radius", 60);

            holeLat = vessel.latitude;
            holeLon = vessel.longitude;
            holeBody = body.bodyName;

            // Two ways to the hole centre: through world space, and through the documented relative
            // frame. Their agreement validates Subterra.Core's BodyFrame convention in KSP.
            Vector3d rel = body.GetRelSurfaceNVector(holeLat, holeLon);
            double surfaceRadius = pqs.GetSurfaceHeight(rel);
            Vector3d world = body.GetWorldSurfacePosition(holeLat, holeLon, surfaceRadius - body.Radius);
            Vector3 pqsLocal = pqs.transform.InverseTransformPoint(world);
            double frameAngle = Vector3d.Angle(((Vector3d)pqsLocal).normalized, rel);

            if (mod == null)
            {
                var go = new GameObject("SubterraSpikeHole");
                go.transform.SetParent(pqs.transform, false);
                mod = go.AddComponent<SpikeHoleMod>();
                mod.sphere = pqs;
                mod.modEnabled = true;
            }

            mod.order = order;
            mod.CenterPqs = pqsLocal;
            mod.Radius = radius;
            mod.Cut.Clear();

            if (harmony == null)
            {
                harmony = new Harmony("Subterra.DevTools.PqsHoleSpike");
                harmony.Patch(
                    AccessTools.Method(typeof(PQ), "UpdateVisibility"),
                    postfix: new HarmonyMethod(typeof(PqsHoleSpike), nameof(UpdateVisibilityPostfix)));
            }

            AccessTools.Method(typeof(PQS), "SetupMods").Invoke(pqs, null);
            pqs.RebuildSphere();
            yield return null;
            yield return null;

            ctx.Pass(Invariant(
                $"{body.bodyName} lat {holeLat:F5} lon {holeLon:F5}: order {order}, radius {radius} m, surface radius {surfaceRadius:F1} m (vessel terrainAltitude {vessel.terrainAltitude:F1}), pqs-local vs GetRelSurfaceNVector {frameAngle:F6}°, quads cut {mod.Cut.Count}, triangles removed {mod.RemovedTriangles}"));
        }

        // spike name = PqsHoleMeasure — measurements for m0-findings.md; FAIL if the hole is covered.
        private static IEnumerator Measure(ConfigNode node, TestContext ctx)
        {
            if (mod == null)
            {
                throw new InvalidOperationException("run spike PqsHole first");
            }

            CelestialBody body = FlightGlobals.GetBodyByName(holeBody);
            PQS pqs = body.pqsController;
            Vector3 center = pqs.transform.TransformPoint(mod.CenterPqs);
            Vector3 up = (center - (Vector3)body.position).normalized;
            float radius = mod.Radius;

            var leaves = new List<PQ>();
            foreach (PQ root in pqs.quads)
            {
                CollectVisibleLeaves(root, leaves);
            }

            // Nearest leaf quad and its vertex spacing (mean triangle edge length).
            PQ nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (PQ quad in leaves)
            {
                Vector3 local = quad.transform.InverseTransformPoint(center);
                float d = Mathf.Sqrt(quad.mesh.bounds.SqrDistance(local));
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = quad;
                }
            }

            double spacing = nearest == null ? double.NaN : MeanEdge(nearest);

            // Triangles left inside the radius and triangles crossing the vertical through the centre.
            int inside = 0;
            int covering = 0;
            foreach (PQ quad in leaves)
            {
                Vector3 c = quad.transform.InverseTransformPoint(center);
                if (quad.mesh.bounds.SqrDistance(c) > (radius + 200f) * (radius + 200f))
                {
                    continue;
                }

                Vector3 dir = quad.transform.InverseTransformDirection(-up);
                Vector3 origin = c - dir * 50f;
                Vector3[] v = quad.mesh.vertices;
                int[] t = quad.mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    if ((v[t[i]] - c).sqrMagnitude < radius * radius
                        || (v[t[i + 1]] - c).sqrMagnitude < radius * radius
                        || (v[t[i + 2]] - c).sqrMagnitude < radius * radius)
                    {
                        inside++;
                    }

                    if (RayHitsTriangle(origin, dir, v[t[i]], v[t[i + 1]], v[t[i + 2]], 500f))
                    {
                        covering++;
                    }
                }
            }

            bool hit = Physics.Raycast(center + up * 50f, -up, out RaycastHit rayHit, 500f, 1 << 15);
            string rayText = hit ? Invariant($"hit {rayHit.collider.name} at {rayHit.distance:F1} m") : "no hit";

            // Layer-15 colliders that are not terrain quads inside the hole (Parallax scatter colliders).
            Collider[] overlaps = Physics.OverlapCapsule(center - up * 20f, center + up * 30f, radius * 0.9f, 1 << 15);
            List<string> foreign = overlaps.Where(c => c.GetComponentInParent<PQ>() == null).Select(c => c.name).ToList();

            string mods = string.Join(", ", pqs.GetComponentsInChildren<PQSMod>(true)
                .OrderBy(m => m.order)
                .Select(m => Invariant($"{m.GetType().Name}:{m.order}{(m.modEnabled ? "" : "(off)")}")));

            var report = new StringBuilder();
            report.AppendLine(Invariant($"maxLevel {pqs.maxLevel}"));
            report.AppendLine(Invariant($"nearest leaf: subdivision {(nearest == null ? -1 : nearest.subdivision)}, distance {nearestDistance:F1} m, mean vertex spacing {spacing:F2} m"));
            report.AppendLine(Invariant($"visible leaves {leaves.Count}, quads cut {mod.Cut.Count}, recuts {mod.Recuts}"));
            report.AppendLine(Invariant($"triangles with a vertex inside {radius} m: {inside}; covering the centre: {covering}"));
            report.AppendLine("raycast from 50 m above, layer 15: " + rayText);
            report.AppendLine(Invariant($"non-quad layer-15 colliders in the hole: {foreign.Count} {string.Join(", ", foreign.Distinct())}"));
            report.AppendLine("PQS mods by order: " + mods);
            File.WriteAllText(Path.Combine(ctx.ResultsDir, "hole-measure.txt"), report.ToString());

            string summary = Invariant(
                $"maxLevel {pqs.maxLevel}, spacing {spacing:F2} m (subdivision {(nearest == null ? -1 : nearest.subdivision)}), inside {inside}, covering {covering}, ray {rayText}, foreign colliders {foreign.Count}, recuts {mod.Recuts}");
            if (inside > 0 || covering > 0 || hit || foreign.Count > 0)
            {
                ctx.Fail(summary);
            }
            else
            {
                ctx.Pass(summary);
            }

            yield break;
        }

        // Re-applies the cut when KSP re-stitches a quad's edges (it reassigns mesh.triangles).
        private static void UpdateVisibilityPostfix(PQ __instance)
        {
            if (mod != null && mod.Cut.TryGetValue(__instance, out PQS.EdgeState state) && state != __instance.edgeState)
            {
                mod.Recuts++;
                mod.CutQuad(__instance);
            }
        }

        private static void CollectVisibleLeaves(PQ quad, List<PQ> leaves)
        {
            if (quad == null)
            {
                return;
            }

            if (quad.isSubdivided && quad.subNodes != null)
            {
                foreach (PQ child in quad.subNodes)
                {
                    CollectVisibleLeaves(child, leaves);
                }
            }
            else if (quad.isVisible && quad.isBuilt && quad.mesh != null)
            {
                leaves.Add(quad);
            }
        }

        private static double MeanEdge(PQ quad)
        {
            Vector3[] v = quad.mesh.vertices;
            int[] t = quad.mesh.triangles;
            double sum = 0;
            int count = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                sum += (v[t[i]] - v[t[i + 1]]).magnitude;
                count++;
            }

            return count == 0 ? double.NaN : sum / count;
        }

        // Möller–Trumbore, both faces.
        private static bool RayHitsTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, float maxDistance)
        {
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-8f)
            {
                return false;
            }

            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(dir, q) * inv;
            if (w < 0 || u + w > 1)
            {
                return false;
            }

            float distance = Vector3.Dot(e2, q) * inv;
            return distance >= 0 && distance <= maxDistance;
        }
    }

    /// <summary>Removes the triangles of each built quad that have a vertex inside the hole radius.</summary>
    internal sealed class SpikeHoleMod : PQSMod
    {
        public Vector3 CenterPqs;
        public float Radius;
        public readonly Dictionary<PQ, PQS.EdgeState> Cut = new Dictionary<PQ, PQS.EdgeState>();
        public int RemovedTriangles;
        public int Recuts;

        public override void OnQuadBuilt(PQ quad)
        {
            CutQuad(quad);
        }

        public override void OnQuadDestroy(PQ quad)
        {
            Cut.Remove(quad);
        }

        public void CutQuad(PQ quad)
        {
            Mesh mesh = quad.mesh;
            if (mesh == null)
            {
                return;
            }

            Vector3 c = quad.transform.InverseTransformPoint(sphere.transform.TransformPoint(CenterPqs));
            if (mesh.bounds.SqrDistance(c) > Radius * Radius)
            {
                return;
            }

            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            var kept = new List<int>(t.Length);
            float r2 = Radius * Radius;
            for (int i = 0; i < t.Length; i += 3)
            {
                if ((v[t[i]] - c).sqrMagnitude < r2 || (v[t[i + 1]] - c).sqrMagnitude < r2 || (v[t[i + 2]] - c).sqrMagnitude < r2)
                {
                    continue;
                }

                kept.Add(t[i]);
                kept.Add(t[i + 1]);
                kept.Add(t[i + 2]);
            }

            if (kept.Count == t.Length)
            {
                if (Cut.ContainsKey(quad))
                {
                    Cut[quad] = quad.edgeState;
                }

                return;
            }

            RemovedTriangles += (t.Length - kept.Count) / 3;
            mesh.triangles = kept.ToArray();
            if (quad.meshCollider != null && quad.meshCollider.sharedMesh != null)
            {
                quad.meshCollider.sharedMesh = null;
                quad.meshCollider.sharedMesh = mesh;
            }

            Cut[quad] = quad.edgeState;
        }
    }
}
