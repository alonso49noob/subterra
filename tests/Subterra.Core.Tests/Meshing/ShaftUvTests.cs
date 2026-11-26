using System;
using System.Collections.Generic;
using Subterra.Core.Meshing;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Sdf;
using Subterra.Core.Tubes;
using Xunit;

namespace Subterra.Core.Tests.Meshing
{
    public class ShaftUvTests
    {
        private const double R = 200000;

        // A shaft 12 m in radius, its wall meshed in a 32 m block halfway up (no floor in the block).
        private static (MeshData Mesh, TubeNetwork Net) ShaftWall()
        {
            var shaft = new Shaft(new Vec3(0, 0, 0), new Vec3(0, 1, 0), 12, 200) { Roughness = 0 };
            var list = new List<Primitive> { shaft };
            var sdf = new NetworkSdf(list, new SpatialGrid(list), Hash.Of("shaft"));
            var net = new TubeNetwork("shaft", "Test", null, "shaft", new Vec3d(0, R - 300, 0), list, sdf, new Skylight[0],
                new[] { new Vec3d(0, 1, 0) }, new[] { R });
            MeshData mesh = SurfaceNets.Build(sdf, new Vec3(-16, 80, -16), 1f, false);
            return (mesh, net);
        }

        [Fact]
        public void AShaftWallIsNotStretched()
        {
            // Wrapped round the wall, the texture keeps its scale everywhere: UV area over world area is the same on every
            // wall triangle (box UVs squeezed it towards 0.5 where the wall faced 45° between axes).
            var (mesh, net) = ShaftWall();
            Assert.False(mesh.IsEmpty);
            MeshData p = TubeUvProjector.Project(mesh, net, 1f);
            int wall = 0, stretched = 0;
            for (int i = 0; i < p.Triangles.Count; i += 3)
            {
                Vec3 a = p.Vertices[p.Triangles[i]], b = p.Vertices[p.Triangles[i + 1]], c = p.Vertices[p.Triangles[i + 2]];
                Vec2 ua = p.Uvs[p.Triangles[i]], ub = p.Uvs[p.Triangles[i + 1]], uc = p.Uvs[p.Triangles[i + 2]];
                float area = Vec3.Cross(b - a, c - a).Length * 0.5f;
                float uvArea = Math.Abs((ub.X - ua.X) * (uc.Y - ua.Y) - (uc.X - ua.X) * (ub.Y - ua.Y)) * 0.5f;
                if (area < 0.05f)
                {
                    continue;
                }

                wall++;
                if (Math.Abs(uvArea / area - 1) > 0.25f && Math.Abs(ua.X) < 30)
                {
                    stretched++;
                }
            }

            Assert.True(wall > 100, $"{wall} wall triangles");
            // Only the triangles on the seam (box UVs there) may differ.
            Assert.True(stretched < wall / 20, $"{stretched} of {wall} triangles stretched");
        }

        [Fact]
        public void TheTextureRunsUpTheShaft()
        {
            // Along the wall the second coordinate is the height: vertices 10 m apart vertically are 10 UV units apart.
            var (mesh, net) = ShaftWall();
            MeshData p = TubeUvProjector.Project(mesh, net, 1f);
            for (int i = 0; i < p.Vertices.Count; i++)
            {
                Vec3 v = p.Vertices[i];
                if (Math.Abs(Math.Sqrt(v.X * v.X + v.Z * v.Z) - 12) < 1 && Math.Abs(v.X) > 3)
                {
                    // On the wall away from the seam: V is the height (box UVs would give Z or Y depending on the facing).
                    Assert.InRange(p.Uvs[i].Y, v.Y - 0.01f, v.Y + 0.01f);
                }
            }
        }
    }
}
