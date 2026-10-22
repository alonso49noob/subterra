using System;
using System.Collections.Generic;
using System.Linq;

namespace Subterra.Core.Radar
{
    /// <summary>
    /// Turns radar coverage and detections into the map-view texture: an equirectangular RGBA32 raster, rows from
    /// south to north (row 0 is latitude -90, the last row +90), columns from west to east (column 0 starts at
    /// longitude -180, the last column ends at +180), exactly like <see cref="CoverageGrid"/> only coarser. The pixel
    /// (x, y) of a w x h raster is centred on longitude -180 + (x + 0.5) * 360 / w and latitude -90 + (y + 0.5) * 180 / h.
    /// Pure and thread-safe: the plugin calls it on a worker.
    /// </summary>
    public static class OverlayPainter
    {
        // decidido por Claude, revisar (ruling R29): plain grey (128,128,128,40) vanished on the Mun in daylight; a faint
        // blue-grey stays visible on grey and on dark ground without competing with the yellow-to-purple discs.
        public const byte ScannedRed = 120;
        public const byte ScannedGreen = 160;
        public const byte ScannedBlue = 220;
        public const byte ScannedAlpha = 48;
        public const byte HintAlpha = 140;
        public const byte DetectedAlpha = 230;

        /// <summary>Confidence value inside a hint disc.</summary>
        public const byte HintConfidence = 102;

        /// <summary>Confidence value inside a detection disc.</summary>
        public const byte DetectedConfidence = 255;

        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;

        /// <summary>Depth (m) at and beyond which <see cref="DepthRamp"/> stops changing.</summary>
        private const double RampDepth = 1000;

        /// <summary>Column of the pixel that contains the longitude (any range; wrapped into [-180, 180)).</summary>
        public static int ColumnOf(int w, double lon)
        {
            double wrapped = (lon + 180) % 360;
            if (wrapped < 0)
            {
                wrapped += 360;
            }

            int x = (int)Math.Floor(wrapped / 360.0 * w);
            return x < 0 ? 0 : x >= w ? w - 1 : x;
        }

        /// <summary>Row of the pixel that contains the latitude (clamped to the raster).</summary>
        public static int RowOf(int h, double lat)
        {
            int y = (int)Math.Floor((lat + 90) / 180.0 * h);
            return y < 0 ? 0 : y >= h ? h - 1 : y;
        }

        public static double CenterLongitude(int w, int x) => -180 + (x + 0.5) * 360.0 / w;

        public static double CenterLatitude(int h, int y) => -90 + (y + 0.5) * 180.0 / h;

        /// <summary>
        /// 0 m is (255, 220, 0), 1000 m or more is (150, 0, 200), linear in between; a depth below 0 or NaN counts
        /// as 0 m.
        /// </summary>
        public static (byte r, byte g, byte b) DepthRamp(double depth)
        {
            double t = double.IsNaN(depth) ? 0 : Math.Max(0, Math.Min(1, depth / RampDepth));
            return (Lerp(255, 150, t), Lerp(220, 0, t), Lerp(0, 200, t));
        }

        /// <summary>
        /// The RGBA32 bytes (w * h * 4). Scanned ground (a cell either instrument scanned) is (120, 160, 220, 48);
        /// then each hint is a disc of radius <see cref="NetworkDetection.Uncertainty"/> in the colour of
        /// <c>DepthRamp(depthMid)</c> with alpha 140, and each detection a disc of the same colour with alpha 230 and
        /// radius max(Uncertainty, 2 px); detections go over hints, and within a level the larger discs go first, so
        /// a small one is never hidden. Everything else is (120, 160, 220, 0): transparent, with the colour of the scanned tint so
        /// that texture filtering does not darken the edge of a coloured disc.
        /// <paramref name="eco"/> and <paramref name="plomada"/> may be null. The caller passes only the detections of
        /// the body shown; <paramref name="bodyRadius"/> (metres) turns the uncertainty into a size on the sphere
        /// (decidido por Claude, revisar: an argument the brief's signature did not have, a metre size needs it).
        /// </summary>
        public static byte[] Paint(int w, int h, CoverageGrid eco, CoverageGrid plomada, IEnumerable<NetworkDetection> detections, double bodyRadius)
        {
            if (w <= 0 || h <= 0)
            {
                throw new ArgumentOutOfRangeException(w <= 0 ? nameof(w) : nameof(h));
            }

            var pixels = new byte[w * h * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = ScannedRed;
                pixels[i + 1] = ScannedGreen;
                pixels[i + 2] = ScannedBlue;
            }

            PaintScanned(pixels, w, h, eco);
            PaintScanned(pixels, w, h, plomada);

            if (detections != null && bodyRadius > 0)
            {
                foreach (NetworkDetection d in Ordered(detections, DetectionLevel.Hint))
                {
                    Disc(w, h, d, bodyRadius, 1, (x, y) => Put(pixels, w, x, y, d, HintAlpha));
                }

                foreach (NetworkDetection d in Ordered(detections, DetectionLevel.Detected))
                {
                    Disc(w, h, d, bodyRadius, 2, (x, y) => Put(pixels, w, x, y, d, DetectedAlpha));
                }
            }

            return pixels;
        }

        /// <summary>
        /// One byte per pixel (w * h, same layout as <see cref="Paint"/>): 0 where nothing was found, 102 inside a
        /// hint disc, 255 inside a detection disc (the same discs as <see cref="Paint"/>).
        /// </summary>
        public static byte[] Confidence(int w, int h, IEnumerable<NetworkDetection> detections, double bodyRadius)
        {
            if (w <= 0 || h <= 0)
            {
                throw new ArgumentOutOfRangeException(w <= 0 ? nameof(w) : nameof(h));
            }

            var confidence = new byte[w * h];
            if (detections != null && bodyRadius > 0)
            {
                foreach (NetworkDetection d in Ordered(detections, DetectionLevel.Hint))
                {
                    Disc(w, h, d, bodyRadius, 1, (x, y) => confidence[y * w + x] = HintConfidence);
                }

                foreach (NetworkDetection d in Ordered(detections, DetectionLevel.Detected))
                {
                    Disc(w, h, d, bodyRadius, 2, (x, y) => confidence[y * w + x] = DetectedConfidence);
                }
            }

            return confidence;
        }

        private static IEnumerable<NetworkDetection> Ordered(IEnumerable<NetworkDetection> detections, DetectionLevel level) =>
            detections.Where(d => d != null && d.Level == level).OrderByDescending(d => d.Uncertainty);

        private static byte Lerp(int from, int to, double t) => (byte)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);

        private static void Put(byte[] pixels, int w, int x, int y, NetworkDetection d, byte alpha)
        {
            (byte r, byte g, byte b) = DepthRamp((d.DepthMin + d.DepthMax) / 2);
            int i = (y * w + x) * 4;
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = alpha;
        }

        private static void PaintScanned(byte[] pixels, int w, int h, CoverageGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            byte[] cells = grid.ToBytes();
            if (w <= CoverageGrid.Width && h <= CoverageGrid.Height)
            {
                // Coarser than the grid: every pixel contains at least one cell centre, so pushing each scanned cell
                // to the pixel it falls in leaves no holes and never loses a thin ground track.
                for (int row = 0; row < CoverageGrid.Height; row++)
                {
                    int y = RowOf(h, CoverageGrid.CellCenterLatitude(row));
                    for (int column = 0; column < CoverageGrid.Width; column++)
                    {
                        if (cells[row * CoverageGrid.Width + column] != 0)
                        {
                            SetScanned(pixels, ((y * w) + ColumnOf(w, CoverageGrid.CellCenterLongitude(column))) * 4);
                        }
                    }
                }
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    int row = CoverageGrid.RowOf(CenterLatitude(h, y));
                    for (int x = 0; x < w; x++)
                    {
                        if (cells[row * CoverageGrid.Width + CoverageGrid.ColumnOf(CenterLongitude(w, x))] != 0)
                        {
                            SetScanned(pixels, (y * w + x) * 4);
                        }
                    }
                }
            }
        }

        private static void SetScanned(byte[] pixels, int i)
        {
            pixels[i] = ScannedRed;
            pixels[i + 1] = ScannedGreen;
            pixels[i + 2] = ScannedBlue;
            pixels[i + 3] = ScannedAlpha;
        }

        /// <summary>
        /// Calls <paramref name="visit"/> for every pixel whose centre is within the detection's uncertainty (great
        /// circle on a sphere of <paramref name="bodyRadius"/>, so the disc stretches in longitude towards the poles
        /// the way the real circle does) of its centroid; the disc is at least <paramref name="minPixels"/> pixels
        /// of radius.
        /// </summary>
        private static void Disc(int w, int h, NetworkDetection d, double bodyRadius, double minPixels, Action<int, int> visit)
        {
            if (double.IsNaN(d.Lat) || double.IsNaN(d.Lon) || double.IsInfinity(d.Lon) || double.IsNaN(d.Uncertainty))
            {
                return;
            }

            double lat = Math.Max(-90, Math.Min(90, d.Lat));
            double lon = ((d.Lon + 180) % 360 + 360) % 360 - 180;
            double pixelRad = 2 * Math.PI / w;
            double rho = Math.Min(Math.PI, Math.Max(Math.Max(0, d.Uncertainty) / bodyRadius, minPixels * pixelRad));

            double phi0 = lat * Deg2Rad;
            double sin0 = Math.Sin(phi0);
            double cos0 = Math.Cos(phi0);
            double cosRho = Math.Cos(rho);

            int rowMin = Math.Max(0, (int)Math.Ceiling((lat - rho * Rad2Deg + 90) / 180.0 * h - 0.5));
            int rowMax = Math.Min(h - 1, (int)Math.Floor((lat + rho * Rad2Deg + 90) / 180.0 * h - 0.5));
            for (int y = rowMin; y <= rowMax; y++)
            {
                double phi = CenterLatitude(h, y) * Deg2Rad;
                double sinP = Math.Sin(phi);
                double cosP = Math.Cos(phi);
                double denominator = cos0 * cosP;
                double halfSpan; // degrees of longitude either side of lon
                if (denominator < 1e-12)
                {
                    if (sin0 * sinP < cosRho - 1e-12)
                    {
                        continue;
                    }

                    halfSpan = 180;
                }
                else
                {
                    double c = (cosRho - sin0 * sinP) / denominator;
                    if (c > 1 + 1e-12)
                    {
                        continue;
                    }

                    halfSpan = c <= -1 ? 180 : Math.Acos(Math.Min(1, c)) * Rad2Deg;
                }

                if (halfSpan >= 180)
                {
                    for (int x = 0; x < w; x++)
                    {
                        visit(x, y);
                    }

                    continue;
                }

                int first = (int)Math.Ceiling((lon - halfSpan + 180) / 360.0 * w - 0.5);
                int last = (int)Math.Floor((lon + halfSpan + 180) / 360.0 * w - 0.5);
                if (last - first + 1 >= w)
                {
                    first = 0;
                    last = w - 1;
                }

                for (int x = first; x <= last; x++)
                {
                    int wrapped = x % w;
                    visit(wrapped < 0 ? wrapped + w : wrapped, y);
                }
            }
        }
    }
}
