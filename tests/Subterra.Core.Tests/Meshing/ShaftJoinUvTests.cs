using System;
using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;
using Xunit;
using Xunit.Abstractions;

namespace Subterra.Core.Tests.Meshing
{
    // Lucas: fans of rotated patches round a shaft blasted from the floor of a pocket (the rounded join slopes through 45°).
    public class ShaftJoinUvTests
    {
        private readonly ITestOutputHelper output;

        public ShaftJoinUvTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void AShaftUnderAPocketHasFewSeams()
        {
            double r = 600000;
            var up = new Vec3d(0, 1, 0);
            // A pocket 9 m wide, 7 m tall; a shaft 3 m in radius from its floor down, off its centre.
            var pocket = new Shaft(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 9, 7) { Roughness = 0.4f, NoiseScale = 2 };
            var shaft = new Shaft(new Vec3(3, -60, 1), new Vec3(0, 1, 0), 3, 62) { Roughness = 0.4f, NoiseScale = 2 };
            var list = new List<Primitive> { pocket, shaft };
            var sdf = new NetworkSdf(list, new SpatialGrid(list), Hash.Of("join"));
            var net = new TubeNetwork("join", "Test", null, "join", up * (r - 300), list, sdf, new Skylight[0], new[] { up }, new[] { r });
            MeshData mesh = SurfaceNets.Build(sdf, new Vec3(-12, -14, -12), 1f, false);
            Assert.False(mesh.IsEmpty);
            MeshData p = TubeUvProjector.Project(mesh, net, 1f);
            // Seam vertices: a position used by several output vertices with different UVs.
            var byPosition = new Dictionary<(int, int, int), HashSet<(int, int)>>();
            for (int i = 0; i < p.Vertices.Count; i++)
            {
                Vec3 v = p.Vertices[i];
                var key = ((int)Math.Round(v.X * 1000), (int)Math.Round(v.Y * 1000), (int)Math.Round(v.Z * 1000));
                if (!byPosition.TryGetValue(key, out var uvs))
                {
                    byPosition[key] = uvs = new HashSet<(int, int)>();
                }

                uvs.Add(((int)Math.Round(p.Uvs[i].X * 1000), (int)Math.Round(p.Uvs[i].Y * 1000)));
            }

            // Box-mapped triangles on sloping ground (25-65°) by the shaft: the flicker zone.
            int flicker = 0, sloped = 0;
            for (int i = 0; i < p.Triangles.Count; i += 3)
            {
                Vec3 a = p.Vertices[p.Triangles[i]], b = p.Vertices[p.Triangles[i + 1]], c = p.Vertices[p.Triangles[i + 2]];
                Vec3 n = Vec3.Cross(b - a, c - a);
                if (n.Length < 1e-5f) continue;
                float cos = Math.Abs(n.Normalized.Y);
                float rho = (float)Math.Sqrt((a.X - 3) * (a.X - 3) + (a.Z - 1) * (a.Z - 1));
                if (cos < 0.42f || cos > 0.9f || rho > 7) continue;
                sloped++;
                Vec2 ua = p.Uvs[p.Triangles[i]];
                bool box = Math.Abs(ua.X - a.X) < 1e-3f && Math.Abs(ua.Y - a.Z) < 1e-3f || Math.Abs(ua.X - a.Z) < 1e-3f && Math.Abs(ua.Y - a.Y) < 1e-3f || Math.Abs(ua.X - a.X) < 1e-3f && Math.Abs(ua.Y - a.Y) < 1e-3f
                    || Math.Abs(ua.X + a.Z) < 1e-3f || Math.Abs(ua.X - a.Z) < 1e-3f && Math.Abs(ua.Y + a.X) < 1e-3f;
                if (box) flicker++;
            }

            output.WriteLine($"{flicker} of {sloped} sloped triangles by the shaft box-mapped");
            int seams = byPosition.Values.Count(s => s.Count > 1);
            double share = (double)seams / byPosition.Count;
            output.WriteLine($"{seams} seam vertices of {byPosition.Count} ({share:P1})");
            Assert.True(sloped > 50, $"{sloped} sloped triangles");
            Assert.True(flicker <= sloped / 20, $"{flicker} of {sloped} sloped triangles box-mapped (16 before)");
            Assert.True(share < 0.21, $"{share:P1} of the vertices on seams (21.5% before)");
        }
    }
}
