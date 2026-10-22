using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Meshing;
using Subterra.Core.Numerics;
using Subterra.Core.Scatter;
using Xunit;

namespace Subterra.Core.Tests.Scatter
{
    public class PropGeneratorTests
    {
        public static IEnumerable<object[]> AllProps() => PropGenerator.Names.Where(n => n != "GlowPatch01").SelectMany(n => new[] { new object[] { n, 0 }, new object[] { n, 1 } });

        [Theory, MemberData(nameof(AllProps))]
        public void ClosedOutwardAndSmall(string name, int lod)
        {
            MeshData m = PropGenerator.Build("Subterra/Props/" + name, lod);
            Assert.NotNull(m);
            Assert.True(m.Vertices.Count < 65535);
            var uses = new Dictionary<(int, int), int>();
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = m.Triangles[i + k], b = m.Triangles[i + (k + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    uses.TryGetValue(key, out int n);
                    uses[key] = n + 1;
                }
            }

            Assert.All(uses.Values, n => Assert.Equal(2, n));

            // Outward: the face normal points away from the centroid of its own closed part (connected piece) for most faces.
            Vec3[] centre = PartCentres(m);
            int outward = 0, total = 0;
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                total++;
                if (Vec3.Dot(Vec3.Cross(b - a, c - a), (a + b + c) / 3f - centre[m.Triangles[i]]) > 0)
                {
                    outward++;
                }
            }

            // Curved pieces (the Kraken's ribs) face their own centroid on the inside of the bend: for those the signed
            // volume of each piece says which way they face.
            if (name.StartsWith("Kraken"))
            {
                Assert.All(PartVolumes(m), v => Assert.True(v > 0, $"piece volume {v}"));
                return;
            }

            Assert.True(outward >= 0.9 * total || name == "IceCluster01" && outward >= 0.7 * total, $"{outward}/{total} outward");
        }

        // Signed volume of each connected piece (positive when its faces point outwards).
        private static IEnumerable<float> PartVolumes(MeshData m)
        {
            Vec3[] centre = PartCentres(m);
            var volumes = new Dictionary<Vec3, float>();
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 o = centre[m.Triangles[i]];
                Vec3 a = m.Vertices[m.Triangles[i]] - o, b = m.Vertices[m.Triangles[i + 1]] - o, c = m.Vertices[m.Triangles[i + 2]] - o;
                volumes.TryGetValue(o, out float v);
                volumes[o] = v + Vec3.Dot(a, Vec3.Cross(b, c)) / 6;
            }

            return volumes.Values;
        }

        // Per vertex: the centroid of the connected piece it belongs to.
        private static Vec3[] PartCentres(MeshData m)
        {
            var parent = Enumerable.Range(0, m.Vertices.Count).ToArray();
            int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                parent[Find(m.Triangles[i + 1])] = Find(m.Triangles[i]);
                parent[Find(m.Triangles[i + 2])] = Find(m.Triangles[i]);
            }

            var sums = new Dictionary<int, (Vec3 sum, int n)>();
            for (int v = 0; v < m.Vertices.Count; v++)
            {
                sums.TryGetValue(Find(v), out var s);
                sums[Find(v)] = (s.sum + m.Vertices[v], s.n + 1);
            }

            return Enumerable.Range(0, m.Vertices.Count).Select(v => sums[Find(v)].sum / sums[Find(v)].n).ToArray();
        }

        [Fact]
        public void GlowPatchFacesDownUnderTheCeiling()
        {
            MeshData m = PropGenerator.Build("GlowPatch01", 0);
            Assert.All(m.Vertices, v => Assert.True(v.Y < 0));
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                Assert.True(Vec3.Cross(b - a, c - a).Y < 0);
            }
        }

        [Fact]
        public void Lod1HasAboutAQuarter()
        {
            int lod0 = PropGenerator.Build("Boulder01", 0).Triangles.Count, lod1 = PropGenerator.Build("Boulder01", 1).Triangles.Count;
            Assert.Equal(lod0 / 4, lod1);
        }

        [Fact] public void Deterministic() => Assert.Equal(PropGenerator.Build("Boulder02", 0).Vertices, PropGenerator.Build("Boulder02", 0).Vertices);

        [Fact] public void UnknownIsNull() => Assert.Null(PropGenerator.Build("Nope", 0));
    }
}
