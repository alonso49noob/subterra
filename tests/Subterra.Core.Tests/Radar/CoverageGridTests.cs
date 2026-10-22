using System;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class CoverageGridTests
    {
        private const double MunRadius = 200000;

        private static bool Marked(CoverageGrid grid, double lat, double lon) => grid.BestAltitude(lat, lon).HasValue;

        [Fact]
        public void EncodeDecodeRoundTrip()
        {
            double back = CoverageGrid.Decode(CoverageGrid.Encode(30000));
            Assert.InRange(back, 30000 * 0.97, 30000 * 1.03);
            Assert.NotEqual(0, CoverageGrid.Encode(30000));
            Assert.Equal(255, CoverageGrid.Encode(1000));
            Assert.Equal(1, CoverageGrid.Encode(1e12));
            Assert.Equal(255, CoverageGrid.Encode(10));
        }

        [Fact]
        public void LowerAltitudeWins()
        {
            var a = new CoverageGrid();
            a.MarkSwath(10, 20, 5000, MunRadius, 50000);
            a.MarkSwath(10, 20, 5000, MunRadius, 30000);
            Assert.InRange(a.BestAltitude(10, 20).Value, 30000 * 0.97, 30000 * 1.03);

            var b = new CoverageGrid();
            b.MarkSwath(10, 20, 5000, MunRadius, 30000);
            b.MarkSwath(10, 20, 5000, MunRadius, 50000);
            Assert.InRange(b.BestAltitude(10, 20).Value, 30000 * 0.97, 30000 * 1.03);
        }

        [Fact]
        public void UnscannedCellIsNull()
        {
            Assert.Null(new CoverageGrid().BestAltitude(0, 0));
        }

        [Fact]
        public void SwathWidth()
        {
            // 2000 m on a 200 km radius is 0.573 degrees: the cell centred at 0.375 is inside, the one at 0.625 is not.
            var grid = new CoverageGrid();
            grid.MarkSwath(0, 0, 2000, MunRadius, 30000);

            Assert.True(Marked(grid, 0.125, 0.375));
            Assert.False(Marked(grid, 0.125, 0.625));
            Assert.True(Marked(grid, 0.125, -0.375));
            Assert.False(Marked(grid, 0.125, -0.625));
            Assert.True(Marked(grid, 0.375, 0.125));
            Assert.False(Marked(grid, 0.625, 0.125));
        }

        [Fact]
        public void NarrowSwathStillMarksTheCellUnderIt()
        {
            var grid = new CoverageGrid();
            grid.MarkSwath(12.3, 45.6, 1, MunRadius, 30000);
            Assert.True(Marked(grid, 12.3, 45.6));
            Assert.Equal(1, CountMarked(grid));
        }

        [Fact]
        public void LongitudesOutsideRangeAreWrapped()
        {
            var grid = new CoverageGrid();
            grid.MarkSwath(0, 190, 1000, MunRadius, 30000); // same as -170
            Assert.True(Marked(grid, 0, -170));
            Assert.True(Marked(grid, 0, 190));
            Assert.False(Marked(grid, 0, 10));

            var other = new CoverageGrid();
            other.MarkSwath(0, -540, 1000, MunRadius, 30000); // same as 180 = -180
            Assert.True(Marked(other, 0, -179.9));
            Assert.True(Marked(other, 0, 179.9));
        }

        [Fact]
        public void LongArcHasNoGaps()
        {
            var grid = new CoverageGrid();
            grid.MarkTrack(0, 0, 0, 90, 2000, MunRadius, 30000);

            // The two latitude bands next to the equator, every column between the two ends.
            for (int column = 720; column < 720 + 360; column++)
            {
                double lon = CoverageGrid.CellCenterLongitude(column);
                Assert.True(Marked(grid, 0.125, lon), "gap at " + lon);
                Assert.True(Marked(grid, -0.125, lon), "gap at " + lon);
            }
        }

        [Fact]
        public void LongArcWithNarrowSwathHasNoGaps()
        {
            // Narrower than a cell (872 m): the step is capped by the cell size and the track still leaves a line.
            var grid = new CoverageGrid();
            grid.MarkTrack(0, 0, 0, 30, 100, MunRadius, 30000);
            for (int column = 720; column < 720 + 120; column++)
            {
                double lon = CoverageGrid.CellCenterLongitude(column);
                Assert.True(Marked(grid, 0.125, lon) || Marked(grid, -0.125, lon), "gap at " + lon);
            }
        }

        [Fact]
        public void WrapsAtAntimeridian()
        {
            var grid = new CoverageGrid();
            grid.MarkTrack(0, 179, 0, -179, 2000, MunRadius, 30000);

            Assert.True(Marked(grid, 0.125, 179.9));
            Assert.True(Marked(grid, 0.125, -179.9));
            Assert.True(Marked(grid, 0.125, 180 - 0.125));
            Assert.False(Marked(grid, 0.125, 0));
            Assert.False(Marked(grid, 0.125, 90));
            Assert.False(Marked(grid, 0.125, -90));
        }

        [Fact]
        public void SwathAcrossAntimeridianMarksBothSides()
        {
            var grid = new CoverageGrid();
            grid.MarkSwath(0, 180, 2000, MunRadius, 30000);
            Assert.True(Marked(grid, 0.125, 179.875));
            Assert.True(Marked(grid, 0.125, -179.875));
            Assert.True(Marked(grid, 0.125, 179.625));
            Assert.True(Marked(grid, 0.125, -179.625));
            Assert.False(Marked(grid, 0.125, 179.0));
            Assert.False(Marked(grid, 0.125, -179.0));
        }

        [Fact]
        public void PolarPassMarksCap()
        {
            var grid = new CoverageGrid();
            grid.MarkTrack(80, 10, 90, 10, 2000, MunRadius, 30000);
            grid.MarkTrack(90, 10, 80, 190, 2000, MunRadius, 30000);

            // Every cell above 89.75 degrees (the top row), at any longitude.
            for (int column = 0; column < CoverageGrid.Width; column++)
            {
                Assert.True(grid.GetCell(CoverageGrid.Height - 1, column) != 0, "hole at column " + column);
            }
        }

        [Fact]
        public void SwathOverThePoleMarksTheWholeCap()
        {
            var grid = new CoverageGrid();
            grid.MarkSwath(90, 0, 2000, MunRadius, 30000);
            // 0.573 degrees: the top two rows (centres 89.875 and 89.625) are within reach everywhere.
            for (int column = 0; column < CoverageGrid.Width; column++)
            {
                Assert.True(grid.GetCell(CoverageGrid.Height - 1, column) != 0);
                Assert.True(grid.GetCell(CoverageGrid.Height - 2, column) != 0);
            }

            Assert.Equal(0, grid.GetCell(CoverageGrid.Height - 3, 0));
        }

        [Fact]
        public void SwathNearThePoleWidensInLongitude()
        {
            // A point 0.2 degrees from the pole with a 1 degree swath reaches the far side of the pole.
            var grid = new CoverageGrid();
            grid.MarkSwath(89.8, 0, 1.0 * Math.PI / 180 * MunRadius, MunRadius, 30000);
            Assert.True(Marked(grid, 89.875, 180));
            Assert.True(Marked(grid, 89.375, 180)); // 0.2 + 0.625 = 0.825 degrees away across the pole
            Assert.False(Marked(grid, 88.375, 180)); // 1.8 degrees away
            Assert.True(Marked(grid, 88.875, 0));
        }

        [Fact]
        public void MatchesBruteForceGreatCircleTest()
        {
            var grid = new CoverageGrid();
            double lat0 = 71.3, lon0 = -178.7, radius = 3000.0 / MunRadius; // 3 km
            grid.MarkSwath(lat0, lon0, 3000, MunRadius, 30000);
            Assert.True(CountMarked(grid) > 10);

            for (int row = 0; row < CoverageGrid.Height; row++)
            {
                for (int column = 0; column < CoverageGrid.Width; column++)
                {
                    double d = Distance(lat0, lon0, CoverageGrid.CellCenterLatitude(row), CoverageGrid.CellCenterLongitude(column));
                    bool marked = grid.GetCell(row, column) != 0;
                    if (d < radius - 1e-9)
                    {
                        Assert.True(marked, "missing " + row + "," + column);
                    }
                    else if (d > radius + 1e-9)
                    {
                        Assert.False(marked && !(row == CoverageGrid.RowOf(lat0) && column == CoverageGrid.ColumnOf(lon0)),
                            "extra " + row + "," + column);
                    }
                }
            }
        }

        [Fact]
        public void VersionBumpsOncePerImprovingCall()
        {
            var grid = new CoverageGrid();
            Assert.Equal(0, grid.Version);

            grid.MarkSwath(0, 0, 5000, MunRadius, 30000); // many cells, one bump
            Assert.Equal(1, grid.Version);

            grid.MarkSwath(0, 0, 5000, MunRadius, 50000); // nothing improves
            Assert.Equal(1, grid.Version);

            grid.MarkSwath(0, 0, 5000, MunRadius, 20000);
            Assert.Equal(2, grid.Version);

            grid.MarkTrack(0, 0, 0, 20, 5000, MunRadius, 20000); // a long track: one bump, not one per sample
            Assert.Equal(3, grid.Version);

            grid.MarkTrack(0, 0, 0, 20, 5000, MunRadius, 20000);
            Assert.Equal(3, grid.Version);
        }

        [Fact]
        public void IdenticalEndpointsMarkASwath()
        {
            var grid = new CoverageGrid();
            grid.MarkTrack(10, 10, 10, 10, 2000, MunRadius, 30000);
            Assert.True(Marked(grid, 10, 10));
            Assert.Equal(1, grid.Version);
        }

        [Fact]
        public void AntipodalEndpointsDoNotThrow()
        {
            var grid = new CoverageGrid();
            grid.MarkTrack(0, 0, 0, 180, 20000, MunRadius, 30000);
            Assert.True(Marked(grid, 0.125, 90.125) || Marked(grid, 89.875, 90.125) || CountMarked(grid) > 100);
        }

        [Fact]
        public void DegenerateInputsAreIgnored()
        {
            var grid = new CoverageGrid();
            grid.MarkSwath(double.NaN, 0, 1000, MunRadius, 30000);
            grid.MarkSwath(0, 0, 1000, 0, 30000);
            grid.MarkSwath(0, 0, 1000, MunRadius, double.NaN);
            grid.MarkTrack(0, 0, double.NaN, 1, 1000, MunRadius, 30000);
            Assert.Equal(0, grid.Version);
            Assert.Equal(0, CountMarked(grid));
        }

        [Fact]
        public void SerializeRoundTrip()
        {
            var grid = new CoverageGrid();
            grid.MarkTrack(-60, -170, 40, 170, 8000, MunRadius, 45000);
            grid.MarkSwath(10, 10, 3000, MunRadius, 12000);

            string text = grid.Serialize();
            CoverageGrid back = CoverageGrid.Deserialize(text, out bool ok);

            Assert.True(ok);
            Assert.Equal(grid.ToBytes(), back.ToBytes());
            Assert.True(text.Length < 200000); // mostly empty: it compresses
        }

        [Fact]
        public void EmptyGridRoundTrips()
        {
            string text = new CoverageGrid().Serialize();
            CoverageGrid back = CoverageGrid.Deserialize(text, out bool ok);
            Assert.True(ok);
            Assert.Equal(0, CountMarked(back));
        }

        [Fact]
        public void GarbageDeserializesEmpty()
        {
            CoverageGrid grid = CoverageGrid.Deserialize("not base64!", out bool ok);
            Assert.False(ok);
            Assert.NotNull(grid);
            Assert.Equal(0, CountMarked(grid));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("AAAA")] // valid base64, not deflate data of a grid
        [InlineData("SGVsbG8gd29ybGQ=")]
        public void BadDataDeserializesEmpty(string data)
        {
            CoverageGrid grid = CoverageGrid.Deserialize(data, out bool ok);
            Assert.False(ok);
            Assert.Equal(0, CountMarked(grid));
        }

        [Fact]
        public void WrongLengthDeserializesEmpty()
        {
            foreach (int length in new[] { 10, CoverageGrid.Width * CoverageGrid.Height - 1, CoverageGrid.Width * CoverageGrid.Height + 1 })
            {
                var raw = new byte[length];
                raw[0] = 7;
                using (var output = new System.IO.MemoryStream())
                {
                    using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionMode.Compress, true))
                    {
                        deflate.Write(raw, 0, raw.Length);
                    }

                    CoverageGrid grid = CoverageGrid.Deserialize(Convert.ToBase64String(output.ToArray()), out bool ok);
                    Assert.False(ok);
                    Assert.Equal(0, CountMarked(grid));
                }
            }
        }

        private static int CountMarked(CoverageGrid grid)
        {
            int count = 0;
            foreach (byte b in grid.ToBytes())
            {
                if (b != 0)
                {
                    count++;
                }
            }

            return count;
        }

        // Great-circle angle in radians between two points given in degrees.
        private static double Distance(double lat0, double lon0, double lat1, double lon1)
        {
            double p0 = lat0 * Math.PI / 180, p1 = lat1 * Math.PI / 180, dl = (lon1 - lon0) * Math.PI / 180;
            double c = Math.Sin(p0) * Math.Sin(p1) + Math.Cos(p0) * Math.Cos(p1) * Math.Cos(dl);
            return Math.Acos(Math.Max(-1, Math.Min(1, c)));
        }
    }
}
