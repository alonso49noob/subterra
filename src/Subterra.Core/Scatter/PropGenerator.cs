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
        public static readonly string[] Names = { "Boulder01", "Boulder02", "Boulder03", "RubbleCone01", "Lavacicle01", "PahoehoeSlab01", "IceCluster01", "GeodeCrystal01", "GlowPatch01", "GlowMat01", "Fungus01", "KrakenRibs01", "KrakenBone01", "Stalagmite01", "DustDrift01", "IceColumn01" };

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
                case "Stalagmite01": return Shape(subdivisions, seed, p => Stalagmite(p), 0.06f);
                case "DustDrift01": return Shape(subdivisions, seed, p => new Vec3(p.X * 2.2f, (p.Y + 1) * 0.18f, p.Z * 1.1f), 0.03f, baseAtZero: false);
                case "IceColumn01": return Shape(subdivisions, seed, p => new Vec3(p.X * 0.4f * (1.2f - 0.4f * Math.Abs(p.Y)), (p.Y + 1) * 3.2f, p.Z * 0.4f * (1.2f - 0.4f * Math.Abs(p.Y))), 0.1f);
                case "IceCluster01": return Crystals(lod == 0 ? 2 : 1, seed);
                case "GeodeCrystal01": return Crystals(lod == 0 ? 2 : 1, seed, 7, 1.5f, 2.2f);
                case "GlowPatch01": return Patch(lod == 0 ? 24 : 12, seed, -1);
                case "GlowMat01": return Patch(lod == 0 ? 24 : 12, seed, 1);
                case "Fungus01": return Fungi(lod == 0 ? 2 : 1, seed);
                case "KrakenRibs01": return Ribcage(lod == 0 ? 2 : 1, seed);
                case "KrakenBone01": return Bone(lod == 0 ? 2 : 1, seed);
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

        private static Vec3 Stalagmite(Vec3 p)
        {
            // A stalagmite: wide foot, tapering up to a rounded tip 2.4 m up (the icicle's shape the other way up).
            float t = Math.Max(0f, Math.Min(1f, (p.Y + 1) * 0.5f));
            float r = 0.45f * (0.08f + 0.92f * (float)Math.Pow(1 - t, 0.8));
            return new Vec3(p.X * r, 2.4f * t, p.Z * r);
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
        // A cluster of `count` pointed prisms leaning out from the base; `length` and `thickness` scale them.
        private static MeshData Crystals(int subdivisions, ulong seed, int count = 5, float length = 1f, float thickness = 1f)
        {
            var rng = new Rng(seed);
            var mesh = new MeshData();
            for (int c = 0; c < count; c++)
            {
                double angle = rng.Range(0, 2 * Math.PI), lean = rng.Range(0.1, 0.5);
                float height = (float)rng.Range(0.8, 1.8) * length, width = (float)rng.Range(0.12, 0.25) * thickness;
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

        // A soft patch of light: a disc of radius 1 (at scale 1) just off the rock, facing away from it (`side` -1: hanging
        // under a ceiling, the prop's up into the rock; +1: lying on a floor or a wall), its edge wavy; UVs centred at
        // (0.5, 0.5) so a radial texture fades it out towards the edge.
        private static MeshData Patch(int segments, ulong seed, int side)
        {
            var rng = new Rng(seed);
            var mesh = new MeshData();
            mesh.Vertices.Add(new Vec3(0, side * 0.12f, 0));
            mesh.Uvs.Add(new Vec2(0.5f, 0.5f));
            double phase = rng.Range(0, 2 * Math.PI);
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                float r = (float)(1 + 0.18 * Math.Sin(3 * a + phase) + 0.1 * Math.Sin(5 * a + 2 * phase));
                mesh.Vertices.Add(new Vec3((float)Math.Cos(a) * r, side * 0.04f, (float)Math.Sin(a) * r));
                mesh.Uvs.Add(new Vec2(0.5f + 0.5f * (float)Math.Cos(a), 0.5f + 0.5f * (float)Math.Sin(a)));
            }

            for (int i = 0; i < segments; i++)
            {
                // Wound to face away from the rock, into the cave.
                mesh.Triangles.AddRange(side < 0 ? new[] { 0, 1 + i, 1 + (i + 1) % segments } : new[] { 0, 1 + (i + 1) % segments, 1 + i });
            }

            foreach (Vec3 _ in mesh.Vertices)
            {
                mesh.Normals.Add(new Vec3(0, side, 0));
            }

            mesh.SkirtTriangleStart = mesh.Triangles.Count;
            return mesh;
        }

        // A clump of mushrooms about 1 m tall: thin stems, flat caps, of several sizes leaning apart.
        private static MeshData Fungi(int subdivisions, ulong seed)
        {
            var rng = new Rng(seed);
            var mesh = new MeshData();
            int count = 3 + (int)(rng.NextDouble() * 4);
            for (int c = 0; c < count; c++)
            {
                double angle = rng.Range(0, 2 * Math.PI), off = c == 0 ? 0 : rng.Range(0.15, 0.45);
                float size = c == 0 ? 1f : (float)rng.Range(0.35, 0.8);
                var foot = new Vec3((float)(Math.Cos(angle) * off), 0, (float)(Math.Sin(angle) * off));
                var axis = new Vec3(foot.X * 0.5f, 1, foot.Z * 0.5f).Normalized;
                float height = size * (float)rng.Range(0.8, 1.1), capRadius = size * (float)rng.Range(0.28, 0.4);
                Part(mesh, subdivisions, u => foot + axis * ((u.Y + 1) * 0.5f * height) + new Vec3(u.X, 0, u.Z) * (0.05f * size));
                Vec3 top = foot + axis * height;
                Part(mesh, subdivisions, u => top + new Vec3(u.X * capRadius, Math.Max(u.Y, -0.3f) * capRadius * 0.45f, u.Z * capRadius));
            }

            Finish(mesh);
            return mesh;
        }

        // Half-buried bones of something huge (Bop's Kraken): five ribs arching over a 1-unit-high span, getting smaller
        // towards the tail, joined by a spine along their tops. 4 units long, ribs 1.4 units across at the widest.
        private static MeshData Ribcage(int subdivisions, ulong seed)
        {
            var rng = new Rng(seed);
            var mesh = new MeshData();
            for (int k = 0; k < 5; k++)
            {
                float x = -1.6f + 0.8f * k, size = 1f - 0.12f * k, lean = (float)rng.Range(-0.08, 0.08);
                Arc(mesh, subdivisions, x, 0.7f * size, 1f * size, 0.07f * size, lean);
            }

            // The spine: along the tops of the ribs, sloping down with them, knobbly (vertebrae) by its noise.
            int[][] noise = Noise.Tables(seed);
            Part(mesh, subdivisions, u =>
            {
                float bump = 1 + 0.35f * Noise.Fbm(new Vec3(u.Y * 6, 0, 0), noise, 2);
                float x = -u.Y * 2.1f;
                return new Vec3(x, 0.66f - 0.15f * x + u.X * 0.09f * bump, u.Z * 0.09f * bump);
            });
            Finish(mesh);
            return mesh;
        }

        // One loose bone: long, bent a little, with knobbed ends (a femur, a jaw), lying on the floor.
        private static MeshData Bone(int subdivisions, ulong seed)
        {
            var rng = new Rng(seed);
            float bend = (float)rng.Range(0.1, 0.3);
            var mesh = new MeshData();
            Part(mesh, subdivisions + 1, u =>
            {
                float knob = 1 + 1.4f * (float)Math.Pow(Math.Abs(u.Y), 8);
                float y = u.Y * 1.5f;
                return new Vec3(u.X * 0.12f * knob, 0.12f - u.Z * 0.1f * knob + bend * (1 - u.Y * u.Y), y);
            });
            Finish(mesh);
            return mesh;
        }

        // A rib: a tapering tube along a half ellipse in the plane x = `x` (half-width `w`, height `h`), its ends in the ground.
        private static void Arc(MeshData mesh, int subdivisions, float x, float w, float h, float r, float lean)
        {
            Part(mesh, subdivisions + 1, u =>
            {
                double a = Math.PI * (0.02 + 0.96 * (u.Y + 1) * 0.5);
                var centre = new Vec3(x + lean * (float)Math.Sin(a), h * (float)Math.Sin(a) - 0.1f, w * (float)Math.Cos(a));
                var radial = new Vec3(0, (float)Math.Sin(a) * w, (float)Math.Cos(a) * h).Normalized;
                float taper = r * (0.6f + 0.4f * (float)Math.Sin(a));
                return centre + radial * (u.X * taper) - new Vec3(1, 0, 0) * (u.Z * taper);
            });
        }

        // Appends a unit icosphere moved through `shape`.
        private static void Part(MeshData mesh, int subdivisions, Func<Vec3, Vec3> shape)
        {
            Icosphere(subdivisions, out List<Vec3> unit, out List<int> tris);
            int offset = mesh.Vertices.Count;
            foreach (Vec3 u in unit)
            {
                mesh.Vertices.Add(shape(u));
            }

            foreach (int i in tris)
            {
                mesh.Triangles.Add(i + offset);
            }
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
