using System;
using Subterra.Core.Numerics;

namespace Subterra.Core.Geo
{
    /// <summary>
    /// Latitude/longitude ↔ unit direction in the PQS frame of KSP: Y is the north pole and
    /// Direction(lat, lon) = (cos φ cos λ, sin φ, cos φ sin λ), the same as
    /// <c>QuaternionD.AngleAxis(lon, Vector3d.down) * QuaternionD.AngleAxis(lat, Vector3d.forward) * Vector3d.right</c>.
    /// </summary>
    public static class BodyFrame
    {
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        public static Vec3d Direction(double latDeg, double lonDeg)
        {
            double lat = latDeg * Deg2Rad;
            double lon = lonDeg * Deg2Rad;
            double cosLat = Math.Cos(lat);
            return new Vec3d(cosLat * Math.Cos(lon), Math.Sin(lat), cosLat * Math.Sin(lon));
        }

        /// <summary>Latitude in [-90, 90] and longitude in (-180, 180] of a (not necessarily unit) direction.</summary>
        public static void ToLatLon(Vec3d dir, out double latDeg, out double lonDeg)
        {
            double horizontal = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            latDeg = Math.Atan2(dir.Y, horizontal) * Rad2Deg;
            lonDeg = Math.Atan2(dir.Z, dir.X) * Rad2Deg;
        }

        /// <summary>Longitude wrapped to (-180, 180].</summary>
        public static double WrapLon(double lonDeg)
        {
            double lon = lonDeg % 360.0;
            if (lon > 180)
            {
                lon -= 360;
            }
            else if (lon <= -180)
            {
                lon += 360;
            }

            return lon;
        }

        /// <summary>Great-circle distance in metres between two directions on a sphere of <paramref name="radius"/>.</summary>
        public static double SurfaceDistance(Vec3d a, Vec3d b, double radius)
        {
            Vec3d na = a.Normalized, nb = b.Normalized;
            return Math.Atan2(Vec3d.Cross(na, nb).Length, Vec3d.Dot(na, nb)) * radius;
        }
    }
}
