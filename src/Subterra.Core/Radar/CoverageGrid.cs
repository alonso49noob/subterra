using System;
using System.IO;
using System.IO.Compression;

namespace Subterra.Core.Radar
{
    /// <summary>
    /// What an orbital instrument has scanned on one body: a 0.25 degree grid that remembers, for each ground cell,
    /// the lowest altitude it was scanned from. One byte per cell, log-encoded (0 = never scanned; a larger byte is
    /// a lower altitude). Row 0 is latitude -90 and row 719 is +90; column 0 is longitude -180 and column 1439 is
    /// +180, so cell centres sit at -89.875 ... 89.875 and -179.875 ... 179.875.
    /// Threading contract: one writer (the main thread) calls MarkSwath/MarkTrack. Other threads may read cells
    /// racily (a byte is read or written atomically, and a cell only ever goes up) and should read
    /// <see cref="Version"/> before reading the cells: a cell change is then never missed by the next version check.
    /// </summary>
    public sealed class CoverageGrid
    {
        public const int Width = 1440;
        public const int Height = 720;

        private const double CellDegrees = 360.0 / Width;
        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;

        // Codes advance by one every 1/25 of an octave of altitude (about 2.8 %), so the round trip stays under 3 %.
        private const double StepsPerOctave = 25;
        private const double ReferenceAltitude = 1000;

        private readonly byte[] _cells = new byte[Width * Height];
        private volatile int _version;

        /// <summary>
        /// Goes up by exactly one for every call (MarkSwath, MarkTrack) that improved at least one cell, not for every
        /// cell or sample, and only after the cells were written; it never goes down. Safe to read from any thread.
        /// the map layer compares it with the value it last painted to know when to repaint.
        /// </summary>
        public int Version => _version;

        /// <summary>
        /// Altitude in metres to its byte: 255 - round(25Â·log2(altitude / 1000)) clamped to 1..255, so 1 km is 255 and
        /// every doubling of altitude costs 25. Never 0 (0 means unscanned), except for NaN.
        /// </summary>
        public static byte Encode(double altitude)
        {
            if (double.IsNaN(altitude))
            {
                return 0;
            }

            if (altitude <= 0)
            {
                return 255;
            }

            double code = 255 - Math.Round(Math.Log(altitude / ReferenceAltitude, 2) * StepsPerOctave);
            if (code < 1)
            {
                return 1;
            }

            return code > 255 ? (byte)255 : (byte)code;
        }

        /// <summary>The altitude in metres a byte stands for (the encoding of 0 is "unscanned" and has no altitude).</summary>
        public static double Decode(byte b) => ReferenceAltitude * Math.Pow(2, (255 - b) / StepsPerOctave);

        /// <summary>The lowest altitude the cell under (lat, lon) was scanned from, or null if it never was.</summary>
        public double? BestAltitude(double lat, double lon)
        {
            if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lat) || double.IsInfinity(lon))
            {
                return null;
            }

            byte b = _cells[RowOf(lat) * Width + ColumnOf(lon)];
            return b == 0 ? (double?)null : Decode(b);
        }

        /// <summary>The raw byte of a cell (0 = unscanned). Row and column are taken as they are.</summary>
        public byte GetCell(int row, int column) => _cells[row * Width + column];

        /// <summary>A copy of every cell, row by row (row 0 first).</summary>
        public byte[] ToBytes() => (byte[])_cells.Clone();

        public static int RowOf(double lat)
        {
            int row = (int)Math.Floor((lat + 90) / CellDegrees);
            return row < 0 ? 0 : row >= Height ? Height - 1 : row;
        }

        /// <summary>Longitudes in any range are wrapped into [-180, 180) first.</summary>
        public static int ColumnOf(double lon)
        {
            int column = (int)Math.Floor((NormalizeLongitude(lon) + 180) / CellDegrees);
            return column < 0 ? 0 : column >= Width ? Width - 1 : column;
        }

        public static double CellCenterLatitude(int row) => -90 + (row + 0.5) * CellDegrees;

        public static double CellCenterLongitude(int column) => -180 + (column + 0.5) * CellDegrees;

        /// <summary>
        /// Marks every cell whose centre is within <paramref name="halfWidth"/> metres (great-circle distance on a
        /// sphere of <paramref name="bodyRadius"/>) of (lat, lon) as scanned from <paramref name="altitude"/>; a cell
        /// keeps its lowest altitude. The cell that contains the point itself is always marked, even when the swath is
        /// narrower than a cell (decidido por Claude, revisar: a ground track must always leave something behind).
        /// </summary>
        public void MarkSwath(double lat, double lon, double halfWidth, double bodyRadius, double altitude)
        {
            byte code = Encode(altitude);
            if (code == 0 || double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lon)
                || double.IsNaN(halfWidth) || double.IsNaN(bodyRadius) || bodyRadius <= 0)
            {
                return;
            }

            if (MarkSwathCore(lat, lon, halfWidth / bodyRadius, code))
            {
                _version = _version + 1;
            }
        }

        /// <summary>One swath without touching Version; rho is the angular radius in radians, code the byte to raise to.</summary>
        private bool MarkSwathCore(double lat, double lon, double rho, byte code)
        {
            lat = Math.Max(-90, Math.Min(90, lat));
            lon = NormalizeLongitude(lon);
            bool improved = Raise(RowOf(lat), ColumnOf(lon), code);
            if (rho > 0)
            {
                improved |= MarkDisc(lat, lon, Math.Min(rho, Math.PI), code);
            }

            return improved;
        }

        /// <summary>
        /// Marks the swath along the great circle (shortest way) from the first point to the second. Samples are taken
        /// every min(max(halfWidth, cellSize / 2), cellSize) metres, cellSize being the width of a cell at the highest
        /// latitude of the path (the equatorial length on the equator): consecutive samples are then in the same or
        /// adjacent cells, the cell under each sample is always marked, so the marked cells form an unbroken chain, and
        /// a very narrow swath costs no more than two samples per cell (decidido por Claude, revisar). A cell the path
        /// only clips at a corner can still be missed. Version goes up once, at the end, if anything improved.
        /// </summary>
        public void MarkTrack(double lat0, double lon0, double lat1, double lon1, double halfWidth, double bodyRadius, double altitude)
        {
            byte code = Encode(altitude);
            if (code == 0 || double.IsNaN(lat0) || double.IsNaN(lon0) || double.IsNaN(lat1) || double.IsNaN(lon1)
                || double.IsInfinity(lon0) || double.IsInfinity(lon1) || double.IsNaN(halfWidth)
                || double.IsNaN(bodyRadius) || bodyRadius <= 0)
            {
                return;
            }

            double x0, y0, z0, x1, y1, z1;
            ToVector(lat0, lon0, out x0, out y0, out z0);
            ToVector(lat1, lon1, out x1, out y1, out z1);

            double cx = y0 * z1 - z0 * y1;
            double cy = z0 * x1 - x0 * z1;
            double cz = x0 * y1 - y0 * x1;
            double sin = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            double dot = x0 * x1 + y0 * y1 + z0 * z1;
            double theta = Math.Atan2(sin, dot); // angle between the points, 0..pi

            // A unit vector at 90 degrees from the start, in the plane of the path: the path is
            // cos(a)Â·p0 + sin(a)Â·q. For antipodal points any plane will do.
            double qx, qy, qz;
            if (sin > 1e-12)
            {
                qx = (x1 - dot * x0) / sin;
                qy = (y1 - dot * y0) / sin;
                qz = (z1 - dot * z0) / sin;
            }
            else
            {
                // Pick the axis least aligned with p0 and take the perpendicular.
                double ax = Math.Abs(x0) < 0.9 ? 1 : 0;
                double ay = ax == 1 ? 0 : 1;
                qx = ay * z0;
                qy = -ax * z0;
                qz = ax * y0 - ay * x0;
                double len = Math.Sqrt(qx * qx + qy * qy + qz * qz);
                qx /= len;
                qy /= len;
                qz /= len;
            }

            // The narrowest cell the path crosses is at its highest latitude (a degree of longitude shrinks with
            // the cosine), including the vertex of the great circle when it lies inside the arc.
            double maxAbsZ = Math.Max(Math.Abs(z0), Math.Abs(z1));
            double vertex = Math.Atan2(qz, z0);
            if (vertex > 0 && vertex < theta)
            {
                maxAbsZ = Math.Max(maxAbsZ, Math.Abs(Math.Cos(vertex) * z0 + Math.Sin(vertex) * qz));
            }

            vertex = Math.Atan2(-qz, -z0);
            if (vertex > 0 && vertex < theta)
            {
                maxAbsZ = Math.Max(maxAbsZ, Math.Abs(Math.Cos(vertex) * z0 + Math.Sin(vertex) * qz));
            }

            // decidido por Claude, revisar: the cell width is floored at 5 % of the equatorial one, so a track near a
            // pole costs at most 20x the samples instead of growing without bound (the swath discs cover the rest).
            double narrowest = Math.Max(0.05, Math.Sqrt(Math.Max(0, 1 - maxAbsZ * maxAbsZ)));
            double cellSize = bodyRadius * CellDegrees * Deg2Rad * narrowest;
            double step = Math.Min(Math.Max(halfWidth, cellSize / 2), cellSize);
            double rho = halfWidth / bodyRadius;

            int segments = (int)Math.Ceiling(theta * bodyRadius / step);
            if (segments < 1)
            {
                if (MarkSwathCore(lat0, lon0, rho, code))
                {
                    _version = _version + 1;
                }

                return;
            }

            bool improved = false;
            for (int i = 0; i <= segments; i++)
            {
                double a = theta * i / segments;
                double c = Math.Cos(a);
                double s = Math.Sin(a);
                double px = c * x0 + s * qx;
                double py = c * y0 + s * qy;
                double pz = c * z0 + s * qz;
                double lat = Math.Asin(Math.Max(-1, Math.Min(1, pz))) * Rad2Deg;
                double lon = Math.Atan2(py, px) * Rad2Deg;
                improved |= MarkSwathCore(lat, lon, rho, code);
            }

            if (improved)
            {
                _version = _version + 1;
            }
        }

        /// <summary>Deflate + base64 of the cells, row by row.</summary>
        public string Serialize()
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                {
                    deflate.Write(_cells, 0, _cells.Length);
                }

                return Convert.ToBase64String(output.ToArray());
            }
        }

        /// <summary>
        /// Rebuilds a grid from <see cref="Serialize"/>. Anything that is not exactly one grid (null, empty, not
        /// base64, not deflate, wrong length) gives an empty grid and ok = false; it never throws.
        /// </summary>
        public static CoverageGrid Deserialize(string data, out bool ok)
        {
            var grid = new CoverageGrid();
            ok = false;
            if (string.IsNullOrWhiteSpace(data))
            {
                return grid;
            }

            try
            {
                byte[] packed = Convert.FromBase64String(data.Trim());
                var loaded = new byte[Width * Height];
                using (var input = new MemoryStream(packed))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                {
                    int total = 0;
                    while (total < loaded.Length)
                    {
                        int read = deflate.Read(loaded, total, loaded.Length - total);
                        if (read <= 0)
                        {
                            return grid; // too short
                        }

                        total += read;
                    }

                    if (deflate.ReadByte() != -1)
                    {
                        return grid; // too long
                    }
                }

                Buffer.BlockCopy(loaded, 0, grid._cells, 0, loaded.Length);
                ok = true;
                return grid;
            }
            catch (Exception)
            {
                // FormatException (base64), InvalidDataException (deflate) and anything else: not a grid.
                return new CoverageGrid();
            }
        }

        private static double NormalizeLongitude(double lon)
        {
            if (lon >= -180 && lon < 180)
            {
                return lon;
            }

            double wrapped = (lon + 180) % 360;
            if (wrapped < 0)
            {
                wrapped += 360;
            }

            wrapped -= 180;
            return wrapped >= 180 ? -180 : wrapped;
        }

        private static void ToVector(double lat, double lon, out double x, out double y, out double z)
        {
            double phi = lat * Deg2Rad;
            double lambda = lon * Deg2Rad;
            double cosPhi = Math.Cos(phi);
            x = cosPhi * Math.Cos(lambda);
            y = cosPhi * Math.Sin(lambda);
            z = Math.Sin(phi);
        }

        private bool Raise(int row, int column, byte code)
        {
            int index = row * Width + column;
            if (_cells[index] >= code)
            {
                return false;
            }

            _cells[index] = code;
            return true;
        }

        /// <summary>
        /// Marks the cells whose centre is within the angular radius rho of the point, with an exact per-row test:
        /// cos d = sin(p0)sin(p) + cos(p0)cos(p)cos(dLon) >= cos(rho) gives the longitude half-span of each row in
        /// closed form, so only the rows in [lat - rho, lat + rho] and the columns inside that span are visited.
        /// A swath that covers a pole simply gets a half-span of 180 degrees on the rows near it.
        /// </summary>
        private bool MarkDisc(double lat, double lon, double rho, byte code)
        {
            double phi0 = lat * Deg2Rad;
            double sin0 = Math.Sin(phi0);
            double cos0 = Math.Cos(phi0);
            double cosRho = Math.Cos(rho);

            double latMin = lat - rho * Rad2Deg;
            double latMax = lat + rho * Rad2Deg;
            int rowMin = Math.Max(0, (int)Math.Ceiling((latMin + 90) / CellDegrees - 0.5));
            int rowMax = Math.Min(Height - 1, (int)Math.Floor((latMax + 90) / CellDegrees - 0.5));

            bool improved = false;
            for (int row = rowMin; row <= rowMax; row++)
            {
                double phi = CellCenterLatitude(row) * Deg2Rad;
                double sinP = Math.Sin(phi);
                double cosP = Math.Cos(phi);

                double denominator = cos0 * cosP;
                double halfSpan; // degrees of longitude either side of lon
                if (denominator < 1e-12)
                {
                    // The point is at a pole: every cell of the row is the same distance from it.
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
                        continue; // this row is out of reach
                    }

                    halfSpan = c <= -1 ? 180 : Math.Acos(Math.Min(1, c)) * Rad2Deg;
                }

                if (halfSpan >= 180)
                {
                    for (int column = 0; column < Width; column++)
                    {
                        improved |= Raise(row, column, code);
                    }

                    continue;
                }

                // Columns whose centre lies in [lon - halfSpan, lon + halfSpan]; they may wrap past either edge.
                int first = (int)Math.Ceiling((lon - halfSpan + 180) / CellDegrees - 0.5);
                int last = (int)Math.Floor((lon + halfSpan + 180) / CellDegrees - 0.5);
                if (last - first + 1 >= Width)
                {
                    first = 0;
                    last = Width - 1;
                }

                for (int column = first; column <= last; column++)
                {
                    int wrapped = column % Width;
                    if (wrapped < 0)
                    {
                        wrapped += Width;
                    }

                    improved |= Raise(row, wrapped, code);
                }
            }

            return improved;
        }
    }
}
