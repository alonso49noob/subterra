using System;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Water
{
    /// <summary>A body's ocean under the ice (sub-project 4): where its ceiling is, and how deep a point is in it.</summary>
    public sealed class OceanShell
    {
        private readonly OceanSpec spec;
        private readonly double radius;

        public OceanShell(OceanSpec spec, double bodyRadius)
        {
            this.spec = spec;
            radius = bodyRadius;
        }

        public OceanSpec Spec => spec;

        /// <summary>
        /// Depth (m) of the ice's underside under the datum at <paramref name="latDeg"/>: thinnest at the poles (tidal
        /// heating is strongest there), thickest at the equator, smoothly between.
        /// </summary>
        public double CeilingDepth(double latDeg)
        {
            double s = Math.Sin(Math.Abs(latDeg) * Math.PI / 180);
            return spec.CeilingEquator + (spec.CeilingPole - spec.CeilingEquator) * s * s;
        }

        /// <summary>Radius of the ceiling at <paramref name="latDeg"/>.</summary>
        public double CeilingRadius(double latDeg) => radius - CeilingDepth(latDeg);

        /// <summary>Densities (kg/m³) of an ice ceiling and of a rock one (Tylo).</summary>
        public const double IceDensity = 920, RockDensity = 2700;

        /// <summary>
        /// Pressure (kPa) the ceiling's weight puts on the water under it at <paramref name="latDeg"/>: its thickness ·
        /// density · <paramref name="gravity"/> (Lucas: the pressure at the bottom counts the ice or rock above too). The
        /// water's own weight is added on top of this.
        /// </summary>
        public double OverburdenKPa(double latDeg, double gravity) =>
            CeilingDepth(latDeg) * (spec.CeilingRock ? RockDensity : IceDensity) * gravity / 1000;

        /// <summary>The water at <paramref name="bodyPos"/> (body frame), or null above the ceiling or under the floor.</summary>
        public WaterSample? At(Vec3d bodyPos)
        {
            double r = bodyPos.Length;
            if (r <= 0)
            {
                return null;
            }

            Vec3d dir = bodyPos / r;
            BodyFrame.ToLatLon(dir, out double lat, out _);
            double top = CeilingRadius(lat);
            double depth = top - r;
            if (depth < 0 || depth > spec.Thickness)
            {
                return null;
            }

            return new WaterSample(depth, spec.Density, dir.ToVec3(), spec.Colour);
        }
    }
}
