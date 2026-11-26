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
    // Lucas: a patchwork of rotated rock on a dug tunnel's floor. Box UVs follow the network's level frame, not the body's axes.
    public class LevelBoxUvTests
    {
        [Fact]
        public void ABumpyFloorTiltedToTheBodysAxesKeepsOneProjection()
        {
            // A network where "up" is 35° off the body's Y axis; a chamber (box UVs) meshed round its floor.
            double tilt = 35 * Math.PI / 180, r = 600000;
            var up = new Vec3d(Math.Sin(tilt), Math.Cos(tilt), 0);
            var chamber = new Chamber(new Vec3(0, 0, 0), up.ToVec3(), 40, 12) { Roughness = 1.5f, NoiseScale = 6 };
            var list = new List<Primitive> { chamber };
            var sdf = new NetworkSdf(list, new SpatialGrid(list), Hash.Of("tilted"));
            var net = new TubeNetwork("tilted", "Test", null, "tilted", up * (r - 200), list, sdf, new Skylight[0], new[] { up }, new[] { r });
            // A block round the middle of the floor.
            Vec3 floor = up.ToVec3() * 0.5f;
            MeshData mesh = SurfaceNets.Build(sdf, floor - new Vec3(8, 8, 8), 1f, false);
            Assert.False(mesh.IsEmpty);
            MeshData p = TubeUvProjector.Project(mesh, net, 1f);
            // Floor triangles (facing up within 40°): their UVs are the level projection, so neighbours share their UVs, and
            // every floor triangle's UVs equal its corners' east/north coordinates.
            Vec3 u = up.ToVec3(), east = Vec3.Cross(new Vec3(0, 1, 0), u).Normalized, north = Vec3.Cross(u, east);
            int floorTris = 0, off = 0;
            for (int i = 0; i < p.Triangles.Count; i += 3)
            {
                Vec3 a = p.Vertices[p.Triangles[i]], b = p.Vertices[p.Triangles[i + 1]], c = p.Vertices[p.Triangles[i + 2]];
                Vec3 n = Vec3.Cross(b - a, c - a);
                if (n.Length < 1e-4f || Vec3.Dot(n.Normalized, u) < Math.Cos(40 * Math.PI / 180))
                {
                    continue;
                }

                floorTris++;
                Vec2 uv = p.Uvs[p.Triangles[i]];
                if (Math.Abs(uv.X - Vec3.Dot(a, east)) > 1e-3f || Math.Abs(uv.Y - Vec3.Dot(a, north)) > 1e-3f)
                {
                    off++;
                }
            }

            Assert.True(floorTris > 50, $"{floorTris} floor triangles");
            Assert.Equal(0, off);
        }
    }
}
