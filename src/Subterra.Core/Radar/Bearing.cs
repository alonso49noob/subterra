using System;

namespace Subterra.Core.Radar
{
    /// <summary>Compass bearing between two points of a body, for radar messages.</summary>
    public static class Bearing
    {
        /// <summary>
        /// Initial great-circle bearing from the first point to the second, in degrees clockwise from north, in
        /// [0, 360). Two coincident points give 0.
        /// </summary>
        public static double Degrees(double fromLat, double fromLon, double toLat, double toLon)
        {
            const double Rad = Math.PI / 180;
            double lat1 = fromLat * Rad, lat2 = toLat * Rad, dLon = (toLon - fromLon) * Rad;
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            if (Math.Abs(x) < 1e-15 && Math.Abs(y) < 1e-15)
            {
                return 0;
            }

            double degrees = Math.Atan2(y, x) / Rad;
            degrees %= 360;
            if (degrees < 0)
            {
                degrees += 360;
            }

            // A value that rounds to 360 would print as 360: keep the range half open.
            return degrees >= 360 ? 0 : degrees;
        }
    }
}
