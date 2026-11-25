using System;
using System.Collections.Generic;
using Subterra.Core.Numerics;

namespace Subterra.Core.Radar
{
    public enum ScanKind
    {
        Orbital,
        Ground,
        SeismicShot,
    }

    /// <summary>Where and how a survey is made. Positions are in body coordinates (metres).</summary>
    public readonly struct ScanGeometry
    {
        private readonly Vec3d[] _geophones;

        private ScanGeometry(ScanKind kind, double altitude, Vec3d origin, Vec3d axis, Vec3d[] geophones, Vec3d source, double chargeKg)
        {
            Kind = kind;
            Altitude = altitude;
            Origin = origin;
            Axis = axis;
            _geophones = geophones;
            Source = source;
            ChargeKg = chargeKg;
        }

        public ScanKind Kind { get; }

        /// <summary>Orbital scans: altitude over the surface, in metres.</summary>
        public double Altitude { get; }

        /// <summary>Ground scans: where the instrument is.</summary>
        public Vec3d Origin { get; }

        /// <summary>Ground scans: the direction it points at.</summary>
        public Vec3d Axis { get; }

        /// <summary>Seismic shots: the geophones (empty for the other scans).</summary>
        public IReadOnlyList<Vec3d> Geophones => _geophones ?? Array.Empty<Vec3d>();

        public Vec3d Source { get; }

        /// <summary>Seismic shots: charge in kg; 0 means the thumper.</summary>
        public double ChargeKg { get; }

        public static ScanGeometry Orbital(double altitude) =>
            new ScanGeometry(ScanKind.Orbital, altitude, Vec3d.Zero, Vec3d.Zero, null, Vec3d.Zero, 0);

        public static ScanGeometry Ground(Vec3d origin, Vec3d axis) =>
            new ScanGeometry(ScanKind.Ground, 0, origin, axis, null, Vec3d.Zero, 0);

        public static ScanGeometry SeismicShot(IReadOnlyList<Vec3d> geophones, Vec3d source, double chargeKg)
        {
            if (geophones == null)
            {
                throw new ArgumentNullException(nameof(geophones));
            }

            var copy = new Vec3d[geophones.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = geophones[i];
            }

            return new ScanGeometry(ScanKind.SeismicShot, 0, Vec3d.Zero, Vec3d.Zero, copy, source, chargeKg);
        }
    }
}
