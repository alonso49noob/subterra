using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Procedural;
using Xunit;

namespace Subterra.Core.Tests.Procedural
{
    public class CellRequestQueueTests
    {
        private static readonly CellGrid Mun = new CellGrid(200000);
        private static readonly CellGrid Kerbin = new CellGrid(600000);

        private static CellId MunCell(double lat, double lon) => Mun.CellOf(BodyFrame.Direction(lat, lon));

        private static CellId KerbinCell(double lat, double lon) => Kerbin.CellOf(BodyFrame.Direction(lat, lon));

        [Fact]
        public void EmptyQueueGivesNothing()
        {
            var q = new CellRequestQueue<int>();
            Assert.False(q.TryTake("Mun", Vec3d.Zero, out _, out _, out _));
        }

        [Fact]
        public void DuplicatesAreKeptOnce()
        {
            var q = new CellRequestQueue<int>();
            Assert.True(q.Add("Mun", MunCell(0, 0), Mun, 1));
            Assert.False(q.Add("Mun", MunCell(0, 0), Mun, 2));
            Assert.True(q.Add("Kerbin", KerbinCell(0, 0), Kerbin, 3));
            Assert.Equal(2, q.Count);
            Assert.True(q.Contains("Mun", MunCell(0, 0)));
        }

        [Fact]
        public void ActiveBodyComesFirstNearestFirst()
        {
            var q = new CellRequestQueue<string>();
            q.Add("Kerbin", KerbinCell(0, 0), Kerbin, "kerbin");
            q.Add("Mun", MunCell(0, 120), Mun, "far");
            q.Add("Mun", MunCell(0, 40), Mun, "middle");
            q.Add("Mun", MunCell(0, 10), Mun, "near");
            Vec3d vessel = BodyFrame.Direction(0, 5);

            string[] order = new string[4];
            for (int i = 0; i < 4; i++)
            {
                Assert.True(q.TryTake("Mun", vessel, out _, out _, out order[i]));
            }

            Assert.Equal(new[] { "near", "middle", "far", "kerbin" }, order);
            Assert.Equal(0, q.Count);
            Assert.False(q.Contains("Mun", MunCell(0, 10)));
        }

        [Fact]
        public void OtherBodiesAreServedInTheOrderAsked()
        {
            var q = new CellRequestQueue<string>();
            q.Add("Kerbin", KerbinCell(0, 90), Kerbin, "first");
            q.Add("Minmus", new CellId(0, 0, 0), new CellGrid(60000), "second");
            q.Add("Kerbin", KerbinCell(0, 0), Kerbin, "third");

            q.TryTake("Mun", BodyFrame.Direction(0, 0), out string body, out _, out string a);
            q.TryTake("Mun", BodyFrame.Direction(0, 0), out _, out _, out string b);
            q.TryTake(null, Vec3d.Zero, out _, out _, out string c);
            Assert.Equal("Kerbin", body);
            Assert.Equal(new[] { "first", "second", "third" }, new[] { a, b, c });
        }

        [Fact]
        public void ATakenCellCanBeAskedForAgain()
        {
            var q = new CellRequestQueue<int>();
            q.Add("Mun", MunCell(0, 0), Mun, 1);
            q.TryTake("Mun", Vec3d.Zero, out _, out _, out _);
            Assert.True(q.Add("Mun", MunCell(0, 0), Mun, 2));
        }
    }
}
