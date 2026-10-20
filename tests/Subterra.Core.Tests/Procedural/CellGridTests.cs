using System.Linq;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Procedural;
using Xunit;

namespace Subterra.Core.Tests.Procedural
{
    public class CellGridTests
    {
        private static readonly CellGrid Grid = new CellGrid(200000);

        [Fact] public void MunHasSevenPerFace() => Assert.Equal(7, Grid.N);

        [Fact]
        public void CenterMapsBackToItsCell()
        {
            Assert.Equal(294, Grid.All().Count());
            foreach (CellId c in Grid.All())
            {
                Assert.Equal(c, Grid.CellOf(Grid.Center(c)));
            }
        }

        [Fact]
        public void NearContainsOwnCell()
        {
            foreach (CellId c in Grid.All())
            {
                Assert.Contains(c, Grid.Near(Grid.Center(c), 1000));
            }
        }

        [Fact]
        public void NearContainsEdgeNeighbours()
        {
            // A neighbour across each edge: step just past the edge in face coordinates.
            var rng = new Rng(5);
            for (int k = 0; k < 500; k++)
            {
                Vec3d dir = BodyFrame.Direction(rng.Range(-89, 89), rng.Range(-180, 180));
                CellId c = Grid.CellOf(dir);
                var near = Grid.Near(dir, 60000).ToList();
                foreach (Vec3d offset in new[] { new Vec3d(0.12, 0, 0), new Vec3d(-0.12, 0, 0), new Vec3d(0, 0.12, 0), new Vec3d(0, -0.12, 0), new Vec3d(0, 0, 0.12), new Vec3d(0, 0, -0.12) })
                {
                    Vec3d other = (dir + offset * 0.1).Normalized;
                    if (BodyFrame.SurfaceDistance(dir, other, 200000) < 40000)
                    {
                        Assert.Contains(Grid.CellOf(other), near);
                    }
                }

                Assert.Contains(c, near);
            }
        }

        [Fact]
        public void BoundsContainCenter()
        {
            foreach (CellId c in Grid.All())
            {
                var (latMin, latMax, lonMin, lonMax) = Grid.Bounds(c);
                BodyFrame.ToLatLon(Grid.Center(c), out double lat, out double lon);
                Assert.InRange(lat, latMin, latMax);
                Assert.True(lon >= lonMin && lon <= lonMax || lon + 360 >= lonMin && lon + 360 <= lonMax, $"{c}: lon {lon} not in [{lonMin}, {lonMax}]");
            }
        }

        [Fact]
        public void AntimeridianCellsUseLonAbove180()
        {
            Assert.Contains(Grid.All(), c => Grid.Bounds(c).lonMax > 180);
        }

        [Fact]
        public void SampleGridHas4096PointsInsideBounds()
        {
            CellId c = Grid.CellOf(BodyFrame.Direction(5, 20));
            var (latMin, latMax, lonMin, lonMax) = Grid.Bounds(c);
            var points = Grid.SampleGrid(c);
            Assert.Equal(4096, points.Count);
            Assert.All(points, p => { Assert.InRange(p.lat, latMin, latMax); Assert.InRange(p.lon, lonMin, lonMax); });
        }
    }
}
