using System;
using Subterra.Core.Model;

namespace Subterra.Core.Radar
{
    /// <summary>Radar properties of a rock layer: attenuation and relative permittivity, derived from density unless set.</summary>
    public static class Materials
    {
        // decidido por Claude, revisar: thresholds and values from the spec (§4.2–4.3).
        private const double IceDensityLimit = 1.2;
        private const double RockDensityLimit = 2.2;
        private const double IceAttenuation60 = 0.01;
        private const double LooseAttenuation60 = 0.1;
        private const double RockAttenuation60 = 0.2;
        private const double IcePermittivity = 4;
        private const double RockPermittivity = 6;
        private const double ReferenceFrequencyMHz = 60;

        /// <summary>Attenuation in dB/m at 60 MHz: the layer's own value, else deduced from its density.</summary>
        public static double Attenuation60(LayerSpec layer)
        {
            if (layer == null)
            {
                throw new ArgumentNullException(nameof(layer));
            }

            if (layer.Attenuation.HasValue)
            {
                return layer.Attenuation.Value;
            }

            if (layer.Density < IceDensityLimit)
            {
                return IceAttenuation60;
            }

            return layer.Density < RockDensityLimit ? LooseAttenuation60 : RockAttenuation60;
        }

        /// <summary>Attenuation in dB/m at the given frequency (MHz): linear in frequency, α(f) = α60 · f / 60.</summary>
        public static double Attenuation(LayerSpec layer, double frequencyMHz)
        {
            return Attenuation60(layer) * frequencyMHz / ReferenceFrequencyMHz;
        }

        /// <summary>Relative permittivity: 4 for light (icy) material, 6 for the rest.</summary>
        public static double Permittivity(LayerSpec layer)
        {
            if (layer == null)
            {
                throw new ArgumentNullException(nameof(layer));
            }

            return layer.Density < IceDensityLimit ? IcePermittivity : RockPermittivity;
        }
    }
}
