using System.Collections.Generic;
using System.Linq;
using Subterra.Core.Environment;
using Subterra.Core.Geo;
using Subterra.Core.Model;
using Subterra.Core.Procedural;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class CellCoverageIndexTests
    {
        private const double MunRadius = 200000;
        private const double Margin = 11000;

        private static readonly CellGrid Grid = new CellGrid(MunRadius);
        private static readonly CellCoverageIndex Index = new CellCoverageIndex(Grid, Margin);

        private static int CellAt(double lat, double lon) => Grid.CellOf(BodyFrame.Direction(lat, lon)).Index(Grid.N);

        [Fact]
        public void NothingChangesWithoutCoverage()
        {
            var last = new long[Index.CellCount];
            Assert.Empty(Index.Changed(new CoverageGrid(), null, last));
            Assert.Empty(Index.Changed(null, null, last));
        }

        [Theory]
        [InlineData(10, 20)]
        [InlineData(49.398, 64.9603)]
        [InlineData(-30, 179.95)]
        [InlineData(15, -179.95)]
        [InlineData(89.9, 0)]
        [InlineData(-89.9, 120)]
        [InlineData(0, 45)]
        public void AMarkedPointChangesItsCellOnce(double lat, double lon)
        {
            var echo = new CoverageGrid();
            var last = new long[Index.CellCount];
            echo.MarkSwath(lat, lon, 500, MunRadius, 12000);

            List<int> changed = Index.Changed(echo, null, last);
            Assert.Contains(CellAt(lat, lon), changed);
            Assert.Empty(Index.Changed(echo, null, last));
        }

        [Fact]
        public void FarCellsDoNotChange()
        {
            var gravity = new CoverageGrid();
            var last = new long[Index.CellCount];
            gravity.MarkSwath(10, 20, 2000, MunRadius, 20000);

            List<int> changed = Index.Changed(null, gravity, last);
            Assert.DoesNotContain(CellAt(-10, -160), changed);
            Assert.DoesNotContain(CellAt(10, 60), changed);
            // A small mark touches the cell and at most its neighbours.
            Assert.InRange(changed.Count, 1, 9);
        }

        [Fact]
        public void ALowerScanChangesTheCellAgain()
        {
            var echo = new CoverageGrid();
            var last = new long[Index.CellCount];
            echo.MarkSwath(10, 20, 2000, MunRadius, 50000);
            Index.Changed(echo, null, last);

            echo.MarkSwath(10, 20, 2000, MunRadius, 20000);
            Assert.Contains(CellAt(10, 20), Index.Changed(echo, null, last));
        }

        [Fact]
        public void EitherInstrumentChangesTheCell()
        {
            var echo = new CoverageGrid();
            var gravity = new CoverageGrid();
            var last = new long[Index.CellCount];
            echo.MarkSwath(10, 20, 2000, MunRadius, 20000);
            Index.Changed(echo, gravity, last);

            gravity.MarkSwath(10, 20, 2000, MunRadius, 20000);
            Assert.Contains(CellAt(10, 20), Index.Changed(echo, gravity, last));
        }

        [Fact]
        public void FullCoverageChangesEveryCell()
        {
            var echo = new CoverageGrid();
            var last = new long[Index.CellCount];
            echo.MarkSwath(0, 0, 3.2 * MunRadius, MunRadius, 30000);

            Assert.Equal(Index.CellCount, Index.Changed(echo, null, last).Count);
        }

        [Fact]
        public void EveryPointIsInsideTheBoxOfItsCell()
        {
            // One mark per sample point: the cell holding it must see it.
            for (double lat = -88; lat <= 88; lat += 7.3)
            {
                for (double lon = -179; lon < 180; lon += 11.7)
                {
                    var echo = new CoverageGrid();
                    echo.MarkSwath(lat, lon, 0, MunRadius, 30000);
                    int cell = CellAt(lat, lon);
                    Assert.True(Index.Signature(cell, echo, null) > 0, $"lat {lat} lon {lon} cell {cell}");
                }
            }
        }
    }

    public class OrbitalSurveyTests
    {
        private const double MunRadius = 200000;

        private static readonly LayerSpec Basalt = new LayerSpec { Density = 3.1 };
        private static readonly IReadOnlyList<LayerSpan> Rock = new[] { new LayerSpan(0, double.PositiveInfinity, Basalt) };

        private static SubsurfaceTarget Tube(double width, double length, double lat = 10, double lon = 20, string id = "N#0") => new SubsurfaceTarget
        {
            Id = id,
            Kind = TargetKind.Void,
            NetworkId = id.Split('#')[0],
            PrimitiveIndex = 0,
            Lat = lat,
            Lon = lon,
            Width = width,
            Height = 40,
            Length = length,
            Depth = 60,
            DensityContrast = 3.1,
            Host = Basalt,
        };

        private static OrbitalSurvey Survey() => new OrbitalSurvey(Instrument.Eco(), Instrument.Plomada());

        [Fact]
        public void UncoveredTargetsAreNotEvaluated()
        {
            var results = new List<SurveyResult>();
            Assert.False(Survey().Evaluate(Tube(300, 2000), new CoverageGrid(), null, t => Rock, results));
            Assert.Empty(results);
        }

        [Fact]
        public void EcoHintsAWideTubeUnderItsSwath()
        {
            var echo = new CoverageGrid();
            echo.MarkSwath(10, 20, 1200, MunRadius, 12000);
            var results = new List<SurveyResult>();

            Assert.True(Survey().Evaluate(Tube(300, 400), echo, null, t => Rock, results));
            SurveyResult r = Assert.Single(results);
            Assert.Equal(InstrumentKind.OrbitalRadar, r.Kind);
            Assert.Equal(DetectionLevel.Hint, r.Detection.Level);
        }

        [Fact]
        public void EcoMissesANarrowTube()
        {
            var echo = new CoverageGrid();
            echo.MarkSwath(10, 20, 1200, MunRadius, 12000);
            var results = new List<SurveyResult>();

            Assert.True(Survey().Evaluate(Tube(50, 400), echo, null, t => Rock, results));
            Assert.Empty(results);
        }

        [Fact]
        public void PlomadaAt20KmHintsA250MetreGalleryButNotA100MetreTube()
        {
            var gravity = new CoverageGrid();
            gravity.MarkSwath(10, 20, 20000, MunRadius, 20000);
            var survey = Survey();
            var results = new List<SurveyResult>();

            survey.Evaluate(Tube(250, 2000, id: "wide#0"), null, gravity, null, results);
            survey.Evaluate(Tube(100, 2000, id: "narrow#0"), null, gravity, null, results);

            SurveyResult r = Assert.Single(results);
            Assert.Equal("wide#0", r.Target.Id);
            Assert.Equal(InstrumentKind.Gravimeter, r.Kind);
            Assert.Equal(DetectionLevel.Hint, r.Detection.Level);
        }

        [Fact]
        public void ATargetIsEvaluatedAgainOnlyWhenItsCoverageImproves()
        {
            var echo = new CoverageGrid();
            echo.MarkSwath(10, 20, 1200, MunRadius, 40000);
            var survey = Survey();
            var results = new List<SurveyResult>();
            SubsurfaceTarget tube = Tube(400, 400);
            int spanCalls = 0;
            IReadOnlyList<LayerSpan> Spans(SubsurfaceTarget t)
            {
                spanCalls++;
                return Rock;
            }

            Assert.True(survey.Evaluate(tube, echo, null, Spans, results));
            Assert.False(survey.Evaluate(tube, echo, null, Spans, results));
            Assert.Equal(1, spanCalls);

            echo.MarkSwath(10, 20, 1200, MunRadius, 12000);
            Assert.True(survey.Evaluate(tube, echo, null, Spans, results));
            Assert.Equal(2, results.Count);
            Assert.True(results[1].Detection.Uncertainty < results[0].Detection.Uncertainty);

            survey.Forget();
            Assert.True(survey.Evaluate(tube, echo, null, Spans, results));
        }

        [Fact]
        public void AScanAtTheWindowEdgeStillCounts()
        {
            // 10 km encodes to a byte that decodes to about 9986 m, below Eco's 10 km minimum.
            byte code = CoverageGrid.Encode(10000);
            Assert.True(CoverageGrid.Decode(code) < 10000);
            Assert.Equal(10000, OrbitalSurvey.UsableAltitude(Instrument.Eco(), code));

            var echo = new CoverageGrid();
            echo.MarkSwath(10, 20, 1200, MunRadius, 10000);
            var results = new List<SurveyResult>();
            Survey().Evaluate(Tube(300, 400), echo, null, t => Rock, results);
            Assert.Equal(DetectionLevel.Hint, Assert.Single(results).Detection.Level);
        }

        [Fact]
        public void BothInstrumentsReport()
        {
            var echo = new CoverageGrid();
            var gravity = new CoverageGrid();
            echo.MarkSwath(10, 20, 1200, MunRadius, 12000);
            gravity.MarkSwath(10, 20, 12000, MunRadius, 12000);
            var results = new List<SurveyResult>();

            Survey().Evaluate(Tube(300, 2000), echo, gravity, t => Rock, results);
            Assert.Equal(new[] { InstrumentKind.OrbitalRadar, InstrumentKind.Gravimeter }, results.Select(r => r.Kind).ToArray());
        }
    }
}
