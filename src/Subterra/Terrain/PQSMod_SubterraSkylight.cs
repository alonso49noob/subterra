using System;
using System.Collections.Generic;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Cuts the finest terrain quads over the skylights: removes every triangle that overlaps a
    /// skylight's disc (seen along the local vertical) and refreshes the quad's collider. Registered
    /// quads are cut again when KSP re-stitches their edges (patch <c>pqsHole</c>). Runs before Parallax
    /// (order 50 vs 999999) so no scatter grows over the hole. Coarse quads are left whole (a coarse
    /// triangle is hundreds of metres wide); the far cap stands in for the hole until it is cut.
    /// </summary>
    public sealed class PQSMod_SubterraSkylight : PQSMod
    {
        public const int ModOrder = 50;

        private readonly List<Hole> holes = new List<Hole>();
        private readonly Dictionary<PQ, PQS.EdgeState> cut = new Dictionary<PQ, PQS.EdgeState>();

        public int HoleCount => holes.Count;

        public int CutQuads => cut.Count;

        public int Recuts { get; private set; }

        /// <summary>Distance between terrain vertices at the finest level (m).</summary>
        public double VertexSpacing => sphere == null ? 50 : Math.PI / 2 * sphere.radius / (1 << sphere.maxLevel) / (PQS.cacheSideVertCount - 1);

        /// <summary>How far beyond the skylight radius the cut can reach (a removed triangle's diagonal), plus a margin.</summary>
        public float CutReach => (float)(VertexSpacing * Math.Sqrt(2) * 1.2);

        /// <summary>Adds a hole (centre and up in body coordinates) and cuts the quads already built; returns its index.</summary>
        public int AddHole(Vector3d centre, Vector3 up, float radius) => AddHoles(new[] { (centre, up, radius) })[0];

        /// <summary>Adds several holes and cuts the quads already built in one pass; returns their indices.</summary>
        public int[] AddHoles(IList<(Vector3d centre, Vector3 up, float radius)> added)
        {
            var indices = new int[added.Count];
            var fresh = new List<Hole>(added.Count);
            for (int i = 0; i < added.Count; i++)
            {
                var hole = new Hole { Centre = added[i].centre, Up = added[i].up.normalized, Radius = added[i].radius, Quads = new HashSet<PQ>() };
                holes.Add(hole);
                fresh.Add(hole);
                indices[i] = holes.Count - 1;
            }

            // Built quads are already cut by the older holes: only the new ones are tested.
            if (sphere != null && sphere.quads != null)
            {
                foreach (PQ root in sphere.quads)
                {
                    CutTree(root, fresh);
                }
            }

            return indices;
        }

        /// <summary>True while a cut quad of that hole is on screen (the terrain is open there).</summary>
        public bool IsOpen(int hole)
        {
            if (hole < 0 || hole >= holes.Count)
            {
                return false;
            }

            foreach (PQ quad in holes[hole].Quads)
            {
                if (quad != null && quad.isVisible && !quad.isSubdivided && cut.ContainsKey(quad))
                {
                    return true;
                }
            }

            return false;
        }

        public override void OnQuadBuilt(PQ quad)
        {
            using (SubterraClock.Measure("PqsHole"))
            {
                Cut(quad, holes);
            }
        }

        public override void OnQuadDestroy(PQ quad)
        {
            cut.Remove(quad);
            foreach (Hole hole in holes)
            {
                hole.Quads.Remove(quad);
            }
        }

        /// <summary>Called after PQ.UpdateVisibility: re-applies the cut if KSP swapped the quad's triangles.</summary>
        public void AfterVisibilityUpdate(PQ quad)
        {
            if (cut.TryGetValue(quad, out PQS.EdgeState state) && state != quad.edgeState)
            {
                Recuts++;
                using (SubterraClock.Measure("PqsHole"))
                {
                    Cut(quad, holes);
                }
            }
        }

        private void CutTree(PQ quad, List<Hole> subset)
        {
            if (quad == null)
            {
                return;
            }

            if (quad.isSubdivided && quad.subNodes != null)
            {
                foreach (PQ child in quad.subNodes)
                {
                    CutTree(child, subset);
                }
            }

            if (quad.isBuilt && quad.mesh != null)
            {
                Cut(quad, subset);
            }
        }

        private void Cut(PQ quad, List<Hole> subset)
        {
            Mesh mesh = quad.mesh;
            if (mesh == null || subset.Count == 0 || quad.subdivision < sphere.maxLevel)
            {
                return;
            }

            Bounds bounds = mesh.bounds;
            float reach = CutReach;

            // Coarse test in sphere coordinates first (one transform per quad, not two per hole).
            Vector3d quadCentre = sphere.transform.InverseTransformPoint(quad.transform.TransformPoint(bounds.center));
            double quadRadius = bounds.extents.magnitude;
            Vector3[] v = null;
            int[] t = null;
            bool changed = false;
            foreach (Hole hole in subset)
            {
                double near = quadRadius + hole.Radius + reach;
                if ((hole.Centre - quadCentre).sqrMagnitude > near * near)
                {
                    continue;
                }

                Vector3 c = quad.transform.InverseTransformPoint(sphere.transform.TransformPoint(hole.Centre));
                float holeReach = hole.Radius + reach;
                if (bounds.SqrDistance(c) > holeReach * holeReach)
                {
                    continue;
                }

                Vector3 up = quad.transform.InverseTransformDirection(sphere.transform.TransformDirection(hole.Up));
                v = v ?? mesh.vertices;
                t = t ?? mesh.triangles;
                var next = new List<int>(t.Length);
                for (int i = 0; i < t.Length; i += 3)
                {
                    if (!Overlaps(v[t[i]], v[t[i + 1]], v[t[i + 2]], c, up, hole.Radius))
                    {
                        next.Add(t[i]);
                        next.Add(t[i + 1]);
                        next.Add(t[i + 2]);
                    }
                }

                if (next.Count != t.Length)
                {
                    t = next.ToArray();
                    changed = true;
                    hole.Quads.Add(quad);
                }
            }

            if (!changed)
            {
                if (cut.ContainsKey(quad))
                {
                    cut[quad] = quad.edgeState;
                }

                return;
            }

            mesh.triangles = t;
            if (quad.meshCollider != null && quad.meshCollider.sharedMesh != null)
            {
                quad.meshCollider.sharedMesh = null;
                quad.meshCollider.sharedMesh = mesh;
            }

            cut[quad] = quad.edgeState;
        }

        // Triangle and disc, both projected on the plane through c perpendicular to up.
        private static bool Overlaps(Vector3 a, Vector3 b, Vector3 d, Vector3 c, Vector3 up, float r)
        {
            Vector3 e1 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 e2 = Vector3.Cross(up, e1);
            Vector2 pa = Flat(a - c, e1, e2), pb = Flat(b - c, e1, e2), pd = Flat(d - c, e1, e2);
            float r2 = r * r;
            if (pa.sqrMagnitude < r2 || pb.sqrMagnitude < r2 || pd.sqrMagnitude < r2)
            {
                return true;
            }

            if (SegmentDistance2(pa, pb) < r2 || SegmentDistance2(pb, pd) < r2 || SegmentDistance2(pd, pa) < r2)
            {
                return true;
            }

            // Centre inside the triangle.
            float s1 = Cross(pb - pa, -pa), s2 = Cross(pd - pb, -pb), s3 = Cross(pa - pd, -pd);
            return s1 >= 0 && s2 >= 0 && s3 >= 0 || s1 <= 0 && s2 <= 0 && s3 <= 0;
        }

        private static Vector2 Flat(Vector3 v, Vector3 e1, Vector3 e2) => new Vector2(Vector3.Dot(v, e1), Vector3.Dot(v, e2));

        private static float SegmentDistance2(Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(-Vector2.Dot(a, ab) / ab.sqrMagnitude) : 0;
            return (a + ab * t).sqrMagnitude;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private sealed class Hole
        {
            public Vector3d Centre;
            public Vector3 Up;
            public float Radius;
            public HashSet<PQ> Quads;
        }
    }
}
