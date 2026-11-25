using System;
using System.Collections.Generic;
using Subterra.Core.Environment;
using Subterra.Core.Model;
using Subterra.Core.Numerics;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class DetectionModelTests
    {
        private static readonly LayerSpec RockLayer = new LayerSpec { Density = 3.0 };
        private static readonly LayerSpec IceLayer = new LayerSpec { Density = 1.0 };
        private static readonly LayerSpan[] Rock = { new LayerSpan(0, double.PositiveInfinity, RockLayer) };
        private static readonly LayerSpan[] Ice = { new LayerSpan(0, double.PositiveInfinity, IceLayer) };

        private const double R = 600000; // body radius for the test geometry

        private static readonly Vec3d Surface = new Vec3d(R, 0, 0);
        private static readonly Vec3d Down = new Vec3d(-1, 0, 0);

        private static SubsurfaceTarget Tube(double width, double height, double depth, double length = 1000,
            LayerSpec host = null, TargetKind kind = TargetKind.Void, Vec3d? position = null)
        {
            return new SubsurfaceTarget
            {
                Id = "t",
                Kind = kind,
                Position = position ?? new Vec3d(R - depth, 0, 0),
                Width = width,
                Height = height,
                Length = length,
                Depth = depth,
                DensityContrast = 3.0,
                Host = host ?? RockLayer,
            };
        }

        private static SubsurfaceTarget Column(TargetKind kind)
        {
            return Tube(double.PositiveInfinity, kind == TargetKind.Gas ? double.PositiveInfinity : 0, 50,
                length: double.PositiveInfinity, kind: kind);
        }

        private static Detection Eco(SubsurfaceTarget t, double altitude, LayerSpan[] spans)
        {
            return DetectionModel.Evaluate(Instrument.Eco(), ScanGeometry.Orbital(altitude), t, spans);
        }

        private static Detection Plomada(SubsurfaceTarget t, double altitude = 20000)
        {
            return DetectionModel.Evaluate(Instrument.Plomada(), ScanGeometry.Orbital(altitude), t, Rock);
        }

        private static Detection Gpr(SubsurfaceTarget t, Vec3d origin, Vec3d axis, bool high = false)
        {
            return DetectionModel.Evaluate(Instrument.Zahori(high), ScanGeometry.Ground(origin, axis), t, Rock);
        }

        private static IReadOnlyList<Vec3d> Spread(int n, double half)
        {
            var list = new List<Vec3d>();
            for (int i = 0; i < n; i++)
            {
                list.Add(new Vec3d(R, -half + 2 * half * i / (n - 1), 0));
            }

            return list;
        }

        private static Detection Seismic(SubsurfaceTarget t, IReadOnlyList<Vec3d> geo, Vec3d source, double charge = 8)
        {
            return DetectionModel.Evaluate(Instrument.Seismic(), ScanGeometry.SeismicShot(geo, source, charge), t, Rock);
        }

        // Eco

        [Fact]
        public void EcoSeesShallowWideTube()
        {
            Detection d = Eco(Tube(400, 40, 100), 30000, Rock);
            Assert.Equal(DetectionLevel.Hint, d.Level);
            Assert.Equal(948.36, d.Uncertainty, 2);
        }

        [Fact] public void EcoMissesDeepTube() => Assert.Equal(DetectionLevel.None, Eco(Tube(400, 40, 500), 30000, Rock).Level);

        [Fact] public void EcoMissesNarrowTube() => Assert.Equal(DetectionLevel.None, Eco(Tube(60, 40, 100), 100000, Rock).Level);

        [Fact]
        public void EcoSeesIceCaveDeep() =>
            Assert.Equal(DetectionLevel.Hint, Eco(Tube(400, 40, 2000, host: IceLayer), 30000, Ice).Level);

        [Fact]
        public void EcoDepthRange()
        {
            Detection d = Eco(Tube(400, 40, 100), 30000, Rock);
            Assert.Equal(70, d.DepthMin, 6);
            Assert.Equal(130, d.DepthMax, 6);
        }

        [Fact]
        public void EcoDepthRangeNeverGoesNegative()
        {
            Detection d = Eco(Tube(400, 40, 5), 30000, Rock);
            Assert.Equal(0, d.DepthMin, 9);
        }

        [Fact]
        public void EcoIgnoresGasAndBoundaries()
        {
            Assert.Equal(DetectionLevel.None, Eco(Tube(400, 40, 100, kind: TargetKind.Gas), 30000, Rock).Level);
            Assert.Equal(DetectionLevel.None, Eco(Tube(400, 40, 100, kind: TargetKind.LayerBoundary), 30000, Rock).Level);
        }

        [Fact]
        public void EcoWaterFollowsVoidRule() =>
            Assert.Equal(DetectionLevel.Hint, Eco(Tube(400, 40, 100, kind: TargetKind.Water), 30000, Rock).Level);

        [Fact]
        public void EcoOutsideAltitudeRangeSeesNothing()
        {
            Assert.Equal(DetectionLevel.None, Eco(Tube(400, 40, 100), 5000, Rock).Level);
            Assert.Equal(DetectionLevel.None, Eco(Tube(4000, 40, 100), 300000, Rock).Level);
        }

        [Fact]
        public void EcoHostNullUsesRockDefault()
        {
            var t = new SubsurfaceTarget
            {
                Kind = TargetKind.Void, Position = new Vec3d(R - 100, 0, 0), Width = 400, Height = 40, Length = 1000, Depth = 100, Host = null,
            };
            Assert.Equal(DetectionLevel.Hint, Eco(t, 30000, Rock).Level);
        }

        // Plomada

        [Fact]
        public void PlomadaSeesMegaTube() => Assert.Equal(DetectionLevel.Hint, Plomada(Tube(500, 100, 100, length: 5000)).Level);

        [Fact]
        public void PlomadaSeesGreatGallery()
        {
            Detection d = Plomada(Tube(250, 100, 90, length: 2000));
            Assert.Equal(DetectionLevel.Hint, d.Level);
            Assert.Equal(20090, d.Uncertainty, 6);
            Assert.Equal(0, d.DepthMin, 6);
            Assert.Equal(20180, d.DepthMax, 6);
        }

        [Fact] public void PlomadaMissesSmallTube() => Assert.Equal(DetectionLevel.None, Plomada(Tube(50, 50, 100, length: 5000)).Level);

        [Fact] public void PlomadaIgnoresGas() => Assert.Equal(DetectionLevel.None, Plomada(Tube(500, 100, 100, kind: TargetKind.Gas)).Level);

        [Fact]
        public void PlomadaUsesSphereForShortBodies()
        {
            // 250 m wide and 250 m long: sphere R = 125, about 4e-4 mGal at 20 km.
            Assert.Equal(DetectionLevel.None, Plomada(Tube(250, 250, 90, length: 250)).Level);
        }

        [Fact]
        public void PlomadaWaterUsesSphere()
        {
            // Same body as the great gallery but Water: the sphere rule applies, so it is not seen.
            Assert.Equal(DetectionLevel.None, Plomada(Tube(250, 100, 90, length: 2000, kind: TargetKind.Water)).Level);
        }

        [Fact]
        public void PlomadaOutsideAltitudeRangeSeesNothing() =>
            Assert.Equal(DetectionLevel.None, Plomada(Tube(500, 100, 100, length: 5000), 5000).Level);

        // Zahori

        [Fact]
        public void GprDetectsTubeBelow()
        {
            Detection d = Gpr(Tube(20, 5, 80), Surface, Down);
            Assert.Equal(DetectionLevel.Detected, d.Level);
            Assert.Equal(RadarPhysics.FresnelGround(60, 6, 80), d.Uncertainty, 9);
            double res = RadarPhysics.VerticalResolution(60, 6);
            Assert.Equal(80 - res, d.DepthMin, 9);
            Assert.Equal(80 + res, d.DepthMax, 9);
        }

        [Fact]
        public void GprMissesOutsideCone()
        {
            var t = Tube(20, 5, 80, position: new Vec3d(R - 80, 150, 0));
            Assert.Equal(DetectionLevel.None, Gpr(t, Surface, Down).Level);
        }

        [Fact]
        public void GprWallSeesSideChamber()
        {
            var t = Tube(30, 10, 20, position: new Vec3d(R, 60, 0));
            Assert.Equal(DetectionLevel.Detected, Gpr(t, Surface, new Vec3d(0, 1, 0)).Level);
        }

        [Fact]
        public void GprHorizontalShotUsesPathLengthForFresnel()
        {
            // Shallow target (Depth 5) but 60 m away along a horizontal shot: the zone is the 60 m one.
            var t = Tube(30, 10, 5, position: new Vec3d(R, 60, 0));
            Detection d = Gpr(t, Surface, new Vec3d(0, 1, 0));
            Assert.Equal(DetectionLevel.Detected, d.Level);
            Assert.Equal(RadarPhysics.FresnelGround(60, 6, 60), d.Uncertainty, 9);
            Assert.NotEqual(RadarPhysics.FresnelGround(60, 6, 5), d.Uncertainty);

            // Width 3 m passes the half-zone check for 5 m (1.1 m) but not for 60 m (3.9 m).
            var narrow = Tube(3, 10, 5, position: new Vec3d(R, 60, 0));
            Assert.Equal(DetectionLevel.None, Gpr(narrow, Surface, new Vec3d(0, 1, 0)).Level);
        }

        [Fact]
        public void GprIgnoresGas() =>
            Assert.Equal(DetectionLevel.None, Gpr(Tube(20, 5, 80, kind: TargetKind.Gas), Surface, Down).Level);

        [Fact]
        public void GprRangeLimitedByMaxDepth() =>
            Assert.Equal(DetectionLevel.None, Gpr(Tube(20, 5, 300), Surface, Down).Level);

        [Fact]
        public void GprHighBandHasShorterRange() =>
            Assert.Equal(DetectionLevel.None, Gpr(Tube(20, 5, 80), Surface, Down, high: true).Level);

        [Fact]
        public void GprSeesLayerBoundary() =>
            Assert.Equal(DetectionLevel.Detected, Gpr(Column(TargetKind.LayerBoundary), Surface, Down).Level);

        // Seismic

        [Fact]
        public void SeismicNeedsThreeGeophones() =>
            Assert.Equal(DetectionLevel.None, Seismic(Tube(20, 5, 40), Spread(2, 200), Surface).Level);

        [Fact]
        public void SeismicSourceTooFar() =>
            Assert.Equal(DetectionLevel.None, Seismic(Tube(20, 5, 40), Spread(5, 200), new Vec3d(R, 1500, 0)).Level);

        [Fact]
        public void SeismicSeesGasAndLayers()
        {
            Assert.Equal(DetectionLevel.Detected, Seismic(Column(TargetKind.Gas), Spread(5, 200), Surface).Level);
            Assert.Equal(DetectionLevel.Detected, Seismic(Column(TargetKind.LayerBoundary), Spread(5, 200), Surface).Level);
        }

        [Fact]
        public void SeismicUncertaintyIsResolution()
        {
            Detection d = Seismic(Tube(20, 5, 40), Spread(5, 200), Surface);
            Assert.Equal(DetectionLevel.Detected, d.Level);
            // L = 400, n = 5: spacing 100; max(50, 40 / 20) = 50
            Assert.Equal(50, d.Uncertainty, 9);
            Assert.Equal(0, d.DepthMin, 9);
            Assert.Equal(90, d.DepthMax, 9);
        }

        [Fact]
        public void SeismicBeyondReachSeesNothing() =>
            Assert.Equal(DetectionLevel.None, Seismic(Tube(20, 5, 150), Spread(5, 200), Surface).Level);

        [Fact]
        public void SeismicOffToTheSideSeesNothing()
        {
            // horizontal limit: L/2 + depth/2 = 220
            Assert.Equal(DetectionLevel.None, Seismic(Tube(20, 5, 40, position: new Vec3d(R - 40, 0, 300)), Spread(5, 200), Surface).Level);
            Assert.Equal(DetectionLevel.Detected, Seismic(Tube(20, 5, 40, position: new Vec3d(R - 40, 0, 200)), Spread(5, 200), Surface).Level);
        }

        // General

        [Fact]
        public void Deterministic()
        {
            var t = Tube(400, 40, 100);
            Detection a = Eco(t, 30000, Rock);
            Detection b = Eco(t, 30000, Rock);
            Assert.Equal(a, b);
        }

        [Fact]
        public void InfiniteColumnTargetsGiveFiniteNumbers()
        {
            foreach (TargetKind kind in new[] { TargetKind.LayerBoundary, TargetKind.Gas })
            {
                SubsurfaceTarget t = Column(kind);
                Detection seis = Seismic(t, Spread(5, 200), Surface);
                Assert.Equal(DetectionLevel.Detected, seis.Level);
                AssertFinite(seis);
                AssertFinite(Gpr(t, Surface, Down));
                Assert.Equal(DetectionLevel.None, Eco(t, 30000, Rock).Level);
                Assert.Equal(DetectionLevel.None, Plomada(t).Level);
            }

            Detection gpr = Gpr(Column(TargetKind.LayerBoundary), Surface, Down);
            Assert.Equal(DetectionLevel.Detected, gpr.Level);
            AssertFinite(gpr);
        }

        private static void AssertFinite(Detection d)
        {
            Assert.True(!double.IsNaN(d.Uncertainty) && !double.IsInfinity(d.Uncertainty));
            Assert.True(!double.IsNaN(d.DepthMin) && !double.IsInfinity(d.DepthMin));
            Assert.True(!double.IsNaN(d.DepthMax) && !double.IsInfinity(d.DepthMax));
        }

        [Fact]
        public void InstrumentFactoriesMatchConstraints()
        {
            Instrument eco = Instrument.Eco();
            Assert.Equal(20, eco.FrequencyMHz);
            Assert.Equal(50, eco.DynamicRangeDb);
            Assert.Equal(10000, eco.AltitudeMin);
            Assert.Equal(30000, eco.AltitudeBest);
            Assert.Equal(250000, eco.AltitudeMax);
            Assert.Equal(0.05, Instrument.Plomada().SensitivityMGal);
            Assert.Equal(10000, Instrument.Plomada().AltitudeMin);
            Assert.Equal(30000, Instrument.Plomada().AltitudeBest);
            Assert.Equal(200000, Instrument.Plomada().AltitudeMax);
            Assert.Equal(60, Instrument.Zahori(false).FrequencyMHz);
            Assert.Equal(500, Instrument.Zahori(true).FrequencyMHz);
            Assert.Equal(70, Instrument.Zahori(true).DynamicRangeDb);
            Assert.Equal(40, Instrument.Zahori(false).ConeHalfAngleDeg);
        }
    }
}
