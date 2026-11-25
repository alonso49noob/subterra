using System;
using System.Collections.Generic;
using Subterra.Core.Environment;

namespace Subterra.Core.Radar
{
    /// <summary>One detection found by an orbital survey: the target, the instrument that saw it and what it saw.</summary>
    public readonly struct SurveyResult
    {
        public SurveyResult(SubsurfaceTarget target, InstrumentKind kind, Detection detection)
        {
            Target = target;
            Kind = kind;
            Detection = detection;
        }

        public SubsurfaceTarget Target { get; }

        public InstrumentKind Kind { get; }

        public Detection Detection { get; }
    }

    /// <summary>
    /// Evaluates targets against the orbital coverage of a body: the radar sounder (Eco) with the coverage grid of
    /// <see cref="InstrumentKind.OrbitalRadar"/>, the gravimeter (Plomada) with that of
    /// <see cref="InstrumentKind.Gravimeter"/>, each from the best (lowest) altitude its grid holds at the target's
    /// latitude and longitude. It remembers the coverage byte each target was last evaluated with, per instrument,
    /// and evaluates a target again only when its byte improved, so a cell can be looked at again cheaply.
    /// Pure and not thread-safe: one user at a time (the lazy detector runs one job per body).
    /// </summary>
    public sealed class OrbitalSurvey
    {
        private static readonly IReadOnlyList<LayerSpan> NoSpans = new LayerSpan[0];

        // Per target: the echo byte in the low 8 bits and the gravity byte in the next 8.
        private readonly Dictionary<SubsurfaceTarget, int> seen = new Dictionary<SubsurfaceTarget, int>();

        public OrbitalSurvey(Instrument eco, Instrument plomada)
        {
            Eco = eco ?? throw new ArgumentNullException(nameof(eco));
            Plomada = plomada ?? throw new ArgumentNullException(nameof(plomada));
        }

        public Instrument Eco { get; }

        public Instrument Plomada { get; }

        /// <summary>Targets remembered so far.</summary>
        public int Remembered => seen.Count;

        /// <summary>
        /// The altitude an orbital instrument is taken to have scanned from, given a coverage byte: the decoded
        /// altitude kept inside the instrument's window. Coverage is only marked from inside the window, but the
        /// byte rounds the altitude by up to 1.4 %, so a scan at the window's edge may decode just outside it
        /// (decidido por Claude, revisar).
        /// </summary>
        public static double UsableAltitude(Instrument instrument, byte code)
        {
            double altitude = CoverageGrid.Decode(code);
            return Math.Max(instrument.AltitudeMin, Math.Min(instrument.AltitudeMax, altitude));
        }

        /// <summary>
        /// Evaluates the target with every instrument whose coverage at the target improved since the last call for
        /// it, adding the detections above None to <paramref name="results"/>. The grids may be null (no coverage).
        /// <paramref name="spansOf"/> gives the layer spans over the target (only asked for the radar sounder; null
        /// is taken as no layers). Returns true if some instrument evaluated it.
        /// </summary>
        public bool Evaluate(SubsurfaceTarget target, CoverageGrid echo, CoverageGrid gravity,
            Func<SubsurfaceTarget, IReadOnlyList<LayerSpan>> spansOf, List<SurveyResult> results)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (double.IsNaN(target.Lat) || double.IsNaN(target.Lon) || double.IsInfinity(target.Lat) || double.IsInfinity(target.Lon))
            {
                return false;
            }

            int row = CoverageGrid.RowOf(target.Lat), column = CoverageGrid.ColumnOf(target.Lon);
            byte echoCode = echo != null ? echo.GetCell(row, column) : (byte)0;
            byte gravityCode = gravity != null ? gravity.GetCell(row, column) : (byte)0;
            seen.TryGetValue(target, out int previous);
            byte echoBefore = (byte)(previous & 0xFF), gravityBefore = (byte)((previous >> 8) & 0xFF);
            bool echoNew = echoCode > echoBefore, gravityNew = gravityCode > gravityBefore;
            if (!echoNew && !gravityNew)
            {
                return false;
            }

            seen[target] = Math.Max(echoCode, echoBefore) | (Math.Max(gravityCode, gravityBefore) << 8);
            if (echoNew)
            {
                IReadOnlyList<LayerSpan> spans = spansOf?.Invoke(target) ?? NoSpans;
                Add(results, target, InstrumentKind.OrbitalRadar,
                    DetectionModel.Evaluate(Eco, ScanGeometry.Orbital(UsableAltitude(Eco, echoCode)), target, spans));
            }

            if (gravityNew)
            {
                Add(results, target, InstrumentKind.Gravimeter,
                    DetectionModel.Evaluate(Plomada, ScanGeometry.Orbital(UsableAltitude(Plomada, gravityCode)), target, NoSpans));
            }

            return true;
        }

        /// <summary>Forgets every target (after a reload, when the detections were reset).</summary>
        public void Forget() => seen.Clear();

        private static void Add(List<SurveyResult> results, SubsurfaceTarget target, InstrumentKind kind, Detection detection)
        {
            if (detection.Level != DetectionLevel.None)
            {
                results.Add(new SurveyResult(target, kind, detection));
            }
        }
    }
}
