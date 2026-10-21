using System;
using Subterra.Core.Model;
using Subterra.Core.Numerics;

namespace Subterra.Core.Excavation
{
    /// <summary>
    /// Pressurised gas a drill can strike on entering a layer that has some (<see cref="LayerSpec.GasChance"/>). Fixed
    /// per body, layer and place (cells of <see cref="CellDegrees"/>°): the same hole always finds the same pockets.
    /// </summary>
    public static class GasPocket
    {
        /// <summary>Size of the places that share a pocket (degrees of latitude and longitude).</summary>
        public const double CellDegrees = 0.01;

        /// <summary>Units of gas in a pocket (decidido por Claude, revisar).</summary>
        public const double Units = 400;

        /// <summary>True if a hole at (<paramref name="lat"/>, <paramref name="lon"/>) finds gas in <paramref name="layer"/>.</summary>
        public static bool Hit(string body, double lat, double lon, LayerSpec layer)
        {
            if (layer == null || layer.GasChance <= 0 || string.IsNullOrEmpty(layer.GasResource))
            {
                return false;
            }

            ulong seed = Hash.Combine(Hash.Of(body ?? ""), Hash.Of(layer.Name ?? ""),
                unchecked((ulong)(long)Math.Floor(lat / CellDegrees)), unchecked((ulong)(long)Math.Floor(lon / CellDegrees)));
            return new Rng(seed).NextDouble() < layer.GasChance;
        }
    }
}
