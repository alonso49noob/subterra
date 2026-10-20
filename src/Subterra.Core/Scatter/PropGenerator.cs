using System;
using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;

namespace Subterra.Core.Scatter
{
    /// <summary>
    /// Procedural cave props (deformed icospheres): closed meshes with outward normals, the prop's
    /// origin at its base (Y up), LOD1 with a quarter of the triangles. Deterministic per name.
    /// </summary>
    public static class PropGenerator
    {
        public static readonly string[] Names = { "Boulder01", "Boulder02", "Boulder03", "RubbleCone01", "Lavacicle01", "PahoehoeSlab01", "IceCluster01" };

        /// <summary>The prop for a model url or name (the part after the last '/'); null if unknown.</summary>
        public static MeshData Build(string model, int lod)
        {
            string name = model;
            int slash = name.LastIndexOf('/');
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }

            if (name.EndsWith("_lod1", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - 5);
                lod = 1;
            }

            int subdivisions = lod == 0 ? 3 : 2;
            ulong seed = Hash.Of(name);
            switch (name)
            {
                case "Boulder01": return Rock(subdivisions, seed, new Vec3(1.0f, 0.75f, 0.9f), 0.25f);
                case "Boulder02": return Rock(subdivisions, seed, new Vec3(1.2f, 0.6f, 0.8f), 0.3f);
                case "Boulder03": return Rock(subdivisions, seed, new Vec3(0.8f, 0.9f, 0.85f), 0.35f);
                case "RubbleCone01": return Shape(subdivisions, seed, p => Cone(p), 0.12f);
                case "Lavacicle01": return Shape(subdivisions, seed, p => Icicle(p), 0.05f, hanging: true);
                case "PahoehoeSlab01": return Shape(subdivisions, seed, p => new Vec3(p.X * 1.6f, (p.Y + 1) * 0.12f, p.Z * 1.2f), 0.06f, baseAtZero: false);
                case "IceCluster01": return Crystals(lod == 0 ? 2 : 1, seed);
                default: return null;
            }
        }

        private static MeshData Rock(int subdivisions, ulong seed, Vec3 scale, float roughness) =>
            Shape(subdivisions, seed, p => new Vec3(p.X * scale.X, p.Y * scale.Y, p.Z * scale.Z), roughness);

        private static Vec3 Cone(Vec3 p)
        {
            // Wide base, rounded top: height 1, radius 1.5 at the base.
            float t = (p.Y + 1) * 0.5f;
            float r = 1.5f * (1 - t * 0.85f);
            return new Vec3(p.X * r, t * 1.0f, p.Z * r);
        }

        private static Vec3 Icicle(Vec3 p)
        {
            // Top flat against the ceiling (y = 0), tapering down to a point 1.8 m below.
            float t = Math.Max(0f, Math.Min(1f, (p.Y + 1) * 0.5f));
            float r = 0.35f * (0.05f + 0.95f * (float)Math.Pow(t, 0.7));
            return new Vec3(p.X * r, -1.8f * (1 - t), p.Z * r);
        }

        // Unit icosphere deformed by `shape`, roughened along the normal, base moved to y = 0.
        private static MeshData Shape(int subdivisions, ulong seed, Func<Vec3, Vec3> shape, float roughness, bool hanging = false, bool baseAtZero = true)
        {
            Icosphere(subdivisions, out List<Vec3> unit, out List<int> tris);
            int[][] noise = Noise.Tables(seed);
            var mesh = new MeshData();
            foreach (Vec3 u in unit)
            {
                float bump = 1 + roughness * Noise.Fbm(u * 1.7f, noise, 3);
                mesh.Vertices.Add(shape(u * bump));
            }

            if (baseAtZero && !hanging)
            {
                float minY = float.MaxValue;
                foreach (Vec3 v in mesh.Vertices)
                {
                    minY = Math.Min(minY, v.Y);
                }

                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    Vec3 v = mesh.Vertices[i];
                    mesh.Vertices[i] = new Vec3(v.X, v.Y - minY - 0.15f, v.Z);
                }
            }

            mesh.Triangles.AddRange(tris);
            Finish(mesh);
            return mesh;
        }

        // A few hexagonal-ish crystals leaning out from a common base.
        private static MeshData Crystals(int subdivisions, ulong seed)
        {
            var rng = new Rng(seed);
            var mesh = new MeshData();
            int count = 5;
            for (int c = 0; c < count; c++)
            {
                double angle = rng.Range(0, 2 * Math.PI), lean = rng.Range(0.1, 0.5);
                float height = (float)rng.Range(0.8, 1.8), width = (float)rng.Range(0.12, 0.25);
                var axis = new Vec3((float)(Math.Cos(angle) * lean), 1, (float)(Math.Sin(angle) * lean)).Normalized;
                Vec3 side = Vec3.Cross(axis, new Vec3(0, 0, 1)).Normalized;
                Vec3 other = Vec3.Cross(side, axis);
                Icosphere(subdivisions, out List<Vec3> unit, out List<int> tris);
                int offset = mesh.Vertices.Count;
                foreach (Vec3 u in unit)
                {
                    float t = (u.Y + 1) * 0.5f;
                    float r = width * (t < 0.8f ? 1f : 0.1f + (1 - t) * 4.5f);
                    mesh.Vertices.Add(axis * (t * height - 0.1f) + side * (u.X * r) + other * (u.Z * r));
                }

                foreach (int i in tris)
                {
                    mesh.Triangles.Add(i + offset);
                }
            }

            Finish(mesh);
            return mesh;
        }

        // Smooth normals from the faces, box UVs (1 m per tile).
        private static void Finish(MeshData mesh)
        {
            var normals = new Vec3[mesh.Vertices.Count];
            for (int i = 0; i < mesh.Triangles.Count; i += 3)
            {
                Vec3 a = mesh.Vertices[mesh.Triangles[i]], b = mesh.Vertices[mesh.Triangles[i + 1]], c = mesh.Vertices[mesh.Triangles[i + 2]];
                Vec3 n = Vec3.Cross(b - a, c - a);
                for (int k = 0; k < 3; k++)
                {
                    normals[mesh.Triangles[i + k]] += n;
                }
            }

            foreach (Vec3 n in normals)
            {
                mesh.Normals.Add(n.Normalized);
            }

            foreach (Vec3 v in mesh.Vertices)
            {
                mesh.Uvs.Add(new Vec2(v.X + v.Z * 0.7f, v.Y + v.Z * 0.3f));
            }

            mesh.SkirtTriangleStart = mesh.Triangles.Count;
        }

        // Unit icosphere; triangles wound so Cross(b - a, c - a) points outwards.
        private static void Icosphere(int subdivisions, out List<Vec3> vertices, out List<int> triangles)
        {
            float t = (1f + (float)Math.Sqrt(5)) / 2f;
            vertices = new List<Vec3>
            {
                new Vec3(-1, t, 0), new Vec3(1, t, 0), new Vec3(-1, -t, 0), new Vec3(1, -t, 0),
                new Vec3(0, -1, t), new Vec3(0, 1, t), new Vec3(0, -1, -t), new Vec3(0, 1, -t),
                new Vec3(t, 0, -1), new Vec3(t, 0, 1), new Vec3(-t, 0, -1), new Vec3(-t, 0, 1),
            };
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = vertices[i].Normalized;
            }

            triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var midpoints = new Dictionary<long, int>();
                var next = new List<int>(triangles.Count * 4);
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Midpoint(vertices, midpoints, a, b), bc = Midpoint(vertices, midpoints, b, c), ca = Midpoint(vertices, midpoints, c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                triangles = next;
            }
        }

        private static int Midpoint(List<Vec3> vertices, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;
            if (!cache.TryGetValue(key, out int index))
            {
                index = vertices.Count;
                vertices.Add(((vertices[a] + vertices[b]) * 0.5f).Normalized);
                cache[key] = index;
            }

            return index;
        }
    }
}
