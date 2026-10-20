using System;
using System.Collections.Generic;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;

namespace Subterra.Core.Procedural
{
    /// <summary>
    /// Equiangular cube-sphere cells of about <c>targetSize</c> metres: face by dominant component,
    /// face coordinates a = atan(u / max) × 4/π in [-1, 1], N × N cells per face.
    /// </summary>
    public sealed class CellGrid
    {
        // Face normal, U axis, V axis (right-handed per face, any fixed choice works).
        private static readonly Vec3d[] Normals = { new Vec3d(1, 0, 0), new Vec3d(-1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, -1, 0), new Vec3d(0, 0, 1), new Vec3d(0, 0, -1) };
        private static readonly Vec3d[] AxisU = { new Vec3d(0, 0, 1), new Vec3d(0, 0, -1), new Vec3d(1, 0, 0), new Vec3d(1, 0, 0), new Vec3d(-1, 0, 0), new Vec3d(1, 0, 0) };
        private static readonly Vec3d[] AxisV = { new Vec3d(0, 1, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1), new Vec3d(0, 0, -1), new Vec3d(0, 1, 0), new Vec3d(0, 1, 0) };

        private readonly double radius;

        public CellGrid(double bodyRadius, double targetSize = 50000)
        {
            radius = bodyRadius;
            N = Math.Max(1, (int)Math.Ceiling(Math.PI / 2 * bodyRadius / targetSize));
        }

        public int N { get; }

        public int CellCount => 6 * N * N;

        public double BodyRadius => radius;

        public IEnumerable<CellId> All()
        {
            for (int index = 0; index < CellCount; index++)
            {
                yield return CellId.FromIndex(index, N);
            }
        }

        public CellId CellOf(Vec3d dir)
        {
            int face = FaceOf(dir);
            double m = Vec3d.Dot(dir, Normals[face]);
            double a = Math.Atan(Vec3d.Dot(dir, AxisU[face]) / m) * 4 / Math.PI;
            double b = Math.Atan(Vec3d.Dot(dir, AxisV[face]) / m) * 4 / Math.PI;
            return new CellId(face, ToIndex(a), ToIndex(b));
        }

        /// <summary>Unit direction of the cell centre.</summary>
        public Vec3d Center(CellId c) => FacePoint(c.Face, -1 + (2 * c.I + 1.0) / N, -1 + (2 * c.J + 1.0) / N);

        /// <summary>Cells that may contain a point within <paramref name="radiusMeters"/> of the surface point in direction <paramref name="dir"/>.</summary>
        public IEnumerable<CellId> Near(Vec3d dir, double radiusMeters)
        {
            Vec3d d = dir.Normalized;
            // A cell spans at most about (π/2)/N radians across; its centre is within that of any point in it.
            double limit = radiusMeters / radius + Math.PI / 2 / N;
            double cosLimit = Math.Cos(Math.Min(Math.PI, limit));
            for (int index = 0; index < CellCount; index++)
            {
                CellId c = CellId.FromIndex(index, N);
                if (Vec3d.Dot(d, Center(c)) >= cosLimit)
                {
                    yield return c;
                }
            }
        }

        /// <summary>
        /// Latitude/longitude box of the cell. Cells across the antimeridian return lonMax &gt; 180;
        /// cells holding a pole span all longitudes up to ±90°.
        /// </summary>
        public (double latMin, double latMax, double lonMin, double lonMax) Bounds(CellId c)
        {
            const int steps = 16;
            double latMin = double.MaxValue, latMax = double.MinValue;
            var lons = new List<double>();
            double a0 = -1 + 2.0 * c.I / N, b0 = -1 + 2.0 * c.J / N, span = 2.0 / N;
            for (int i = 0; i <= steps; i++)
            {
                for (int j = 0; j <= steps; j++)
                {
                    BodyFrame.ToLatLon(FacePoint(c.Face, a0 + span * i / steps, b0 + span * j / steps), out double lat, out double lon);
                    latMin = Math.Min(latMin, lat);
                    latMax = Math.Max(latMax, lat);
                    lons.Add(lon);
                }
            }

            bool northPole = c.Face == 2 && ContainsFaceCentre(c);
            bool southPole = c.Face == 3 && ContainsFaceCentre(c);
            if (northPole || southPole)
            {
                return (northPole ? latMin : -90, northPole ? 90 : latMax, -180, 180);
            }

            double lonMin = double.MaxValue, lonMax = double.MinValue;
            foreach (double lon in lons)
            {
                lonMin = Math.Min(lonMin, lon);
                lonMax = Math.Max(lonMax, lon);
            }

            if (lonMax - lonMin > 180)
            {
                // Across the antimeridian: move the negative longitudes up by 360.
                lonMin = double.MaxValue;
                lonMax = double.MinValue;
                foreach (double raw in lons)
                {
                    double lon = raw < 0 ? raw + 360 : raw;
                    lonMin = Math.Min(lonMin, lon);
                    lonMax = Math.Max(lonMax, lon);
                }
            }

            return (latMin, latMax, lonMin, lonMax);
        }

        /// <summary>
        /// A regular n × n latitude/longitude grid over the cell's <see cref="Bounds"/> grown by
        /// <paramref name="marginMeters"/> (row by row from latMin; the same layout as <see cref="Geo.GridSamples"/>).
        /// </summary>
        public IReadOnlyList<(double lat, double lon)> SampleGrid(CellId c, int n = 64, double marginMeters = 0)
        {
            var (latMin, latMax, lonMin, lonMax) = GridRect(c, marginMeters);
            var points = new List<(double, double)>(n * n);
            for (int i = 0; i < n; i++)
            {
                double lat = latMin + (latMax - latMin) * i / (n - 1);
                for (int j = 0; j < n; j++)
                {
                    points.Add((lat, lonMin + (lonMax - lonMin) * j / (n - 1)));
                }
            }

            return points;
        }

        /// <summary>The rectangle <see cref="SampleGrid"/> covers.</summary>
        public (double latMin, double latMax, double lonMin, double lonMax) GridRect(CellId c, double marginMeters)
        {
            var (latMin, latMax, lonMin, lonMax) = Bounds(c);
            double marginDeg = marginMeters / radius * BodyFrame.Rad2Deg;
            latMin = Math.Max(-90, latMin - marginDeg);
            latMax = Math.Min(90, latMax + marginDeg);
            if (lonMax - lonMin < 360)
            {
                double cosLat = Math.Max(0.05, Math.Cos(Math.Max(Math.Abs(latMin), Math.Abs(latMax)) * BodyFrame.Deg2Rad));
                double lonMargin = Math.Min(30, marginDeg / cosLat);
                lonMin -= lonMargin;
                lonMax += lonMargin;
            }

            return (latMin, latMax, lonMin, lonMax);
        }

        private bool ContainsFaceCentre(CellId c)
        {
            int middle = ToIndex(0);
            return c.I == middle && c.J == middle || N % 2 == 0 && (c.I == middle || c.I == middle - 1) && (c.J == middle || c.J == middle - 1);
        }

        private Vec3d FacePoint(int face, double a, double b)
        {
            double su = Math.Tan(a * Math.PI / 4), sv = Math.Tan(b * Math.PI / 4);
            return (Normals[face] + AxisU[face] * su + AxisV[face] * sv).Normalized;
        }

        private int ToIndex(double a) => Math.Max(0, Math.Min(N - 1, (int)Math.Floor((a + 1) / 2 * N)));

        private static int FaceOf(Vec3d d)
        {
            double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
            if (ax >= ay && ax >= az)
            {
                return d.X >= 0 ? 0 : 1;
            }

            if (ay >= az)
            {
                return d.Y >= 0 ? 2 : 3;
            }

            return d.Z >= 0 ? 4 : 5;
        }
    }
}
