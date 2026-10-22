using System;
using System.Collections.Generic;
using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Radar
{
    /// <summary>Pure rules that decide what each instrument sees of a target.</summary>
    public static class DetectionModel
    {
        // R3: a target without host uses the Materials defaults for density 2.5 (rock).
        private static readonly LayerSpec DefaultHost = new LayerSpec { Density = 2.5 };

        // decidido por Claude, revisar: a seismic shot needs its source this close to the geophone centroid (m).
        private const double MaxSourceDistance = 1000;

        private const int MinGeophones = 3;

        public static Detection Evaluate(Instrument inst, ScanGeometry g, SubsurfaceTarget t, IReadOnlyList<LayerSpan> spans)
        {
            if (inst == null)
            {
                throw new ArgumentNullException(nameof(inst));
            }

            if (t == null)
            {
                throw new ArgumentNullException(nameof(t));
            }

            switch (inst.Kind)
            {
                case InstrumentKind.OrbitalRadar:
                    return g.Kind == ScanKind.Orbital ? EvaluateEco(inst, g, t, spans) : Detection.None;
                case InstrumentKind.Gravimeter:
                    return g.Kind == ScanKind.Orbital ? EvaluatePlomada(inst, g, t) : Detection.None;
                case InstrumentKind.GroundRadar:
                    return g.Kind == ScanKind.Ground ? EvaluateGpr(inst, g, t, spans) : Detection.None;
                case InstrumentKind.Seismic:
                    return g.Kind == ScanKind.SeismicShot ? EvaluateSeismic(g, t) : Detection.None;
                default:
                    return Detection.None;
            }
        }

        // Voids, sealed chambers and water share the hole rules for radar and seismic.
        private static bool IsHole(TargetKind kind) =>
            kind == TargetKind.Void || kind == TargetKind.Sealed || kind == TargetKind.Water;

        private static double Eps(SubsurfaceTarget t) => Materials.Permittivity(t.Host ?? DefaultHost);

        private static Detection EvaluateEco(Instrument inst, ScanGeometry g, SubsurfaceTarget t, IReadOnlyList<LayerSpan> spans)
        {
            // decidido por Claude, revisar: outside the useful altitude band the radar sees nothing.
            if (!IsHole(t.Kind) || g.Altitude < inst.AltitudeMin || g.Altitude > inst.AltitudeMax)
            {
                return Detection.None;
            }

            double f = inst.FrequencyMHz;
            double res = RadarPhysics.VerticalResolution(f, Eps(t));
            double fresnel = RadarPhysics.FresnelOrbital(f, g.Altitude);
            if (t.Depth > RadarPhysics.MaxDepth(spans, f, inst.DynamicRangeDb)
                || t.Height < 2 * res
                || t.Width < fresnel / 3)
            {
                return Detection.None;
            }

            double half = Math.Max(4 * res, 0.3 * t.Depth);
            return new Detection(DetectionLevel.Hint, fresnel, Math.Max(0, t.Depth - half), t.Depth + half);
        }

        private static Detection EvaluatePlomada(Instrument inst, ScanGeometry g, SubsurfaceTarget t)
        {
            // decidido por Claude, revisar: layer boundaries (infinite planes) are not modelled by the gravimeter.
            if (t.Kind == TargetKind.Gas || t.Kind == TargetKind.LayerBoundary
                || g.Altitude < inst.AltitudeMin || g.Altitude > inst.AltitudeMax)
            {
                return Detection.None;
            }

            double z = t.Depth + g.Altitude;
            double radius = t.Width / 2;
            double dg = t.Kind != TargetKind.Water && t.Length >= 2 * t.Width
                ? GravityPhysics.CylinderMGal(t.DensityContrast, radius, z)
                : GravityPhysics.SphereMGal(t.DensityContrast, radius, z);
            if (!(dg >= inst.SensitivityMGal))
            {
                return Detection.None;
            }

            return new Detection(DetectionLevel.Hint, z, 0, t.Depth + z);
        }

        private static Detection EvaluateGpr(Instrument inst, ScanGeometry g, SubsurfaceTarget t, IReadOnlyList<LayerSpan> spans)
        {
            if (t.Kind == TargetKind.Gas)
            {
                return Detection.None;
            }

            Vec3d toTarget = t.Position - g.Origin;
            double distance = toTarget.Length;
            double axisLength = g.Axis.Length;
            if (distance > 0)
            {
                if (!(axisLength > 0))
                {
                    return Detection.None;
                }

                double cos = Vec3d.Dot(toTarget, g.Axis) / (distance * axisLength);
                if (Math.Acos(Math.Max(-1, Math.Min(1, cos))) * 180 / Math.PI > inst.ConeHalfAngleDeg)
                {
                    return Detection.None;
                }
            }

            double f = inst.FrequencyMHz;
            if (distance > RadarPhysics.MaxDepth(spans, f, inst.DynamicRangeDb))
            {
                return Detection.None;
            }

            double eps = Eps(t);
            double res = RadarPhysics.VerticalResolution(f, eps);
            double fresnel = RadarPhysics.FresnelGround(f, eps, t.Depth);

            // Layer boundaries are planes: no size requirement (their sizes may be infinite).
            if (t.Kind != TargetKind.LayerBoundary && (t.Height < 2 * res || t.Width < fresnel / 2))
            {
                return Detection.None;
            }

            return new Detection(DetectionLevel.Detected, fresnel, Math.Max(0, t.Depth - res), t.Depth + res);
        }

        private static Detection EvaluateSeismic(ScanGeometry g, SubsurfaceTarget t)
        {
            IReadOnlyList<Vec3d> geo = g.Geophones;
            int n = geo.Count;
            if (n < MinGeophones)
            {
                return Detection.None;
            }

            Vec3d centroid = Vec3d.Zero;
            for (int i = 0; i < n; i++)
            {
                centroid += geo[i];
            }

            centroid = centroid / n;
            if (Vec3d.Distance(g.Source, centroid) > MaxSourceDistance)
            {
                return Detection.None;
            }

            double spread = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    spread = Math.Max(spread, Vec3d.Distance(geo[i], geo[j]));
                }
            }

            if (t.Depth > SeismicPhysics.Reach(spread, g.ChargeKg))
            {
                return Detection.None;
            }

            // Horizontal distance: project onto the plane perpendicular to the radial direction at the centroid.
            Vec3d offset = t.Position - centroid;
            Vec3d up = centroid.Normalized;
            Vec3d horizontal = offset - up * Vec3d.Dot(offset, up);
            if (horizontal.Length > spread / 2 + t.Depth / 2)
            {
                return Detection.None;
            }

            double uncertainty = SeismicPhysics.Resolution(spread / (n - 1), t.Depth);
            return new Detection(DetectionLevel.Detected, uncertainty, Math.Max(0, t.Depth - uncertainty), t.Depth + uncertainty);
        }
    }
}
