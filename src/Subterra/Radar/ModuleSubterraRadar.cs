using System;
using KSP.Localization;
using Subterra.Core.Radar;

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
    /// and, from task 18, the Zahori ground radar. This part of the module (task 13) starts and stops the
    /// instrument, draws its electricity and reports whether it could scan; <see cref="OrbitalScanner"/> (task 14)
    /// marks the coverage grid from it.
    ///
    /// Power: while <see cref="scanning"/> is on, the part draws <see cref="powerUse"/> EC/s from the vessel every
    /// physics step. If the electricity runs out, <see cref="scanning"/> stays on (decidido por Claude, revisar):
    /// the status reads "no power" and the scan resumes by itself when charge is back, so a night pass over the
    /// dark side does not make the player press Start again. Only loaded, unpacked vessels scan.
    /// </summary>
    public sealed class ModuleSubterraRadar : PartModule
    {
        public const string ModeOrbitalRadar = "orbitalRadar";
        public const string ModeGravimeter = "gravimeter";
        public const string ModeGroundRadar = "groundRadar";

        /// <summary>Fraction of a step's charge that must be delivered for the instrument to count as powered.</summary>
        private const double PoweredFraction = 0.99;

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

        [KSPField(isPersistant = true)] public bool scanning;
        [KSPField(isPersistant = true)] public bool highBand;

        /// <summary>
        /// UT up to which <see cref="OrbitalScanner"/> has walked this instrument's ground track (its progress marker;
        /// only the scanner and <see cref="StartScan"/> move it).
        /// </summary>
        [KSPField(isPersistant = true)] public double lastScanUT;

        /// <summary>The body <see cref="lastScanUT"/> belongs to: after a change of body the scanner starts afresh.</summary>
        [KSPField(isPersistant = true)] public string lastScanBody = "";

        [KSPField(guiActive = true, guiName = "#Subterra_Radar_Status")]
        public string uiStatus = "";

        private ScanBlock block = ScanBlock.None;

        /// <summary>Whether the last physics step produced a scan (scanning, powered, in the window).</summary>
        public bool IsScanningNow { get; private set; }

        /// <summary>The reason the last step could not scan; None while it can.</summary>
        public ScanBlock Block => block;

        public bool IsOrbitalMode => mode == ModeOrbitalRadar || mode == ModeGravimeter;

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
            }
            catch (Exception e)
            {
                scanning = false;
                IsScanningNow = false;
                Log.Error($"Radar on {vessel.GetDisplayName()} stopped: {e}");
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

                return maxSpeed > 0 && vessel.srfSpeed >= maxSpeed ? ScanBlock.TooFast : ScanBlock.None;
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
