using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        public int AddHole(Vector3d centre, Vector3 up, float radius, bool clip = false) => AddHoles(new[] { (centre, up, radius, clip, (Func<Vector3d, float>)null) })[0];

        /// <summary>Adds several holes and cuts the quads already built in one pass; returns their indices.</summary>
        public int[] AddHoles(IList<(Vector3d centre, Vector3 up, float radius, bool clip, Func<Vector3d, float> field)> added)
        {
            var indices = new int[added.Count];
            var fresh = new List<Hole>(added.Count);
            for (int i = 0; i < added.Count; i++)
            {
                var hole = new Hole { Centre = added[i].centre, Up = added[i].up.normalized, Radius = added[i].radius, Clip = added[i].clip, Field = added[i].field, Quads = new HashSet<PQ>() };
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

        /// <summary>Cuts again the quads near hole <paramref name="index"/>: its shape changed (a tunnel mouth grew).</summary>
        public void Refresh(int index)
        {
            if (index < 0 || index >= holes.Count || sphere == null || sphere.quads == null)
            {
                return;
            }

            var one = new List<Hole> { holes[index] };
            foreach (PQ root in sphere.quads)
            {
                CutTree(root, one);
            }
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
            // The pooled mesh goes back as KSP made it (its own vertex count): KSP refills it for the next quad.
            Restore(quad);
            cut.Remove(quad);
            foreach (Hole hole in holes)
            {
                hole.Quads.Remove(quad);
            }
        }

        /// <summary>Called after PQ.UpdateVisibility: re-applies the cut if KSP swapped the quad's triangles.</summary>
        public void AfterVisibilityUpdate(PQ quad)
        {
            SyncPatch(quad);
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

        /// <summary>Target size (m) of the grid a clipped hole's edge is cut on: the edge is this smooth.</summary>
        private const float ClipCell = 1.25f;

        // KSP's own mesh of a quad (vertex count, and its triangles while ours are set) and the triangles we last set.
        private readonly Dictionary<PQ, Origin> origins = new Dictionary<PQ, Origin>();

        private sealed class Origin
        {
            public int Count;
            public int[] Triangles;
            public int[] Applied;
        }

        // Cuts the quad from KSP's mesh (not on top of an earlier cut) with every hole near it: removed triangles for
        // dug mouths, a smooth round edge clipped into the terrain for the others. `subset`: holes worth a look.
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
            var near = new List<(Hole hole, Vector3 c, Vector3 up)>();
            bool any = false;
            foreach (Hole hole in holes)
            {
                double limit = quadRadius + hole.Radius + reach;
                if ((hole.Centre - quadCentre).sqrMagnitude > limit * limit)
                {
                    continue;
                }

                Vector3 c = quad.transform.InverseTransformPoint(sphere.transform.TransformPoint(hole.Centre));
                float holeReach = hole.Radius + reach;
                if (bounds.SqrDistance(c) > holeReach * holeReach)
                {
                    continue;
                }

                near.Add((hole, c, quad.transform.InverseTransformDirection(sphere.transform.TransformDirection(hole.Up))));
                any |= subset == holes || subset.Contains(hole);
            }

            if (near.Count == 0 || !any)
            {
                if (near.Count == 0)
                {
                    DropPatch(quad);
                }

                if (cut.ContainsKey(quad))
                {
                    cut[quad] = quad.edgeState;
                }

                return;
            }

            int[] t = BaseTriangles(quad, mesh, out Origin origin);
            Vector3[] v = mesh.vertices;
            List<Core.Meshing.ClipHole> clips = null;
            bool changed = false;
            foreach ((Hole hole, Vector3 c, Vector3 up) in near)
            {
                if (hole.Clip)
                {
                    clips = clips ?? new List<Core.Meshing.ClipHole>();
                    Func<Core.Numerics.Vec3, float> field = null;
                    if (hole.Field != null)
                    {
                        Matrix4x4 toBody = sphere.transform.worldToLocalMatrix * quad.transform.localToWorldMatrix;
                        Func<Vector3d, float> bodyField = hole.Field;
                        field = p => bodyField(toBody.MultiplyPoint3x4(p.ToUnity()));
                    }

                    clips.Add(new Core.Meshing.ClipHole(c.ToCore(), up.ToCore(), hole.Radius, field));
                    hole.Quads.Add(quad);
                    continue;
                }

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

            var added = new List<Core.Meshing.TerrainClip.Blend>();
            if (clips != null)
            {
                var core = new Core.Numerics.Vec3[origin.Count];
                for (int i = 0; i < core.Length; i++)
                {
                    core[i] = v[i].ToCore();
                }

                int[] clipped = Core.Meshing.TerrainClip.Clip(core, t, clips, ClipCell, added);
                changed |= !ReferenceEquals(clipped, t);
                t = clipped;
            }

            if (!changed)
            {
                if (cut.ContainsKey(quad))
                {
                    cut[quad] = quad.edgeState;
                }

                return;
            }

            // KSP's mesh keeps its own vertices (KSP rewrites their normals and UVs itself): the triangles a clipped hole
            // split go to a patch of their own on the quad, with the terrain's material and every attribute blended alike.
            var own = new List<int>(t.Length);
            var split = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                List<int> to = t[i] < origin.Count && t[i + 1] < origin.Count && t[i + 2] < origin.Count ? own : split;
                to.Add(t[i]);
                to.Add(t[i + 1]);
                to.Add(t[i + 2]);
            }

            int[] ownArray = own.ToArray();
            mesh.triangles = ownArray;
            origin.Applied = ownArray;
            SetPatch(quad, mesh, origin.Count, split, added);
            if (quad.meshCollider != null && quad.meshCollider.sharedMesh != null)
            {
                quad.meshCollider.sharedMesh = null;
                quad.meshCollider.sharedMesh = mesh;
            }

            cut[quad] = quad.edgeState;
        }

        // KSP's triangles for the quad as it is now: the ones we stored if ours are set, else the mesh's own (stored).
        // The mesh is left with KSP's vertices only.
        private int[] BaseTriangles(PQ quad, Mesh mesh, out Origin origin)
        {
            if (!origins.TryGetValue(quad, out origin))
            {
                origin = new Origin { Count = mesh.vertexCount };
                origins[quad] = origin;
            }

            int[] current = mesh.triangles;
            bool ours = origin.Applied != null && SameTriangles(current, origin.Applied);
            if (!ours)
            {
                origin.Triangles = current;
            }

            mesh.triangles = origin.Triangles;
            return origin.Triangles;
        }

        // Puts the quad's mesh back as KSP built it (before it is pooled or rebuilt).
        private void Restore(PQ quad)
        {
            DropPatch(quad);
            if (!origins.TryGetValue(quad, out Origin origin))
            {
                return;
            }

            origins.Remove(quad);
            Mesh mesh = quad.mesh;
            if (mesh == null)
            {
                return;
            }

            if (origin.Applied != null && origin.Triangles != null && SameTriangles(mesh.triangles, origin.Applied))
            {
                mesh.triangles = origin.Triangles;
            }
        }

        private static bool SameTriangles(int[] a, int[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        // The patch of each quad a clipped hole split: the kept parts of its split triangles, drawn and collided with
        // like the terrain (a child of the quad, its material, every vertex attribute blended from KSP's).
        private readonly Dictionary<PQ, GameObject> patches = new Dictionary<PQ, GameObject>();

        private void DropPatch(PQ quad)
        {
            if (patches.TryGetValue(quad, out GameObject go))
            {
                patches.Remove(quad);
                if (go != null)
                {
                    Mesh old = go.GetComponent<MeshFilter>()?.sharedMesh;
                    Destroy(go);
                    if (old != null)
                    {
                        Destroy(old);
                    }
                }
            }
        }

        // Shows the patch exactly when KSP shows the quad.
        private void SyncPatch(PQ quad)
        {
            if (patches.TryGetValue(quad, out GameObject go) && go != null && quad.meshRenderer != null)
            {
                go.GetComponent<MeshRenderer>().enabled = quad.meshRenderer.enabled;
                if (quad.meshRenderer.HasPropertyBlock())
                {
                    var block = new MaterialPropertyBlock();
                    quad.meshRenderer.GetPropertyBlock(block);
                    go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                }
            }
        }

        private void SetPatch(PQ quad, Mesh source, int count, List<int> triangles, List<Core.Meshing.TerrainClip.Blend> added)
        {
            DropPatch(quad);
            if (triangles.Count == 0)
            {
                return;
            }

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colours = new List<Color>();
            source.GetVertices(positions);
            source.GetNormals(normals);
            source.GetTangents(tangents);
            source.GetColors(colours);
            var uvs = new List<Vector4>[4];
            var dims = new int[4];
            for (int ch = 0; ch < 4; ch++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
                dims[ch] = source.HasVertexAttribute(attribute) ? source.GetVertexAttributeDimension(attribute) : 0;
                if (dims[ch] > 0)
                {
                    uvs[ch] = new List<Vector4>();
                    source.GetUVs(ch, uvs[ch]);
                }
            }

            // Only the vertices the patch uses, KSP's first, then the blended ones, renumbered.
            var map = new Dictionary<int, int>();
            var pPos = new List<Vector3>();
            var pNorm = new List<Vector3>();
            var pTan = new List<Vector4>();
            var pCol = new List<Color>();
            var pUv = new List<Vector4>[4];
            for (int ch = 0; ch < 4; ch++)
            {
                pUv[ch] = uvs[ch] != null && uvs[ch].Count == positions.Count ? new List<Vector4>() : null;
            }

            bool hasNormals = normals.Count == positions.Count, hasTangents = tangents.Count == positions.Count, hasColours = colours.Count == positions.Count;
            T Blend<T>(List<T> list, int i, Func<T, float, T> scale, Func<T, T, T> add)
            {
                if (i < count)
                {
                    return list[i];
                }

                Core.Meshing.TerrainClip.Blend b = added[i - count];
                return add(add(scale(list[b.A], b.Wa), scale(list[b.B], b.Wb)), scale(list[b.C], b.Wc));
            }

            var patchTriangles = new int[triangles.Count];
            for (int k = 0; k < triangles.Count; k++)
            {
                int i = triangles[k];
                if (!map.TryGetValue(i, out int j))
                {
                    j = pPos.Count;
                    map[i] = j;
                    pPos.Add(Blend(positions, i, (v, w) => v * w, (a, b) => a + b));
                    if (hasNormals)
                    {
                        pNorm.Add(Blend(normals, i, (v, w) => v * w, (a, b) => a + b).normalized);
                    }

                    if (hasTangents)
                    {
                        pTan.Add(Blend(tangents, i, (v, w) => v * w, (a, b) => a + b));
                    }

                    if (hasColours)
                    {
                        pCol.Add(Blend(colours, i, (v, w) => v * w, (a, b) => a + b));
                    }

                    for (int ch = 0; ch < 4; ch++)
                    {
                        pUv[ch]?.Add(Blend(uvs[ch], i, (v, w) => v * w, (a, b) => a + b));
                    }
                }

                patchTriangles[k] = j;
            }

            var mesh = new Mesh { name = "SubterraTerrainPatch" };
            if (pPos.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(pPos);
            if (hasNormals)
            {
                mesh.SetNormals(pNorm);
            }

            if (hasTangents)
            {
                mesh.SetTangents(pTan);
            }

            if (hasColours)
            {
                mesh.SetColors(pCol);
            }

            for (int ch = 0; ch < 4; ch++)
            {
                if (pUv[ch] == null)
                {
                    continue;
                }

                if (dims[ch] <= 2)
                {
                    mesh.SetUVs(ch, pUv[ch].ConvertAll(u => new Vector2(u.x, u.y)));
                }
                else if (dims[ch] == 3)
                {
                    mesh.SetUVs(ch, pUv[ch].ConvertAll(u => new Vector3(u.x, u.y, u.z)));
                }
                else
                {
                    mesh.SetUVs(ch, pUv[ch]);
                }
            }

            mesh.SetTriangles(patchTriangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("SubterraTerrainPatch") { layer = quad.gameObject.layer };
            go.transform.SetParent(quad.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = quad.meshRenderer != null ? quad.meshRenderer.sharedMaterial : sphere.surfaceMaterial;
            renderer.shadowCastingMode = quad.meshRenderer != null ? quad.meshRenderer.shadowCastingMode : ShadowCastingMode.On;
            renderer.receiveShadows = quad.meshRenderer == null || quad.meshRenderer.receiveShadows;
            renderer.lightProbeUsage = quad.meshRenderer != null ? quad.meshRenderer.lightProbeUsage : LightProbeUsage.Off;
            renderer.reflectionProbeUsage = quad.meshRenderer != null ? quad.meshRenderer.reflectionProbeUsage : ReflectionProbeUsage.Off;
            if (quad.meshRenderer != null && quad.meshRenderer.HasPropertyBlock())
            {
                // KSP gives each quad its own shader values (fading, tiling): the patch takes the same.
                var block = new MaterialPropertyBlock();
                quad.meshRenderer.GetPropertyBlock(block);
                renderer.SetPropertyBlock(block);
            }

            if (quad.meshCollider != null)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            patches[quad] = go;
            SyncPatch(quad);
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

            // Clipped into the terrain with a smooth round edge (true), or whole triangles removed (false).
            public bool Clip;

            // The hole's shape in body coordinates (negative inside), for a clipped hole that is not round; Radius bounds it.
            public Func<Vector3d, float> Field;
            public HashSet<PQ> Quads;
        }
    }
}
