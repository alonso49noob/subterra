namespace Subterra.Core.Radar
{
    public enum InstrumentKind
    {
        OrbitalRadar,
        Gravimeter,
        GroundRadar,
        Seismic,
    }

    /// <summary>Fixed characteristics of one survey instrument. Immutable.</summary>
    public sealed class Instrument
    {
        public InstrumentKind Kind { get; init; }

        public double FrequencyMHz { get; init; }

        public double DynamicRangeDb { get; init; }

        /// <summary>Smallest anomaly the gravimeter can tell from noise, in mGal.</summary>
        public double SensitivityMGal { get; init; }

        /// <summary>Half opening of the ground radar cone, in degrees.</summary>
        public double ConeHalfAngleDeg { get; init; }

        /// <summary>Useful altitude band for orbital instruments, in metres.</summary>
        public double AltitudeMin { get; init; }

        public double AltitudeBest { get; init; }

        public double AltitudeMax { get; init; }

        /// <summary>Eco, the orbital radar: 20 MHz, 50 dB, useful from 10 to 250 km, best at 30 km.</summary>
        public static Instrument Eco() => new Instrument
        {
            Kind = InstrumentKind.OrbitalRadar,
            FrequencyMHz = 20,
            DynamicRangeDb = 50,
            AltitudeMin = 10000,
            AltitudeBest = 30000,
            AltitudeMax = 250000,
        };

        /// <summary>Plomada, the gravimeter: sigma 0.05 mGal, useful from 10 to 200 km, best at 30 km.</summary>
        public static Instrument Plomada() => new Instrument
        {
            Kind = InstrumentKind.Gravimeter,
            SensitivityMGal = 0.05,
            AltitudeMin = 10000,
            AltitudeBest = 30000,
            AltitudeMax = 200000,
        };

        /// <summary>Zahori, the ground radar: 60 MHz (low band) or 500 MHz (high band), 70 dB, 40 degree cone.</summary>
        public static Instrument Zahori(bool highBand) => new Instrument
        {
            Kind = InstrumentKind.GroundRadar,
            FrequencyMHz = highBand ? 500 : 60,
            DynamicRangeDb = 70,
            ConeHalfAngleDeg = 40,
        };

        /// <summary>The seismic survey; its limits come from the shot geometry, not from the instrument.</summary>
        public static Instrument Seismic() => new Instrument { Kind = InstrumentKind.Seismic };
    }
}
