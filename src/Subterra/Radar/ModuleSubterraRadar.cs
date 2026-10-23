using System;
using KSP.Localization;
using Subterra.Core.Geo;
using Subterra.Core.Numerics;
using Subterra.Core.Radar;
using UnityEngine;

namespace Subterra
{
    /// <summary>Why an instrument cannot produce a scan right now (None: it can).</summary>
    public enum ScanBlock
    {
        None,
        NoPower,
        Landed,
        OutOfAltitude,
        NotLanded,
        TooFast,
    }

    /// <summary>
    /// The subsurface radar instrument on a part: SR-E "Eco" (orbital radar sounder), SG-P "Plomada" (gravimeter)
    /// and SG-Z "Zahori" (ground radar). The module starts and stops the instrument, draws its electricity and
    /// reports whether it could scan; <see cref="OrbitalScanner"/> marks the coverage grid from the orbital modes.
    /// The ground radar scans here: every <see cref="GroundSampleSeconds"/> it samples what lies in its cone
    /// (<see cref="GroundScan"/>) and puts the detections into the store.
    ///
    /// Power: while <see cref="scanning"/> is on, the part draws <see cref="powerUse"/> EC/s from the vessel every
    /// physics step. If the electricity runs out, <see cref="scanning"/> stays on (decidido por Claude, revisar):
    /// the status reads "no power" and the scan resumes by itself when charge is back, so a night pass over the
    /// dark side does not make the player press Start again. Charge is drawn here, on loaded vessels (packed on rails
    /// too). Unloaded vessels draw nothing: <see cref="OrbitalScanner"/> keeps scanning in the background only if the
    /// instrument was on and powered when the vessel was saved (<see cref="poweredWhenSaved"/>, spec 4.3).
    /// </summary>
    public sealed class ModuleSubterraRadar : PartModule
    {
        public const string ModeOrbitalRadar = "orbitalRadar";
        public const string ModeGravimeter = "gravimeter";
        public const string ModeGroundRadar = "groundRadar";

        /// <summary>Fraction of a step's charge that must be delivered for the instrument to count as powered.</summary>
        private const double PoweredFraction = 0.99;

        /// <summary>Game seconds between two samples of the ground radar (spec 4.3 c).</summary>
        public const double GroundSampleSeconds = 0.5;

        /// <summary>orbitalRadar, gravimeter or groundRadar.</summary>
        [KSPField] public string mode = ModeOrbitalRadar;

        [KSPField] public float frequencyMHz;

        /// <summary>Ground radar only: the high band; <see cref="frequencyMHz"/> is the low band.</summary>
        [KSPField] public float frequencyHighMHz;

        [KSPField] public float dynamicRangeDb;
        [KSPField] public float sensitivityMGal;
        [KSPField] public float coneHalfAngle;

        /// <summary>Useful altitude window above sea level (the datum), in metres; orbital instruments only.</summary>
        [KSPField] public float altitudeMin;

        [KSPField] public float altitudeBest;
        [KSPField] public float altitudeMax;

        /// <summary>Electric charge per second while scanning.</summary>
        [KSPField] public float powerUse = 1f;

        /// <summary>Ground radar only: the vessel must move slower than this (m/s) to scan.</summary>
        [KSPField] public float maxSpeed;

        [KSPField] public string experimentId = "";

        /// <summary>
        /// Ground radar only: the direction it looks at, in the part's own axes. Default +Z: out of the face that is
        /// mounted, as KSP turns a surface-attached part so that its attach node (direction -Z in the cfg) faces the
        /// parent. Mounted under a rover it looks down, mounted on a side it looks sideways.
        /// </summary>
        [KSPField] public Vector3 scanAxis = new Vector3(0, 0, 1);

        [KSPField(isPersistant = true)] public bool scanning;
        [KSPField(isPersistant = true)] public bool highBand;

        /// <summary>
        /// UT up to which <see cref="OrbitalScanner"/> has walked this instrument's ground track (its progress marker;
        /// only the scanner and <see cref="StartScan"/> move it).
        /// </summary>
        [KSPField(isPersistant = true)] public double lastScanUT;

        /// <summary>
        /// Whether the last step had electricity (false only after a "no power" step). Saved with the vessel, so an
        /// unloaded vessel scans in the background only if it was powered when it was unloaded (spec 4.3).
        /// </summary>
        [KSPField(isPersistant = true)] public bool poweredWhenSaved = true;

        /// <summary>The body <see cref="lastScanUT"/> belongs to: after a change of body the scanner starts afresh.</summary>
        [KSPField(isPersistant = true)] public string lastScanBody = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Radar_Status")]
        public string uiStatus = "";

        private ScanBlock block = ScanBlock.None;

        // Ground radar: the next game time (UT) a sample is due.
        private double nextGroundSample;
        private readonly System.Collections.Generic.List<GroundFinding> pendingFindings = new System.Collections.Generic.List<GroundFinding>();

        /// <summary>Whether the last physics step produced a scan (scanning, powered, in the window).</summary>
        public bool IsScanningNow { get; private set; }

        /// <summary>The reason the last step could not scan; None while it can.</summary>
        public ScanBlock Block => block;

        public bool IsOrbitalMode => mode == ModeOrbitalRadar || mode == ModeGravimeter;

        /// <summary>The ground radar's look direction in world space (unit length).</summary>
        public Vector3 ScanAxisWorld => part.transform.TransformDirection(scanAxis).normalized;

        [KSPEvent(guiActive = true, guiName = "#Subterra_Radar_Start")]
        public void StartScan()
        {
            scanning = true;
            // The orbital scanner walks the ground track from lastScanUT; a fresh start must not back-fill the past.
            lastScanUT = Planetarium.GetUniversalTime();
            lastScanBody = vessel != null && vessel.mainBody != null ? vessel.mainBody.bodyName : "";
            Refresh(false);
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Radar_Stop")]
        public void StopScan()
        {
            scanning = false;
            Refresh(false);
            UpdateEvents();
        }

        [KSPEvent(guiActive = true, guiName = "#Subterra_Radar_Band")]
        public void ToggleBand()
        {
            if (mode != ModeGroundRadar)
            {
                return;
            }

            highBand = !highBand;
        }

        /// <summary>The instrument as configured on this part (so a patched cfg balances it).</summary>
        public Instrument BuildInstrument()
        {
            switch (mode)
            {
                case ModeOrbitalRadar:
                    return new Instrument
                    {
                        Kind = InstrumentKind.OrbitalRadar,
                        FrequencyMHz = frequencyMHz,
                        DynamicRangeDb = dynamicRangeDb,
                        AltitudeMin = altitudeMin,
                        AltitudeBest = altitudeBest,
                        AltitudeMax = altitudeMax,
                    };
                case ModeGravimeter:
                    return new Instrument
                    {
                        Kind = InstrumentKind.Gravimeter,
                        SensitivityMGal = sensitivityMGal,
                        AltitudeMin = altitudeMin,
                        AltitudeBest = altitudeBest,
                        AltitudeMax = altitudeMax,
                    };
                case ModeGroundRadar:
                    return new Instrument
                    {
                        Kind = InstrumentKind.GroundRadar,
                        FrequencyMHz = highBand ? frequencyHighMHz : frequencyMHz,
                        DynamicRangeDb = dynamicRangeDb,
                        ConeHalfAngleDeg = coneHalfAngle,
                    };
                default:
                    throw new InvalidOperationException("unknown radar mode '" + mode + "' on " + part.name);
            }
        }

        /// <summary>
        /// Whether the instrument could scan now: electricity, and the situation it needs (orbital modes: not on the
        /// ground or the sea, altitude above sea level inside [altitudeMin, altitudeMax]; ground radar: landed and
        /// slower than maxSpeed). On false, reason is the localized status text. Does not draw any charge.
        /// </summary>
        public bool CanScan(out string reason)
        {
            ScanBlock b = Evaluate(HasCharge());
            reason = b == ScanBlock.None ? "" : Localizer.Format(StatusKey(b));
            return b == ScanBlock.None;
        }

        public override void OnStart(StartState state)
        {
            UpdateEvents();
            Refresh(false);
        }

        public override string GetInfo()
        {
            switch (mode)
            {
                case ModeGroundRadar:
                    return Localizer.Format("#Subterra_Radar_InfoGround", frequencyMHz.ToString("F0"), frequencyHighMHz.ToString("F0"),
                        maxSpeed.ToString("F1"), powerUse.ToString("F1"));
                default:
                    return Localizer.Format("#Subterra_Radar_InfoOrbital", (altitudeMin / 1000).ToString("F0"), (altitudeMax / 1000).ToString("F0"),
                        (altitudeBest / 1000).ToString("F0"), powerUse.ToString("F1"));
            }
        }

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                return;
            }

            try
            {
                Refresh(true);
                if (mode == ModeGroundRadar && IsScanningNow)
                {
                    GroundTick();
                }
            }
            catch (Exception e)
            {
                scanning = false;
                IsScanningNow = false;
                Log.Error($"Radar on {vessel.GetDisplayName()} stopped: {e}");
            }
        }

        // Ground radar: a sample every GroundSampleSeconds of game time while it can scan.
        private void GroundTick()
        {
            double now = Planetarium.GetUniversalTime();
            // A marker further ahead than one interval comes from a game time that has gone back (a quickload).
            if (now < nextGroundSample && nextGroundSample - now <= GroundSampleSeconds)
            {
                return;
            }

            nextGroundSample = now + GroundSampleSeconds;
            RadarScenario scenario = RadarScenario.Instance;
            if (scenario == null || !BodyRegistry.Instance.TryGet(vessel.mainBody, out BodyRuntime runtime))
            {
                return;
            }

            using (SubterraClock.Measure("GroundScan"))
            {
                Vec3d origin = Frames.ToBody(vessel.mainBody, part.transform.position);
                Vec3d axis = Frames.DirectionToBody(vessel.mainBody, ScanAxisWorld);
                BodyFrame.ToLatLon(origin, out double lat, out double lon);
                Instrument instrument = BuildInstrument();
                if (!GroundScan.Prepare(runtime, instrument, origin))
                {
                    // The targets around are being built on a worker; the next sample finds them ready.
                    return;
                }

                var seen = GroundScan.Sample(runtime, instrument, origin, axis, lat, lon);
                // The messages go out in the next Update: the sample and the message in one frame cost more than the budget.
                pendingFindings.AddRange(GroundScan.Apply(scenario.Detections, vessel.mainBody.bodyName, seen, origin, lat, lon));
            }
        }

        // Shows the findings of the last samples (one message per network found, never one per primitive).
        private void Update()
        {
            if (pendingFindings.Count == 0)
            {
                return;
            }

            using (SubterraClock.Measure("GroundScan"))
            {
                foreach (GroundFinding f in pendingFindings)
                {
                    // A fixed English part for the logs and the in-game tests (the test copy may run in any language).
                    string text = Localizer.Format("#Subterra_Radar_GprHit", f.Distance.ToString("F0"), f.Bearing.ToString("F0"));
                    Log.Info($"Radar: Zahori hollow at {f.Distance:F0} m, bearing {f.Bearing:F0} deg ({f.NetworkId}): \"{text}\"");
                    ScreenMessages.PostScreenMessage(text, 6f, ScreenMessageStyle.UPPER_CENTER);
                }

                pendingFindings.Clear();
            }
        }

        // One step: draws the charge if scanning (when draw is set), works out the block and the status line.
        private void Refresh(bool draw)
        {
            IsScanningNow = false;
            if (vessel == null)
            {
                return;
            }

            if (!scanning)
            {
                block = ScanBlock.None;
                uiStatus = Localizer.Format("#Subterra_Radar_Status_Idle");
                return;
            }

            bool powered;
            if (draw)
            {
                double need = powerUse * TimeWarp.fixedDeltaTime;
                double got = need > 0 ? part.RequestResource("ElectricCharge", need) : 0;
                powered = need <= 0 || got >= need * PoweredFraction;
            }
            else
            {
                powered = HasCharge();
            }

            block = Evaluate(powered);
            poweredWhenSaved = block != ScanBlock.NoPower;
            if (block == ScanBlock.None)
            {
                IsScanningNow = true;
                uiStatus = Localizer.Format("#Subterra_Radar_Status_Scanning");
            }
            else
            {
                uiStatus = Localizer.Format(StatusKey(block));
            }
        }

        private ScanBlock Evaluate(bool powered)
        {
            if (!powered)
            {
                return ScanBlock.NoPower;
            }

            if (mode == ModeGroundRadar)
            {
                if (!vessel.Landed)
                {
                    return ScanBlock.NotLanded;
                }

                // decidido por Claude, revisar: maxSpeed = 0 means "nothing is slow enough" (ruling R5: the in-game test
                // sets it to 0 to get the too-fast status without driving), not "no limit".
                return vessel.srfSpeed >= maxSpeed ? ScanBlock.TooFast : ScanBlock.None;
            }

            if (vessel.Landed || vessel.Splashed)
            {
                return ScanBlock.Landed;
            }

            double altitude = vessel.altitude;
            return altitude < altitudeMin || altitude > altitudeMax ? ScanBlock.OutOfAltitude : ScanBlock.None;
        }

        private bool HasCharge()
        {
            if (vessel == null)
            {
                return false;
            }

            vessel.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out double amount, out double max);
            return amount > 0;
        }

        /// <summary>The localization key of a block's status text (also used by the in-game tests).</summary>
        public static string StatusKey(ScanBlock b)
        {
            switch (b)
            {
                case ScanBlock.NoPower: return "#Subterra_Radar_Status_NoPower";
                case ScanBlock.Landed: return "#Subterra_Radar_Status_Landed";
                case ScanBlock.OutOfAltitude: return "#Subterra_Radar_Status_OutOfAltitude";
                case ScanBlock.NotLanded: return "#Subterra_Radar_Status_NotLanded";
                case ScanBlock.TooFast: return "#Subterra_Radar_Status_TooFast";
                default: return "#Subterra_Radar_Status_Scanning";
            }
        }

        private void UpdateEvents()
        {
            Events[nameof(StartScan)].active = !scanning;
            Events[nameof(StopScan)].active = scanning;
            Events[nameof(ToggleBand)].active = mode == ModeGroundRadar;
        }
    }
}
