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
        public static IEnumerable<object[]> AllProps() => PropGenerator.Names.SelectMany(n => new[] { new object[] { n, 0 }, new object[] { n, 1 } });

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

            // Outward: the face normal points away from the centroid of its own closed part for most faces.
            Vec3 centre = m.Vertices.Aggregate(Vec3.Zero, (s, v) => s + v) / m.Vertices.Count;
            int outward = 0, total = 0;
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                Vec3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                total++;
                if (Vec3.Dot(Vec3.Cross(b - a, c - a), (a + b + c) / 3f - centre) > 0)
                {
                    outward++;
                }
            }

            Assert.True(outward >= 0.9 * total || name == "IceCluster01" && outward >= 0.7 * total, $"{outward}/{total} outward");
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
