using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class OverlayPainterTests
    {
        private const int W = 1024;
        private const int H = 512;
        private const double MunRadius = 200000;

        private static NetworkDetection Detection(DetectionLevel level, double lat, double lon, double uncertainty, double depthMin = 100, double depthMax = 100) =>
            new NetworkDetection { NetworkId = "t", Body = "Mun", Level = level, Lat = lat, Lon = lon, Uncertainty = uncertainty, DepthMin = depthMin, DepthMax = depthMax };

        private static (byte r, byte g, byte b, byte a) Pixel(byte[] rgba, int x, int y)
        {
            int i = (y * W + x) * 4;
            return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
        }

        private static int Alpha(byte[] rgba, int x, int y) => rgba[(y * W + x) * 4 + 3];

        // The pixel a scanned cell is drawn in is the one its centre falls in (a pixel is 0.35 degrees, a cell 0.25).
        private static int AlphaAtCell(byte[] rgba, double lat, double lon)
        {
            double cellLat = CoverageGrid.CellCenterLatitude(CoverageGrid.RowOf(lat));
            double cellLon = CoverageGrid.CellCenterLongitude(CoverageGrid.ColumnOf(lon));
            return Alpha(rgba, OverlayPainter.ColumnOf(W, cellLon), OverlayPainter.RowOf(H, cellLat));
        }

        [Fact]
        public void EmptyIsTransparent()
        {
            byte[] rgba = OverlayPainter.Paint(W, H, new CoverageGrid(), new CoverageGrid(), new List<NetworkDetection>(), MunRadius);

            Assert.Equal(W * H * 4, rgba.Length);
            for (int i = 3; i < rgba.Length; i += 4)
            {
                Assert.Equal(0, rgba[i]);
            }

            byte[] none = OverlayPainter.Paint(W, H, null, null, null, MunRadius);
            for (int i = 3; i < none.Length; i += 4)
            {
                Assert.Equal(0, none[i]);
            }
        }

        [Fact]
        public void ScannedIsTinted()
        {
            var eco = new CoverageGrid();
            eco.MarkSwath(10.1, 20.3, 0, MunRadius, 30000); // narrower than a cell: exactly the cell under the point

            byte[] rgba = OverlayPainter.Paint(W, H, eco, null, null, MunRadius);

            int x = OverlayPainter.ColumnOf(W, CoverageGrid.CellCenterLongitude(CoverageGrid.ColumnOf(20.3)));
            int y = OverlayPainter.RowOf(H, CoverageGrid.CellCenterLatitude(CoverageGrid.RowOf(10.1)));
            Assert.Equal(((byte)120, (byte)160, (byte)220, (byte)48), Pixel(rgba, x, y));
            int painted = 0;
            for (int i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 0)
                {
                    painted++;
                }
            }

            Assert.Equal(1, painted);
        }

        [Fact]
        public void ScannedByEitherInstrument()
        {
            var eco = new CoverageGrid();
            var plomada = new CoverageGrid();
            eco.MarkSwath(-40, -100, 0, MunRadius, 30000);
            plomada.MarkSwath(40, 100, 0, MunRadius, 30000);

            byte[] rgba = OverlayPainter.Paint(W, H, eco, plomada, null, MunRadius);

            Assert.Equal(48, AlphaAtCell(rgba, -40, -100));
            Assert.Equal(48, AlphaAtCell(rgba, 40, 100));
        }

        [Fact]
        public void HintDiscRadius()
        {
            // 50 km on a 200 km body is 0.25 rad = 14.32 degrees = 40.7 px of 0.3516 degrees at the equator.
            var hint = Detection(DetectionLevel.Hint, 0, 0, 50000, 0, 0);
            byte[] rgba = OverlayPainter.Paint(W, H, null, null, new[] { hint }, MunRadius);

            int y = OverlayPainter.RowOf(H, 0.1); // the row just north of the equator
            int centre = OverlayPainter.ColumnOf(W, 0.1);
            int right = centre, left = centre;
            while (Alpha(rgba, right + 1, y) != 0)
            {
                right++;
            }

            while (Alpha(rgba, left - 1, y) != 0)
            {
                left--;
            }

            double radiusPx = (right - left + 1) / 2.0;
            Assert.InRange(radiusPx, 40.7 - 1, 40.7 + 1);

            // Nothing 2 px beyond the edge, along the row and along the meridian.
            Assert.Equal(0, Alpha(rgba, right + 2, y));
            Assert.Equal(0, Alpha(rgba, left - 2, y));
            int top = y;
            while (Alpha(rgba, centre, top + 1) != 0)
            {
                top++;
            }

            Assert.InRange(top - y + 1, 40.7 - 1, 40.7 + 1.5);
            Assert.Equal(0, Alpha(rgba, centre, top + 2));

            (byte r, byte g, byte b, byte a) = Pixel(rgba, centre, y);
            Assert.Equal(((byte)255, (byte)220, (byte)0, (byte)140), (r, g, b, a));
        }

        [Fact]
        public void DiscIsWiderInLongitudeAwayFromTheEquator()
        {
            var hint = Detection(DetectionLevel.Hint, 60, 0, 50000, 0, 0);
            byte[] rgba = OverlayPainter.Paint(W, H, null, null, new[] { hint }, MunRadius);

            int y = OverlayPainter.RowOf(H, 60);
            int centre = OverlayPainter.ColumnOf(W, 0.1);
            int right = centre;
            while (Alpha(rgba, right + 1, y) != 0)
            {
                right++;
            }

            // The same circle spans about 1 / cos(60) = 2 times as many columns as on the equator.
            Assert.InRange(right - centre, 2 * 40.7 - 3, 2 * 40.7 + 3);
        }

        [Fact]
        public void DiscWrapsAcrossTheAntimeridian()
        {
            var hint = Detection(DetectionLevel.Hint, 0, 180, 50000, 0, 0);
            byte[] rgba = OverlayPainter.Paint(W, H, null, null, new[] { hint }, MunRadius);

            int y = OverlayPainter.RowOf(H, 0.1);
            Assert.NotEqual(0, Alpha(rgba, 0, y));
            Assert.NotEqual(0, Alpha(rgba, W - 1, y));
            Assert.NotEqual(0, Alpha(rgba, 30, y));
            Assert.NotEqual(0, Alpha(rgba, W - 30, y));
            Assert.Equal(0, Alpha(rgba, W / 2, y));
        }

        [Fact]
        public void DetectedIsOpaqueAndAtLeastTwoPixels()
        {
            // 100 m is far below a pixel (1.2 km): still a disc of 2 px radius, alpha 230.
            var detected = Detection(DetectionLevel.Detected, 0, 0, 100, 1000, 1000);
            byte[] rgba = OverlayPainter.Paint(W, H, null, null, new[] { detected }, MunRadius);

            int y = OverlayPainter.RowOf(H, 0.1);
            int centre = OverlayPainter.ColumnOf(W, 0.1);
            (byte r, byte g, byte b, byte a) = Pixel(rgba, centre, y);
            Assert.Equal(((byte)150, (byte)0, (byte)200, (byte)230), (r, g, b, a));
            Assert.Equal(230, Alpha(rgba, centre + 1, y));
            Assert.Equal(230, Alpha(rgba, centre - 1, y));
            Assert.Equal(0, Alpha(rgba, centre + 4, y));
            Assert.Equal(0, Alpha(rgba, centre - 4, y));
        }

        [Fact]
        public void DetectedOverHintAndSmallOverLarge()
        {
            var large = Detection(DetectionLevel.Hint, 0, 0, 50000, 0, 0);
            var small = Detection(DetectionLevel.Hint, 0, 0, 10000, 1000, 1000);
            var detected = Detection(DetectionLevel.Detected, 0, 0, 3000, 500, 500);
            byte[] rgba = OverlayPainter.Paint(W, H, null, null, new[] { small, detected, large }, MunRadius);

            int y = OverlayPainter.RowOf(H, 0.1);
            int centre = OverlayPainter.ColumnOf(W, 0.1);
            Assert.Equal(230, Alpha(rgba, centre, y)); // detected on top
            Assert.Equal(((byte)150, (byte)0, (byte)200, (byte)140), Pixel(rgba, centre + 5, y)); // small hint over large
            Assert.Equal(((byte)255, (byte)220, (byte)0, (byte)140), Pixel(rgba, centre + 30, y)); // only the large one
        }

        [Fact]
        public void DepthRampEnds()
        {
            Assert.Equal(((byte)255, (byte)220, (byte)0), OverlayPainter.DepthRamp(0));
            Assert.Equal(((byte)150, (byte)0, (byte)200), OverlayPainter.DepthRamp(1000));
            Assert.Equal(((byte)150, (byte)0, (byte)200), OverlayPainter.DepthRamp(5000));
            Assert.Equal(((byte)255, (byte)220, (byte)0), OverlayPainter.DepthRamp(-20));
            Assert.Equal(((byte)255, (byte)220, (byte)0), OverlayPainter.DepthRamp(double.NaN));
            (byte r, byte g, byte b) = OverlayPainter.DepthRamp(500);
            Assert.Equal(((byte)203, (byte)110, (byte)100), (r, g, b));
        }

        [Fact]
        public void ConfidenceLevels()
        {
            var hint = Detection(DetectionLevel.Hint, 0, -90, 50000);
            var detected = Detection(DetectionLevel.Detected, 0, 90, 20000);
            var overlap = Detection(DetectionLevel.Hint, 0, 90, 60000);
            byte[] confidence = OverlayPainter.Confidence(W, H, new[] { hint, detected, overlap }, MunRadius);

            Assert.Equal(W * H, confidence.Length);
            int y = OverlayPainter.RowOf(H, 0.1);
            Assert.Equal(102, confidence[y * W + OverlayPainter.ColumnOf(W, -90)]);
            Assert.Equal(255, confidence[y * W + OverlayPainter.ColumnOf(W, 90)]); // the detection beats the hint
            Assert.Equal(0, confidence[y * W + OverlayPainter.ColumnOf(W, 0)]);
            Assert.Equal(102, confidence[y * W + OverlayPainter.ColumnOf(W, 90) + 40]); // hint only, outside the detection
            Assert.Equal(new[] { 0, 102, 255 }, confidence.Select(c => (int)c).Distinct().OrderBy(c => c).ToArray());
        }

        [Fact]
        public void PixelMappingNorthAndMeridians()
        {
            // Rows run south to north; column 0 is the western edge of longitude -180, the last column ends at +180.
            Assert.Equal(0, OverlayPainter.RowOf(H, -90));
            Assert.Equal(H - 1, OverlayPainter.RowOf(H, 90));
            Assert.Equal(H / 2, OverlayPainter.RowOf(H, 0));
            Assert.Equal(H / 2 - 1, OverlayPainter.RowOf(H, -0.001));
            Assert.True(OverlayPainter.CenterLatitude(H, 0) < OverlayPainter.CenterLatitude(H, H - 1));
            Assert.Equal(0, OverlayPainter.ColumnOf(W, -180));
            Assert.Equal(W - 1, OverlayPainter.ColumnOf(W, 179.999));
            Assert.Equal(0, OverlayPainter.ColumnOf(W, 180)); // +180 is the same meridian as -180
            Assert.Equal(W / 2, OverlayPainter.ColumnOf(W, 0));
            Assert.Equal(W / 2 - 1, OverlayPainter.ColumnOf(W, -0.001));
            Assert.Equal(W / 4, OverlayPainter.ColumnOf(W, -90));
            Assert.Equal(3 * W / 4, OverlayPainter.ColumnOf(W, 90));
            Assert.Equal(OverlayPainter.ColumnOf(W, -170), OverlayPainter.ColumnOf(W, 190));

            // A marked point lands on the pixel the mapping says, in a corner-to-corner check.
            var eco = new CoverageGrid();
            eco.MarkSwath(80, -170, 0, MunRadius, 30000);
            byte[] rgba = OverlayPainter.Paint(W, H, eco, null, null, MunRadius);
            Assert.Equal(48, AlphaAtCell(rgba, 80, -170));
            Assert.True(OverlayPainter.RowOf(H, 80) > H * 3 / 4); // north is the high rows
            Assert.True(OverlayPainter.ColumnOf(W, -170) < W / 8); // west is the low columns
        }
    }
}
