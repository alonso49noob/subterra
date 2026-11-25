using System;

namespace Subterra.Core.Water
{
    /// <summary>
    /// Forces and pressure in still water, in SI units (metres, kilograms, newtons, kilopascals). No KSP types: the
    /// plugin turns them into forces on parts, and sub-project 5 may use them for its own seas.
    /// </summary>
    public static class WaterPhysics
    {
        /// <summary>Fresh water (kg/m³).</summary>
        public const double FreshDensity = 1000;

        /// <summary>Drag coefficient of a part moving through water (a bluff body, like KSP's own ocean drag).</summary>
        public const double DragCoefficient = 1.0;

        /// <summary>
        /// The share of a part's box volume that displaces water: parts are not solid boxes (tanks are round, frames are
        /// open). [Claude, revisar]
        /// </summary>
        public const double FillFactor = 0.5;

        /// <summary>Pressure <paramref name="depth"/> metres under the surface (kPa): the air on the surface plus ρ·g·h.</summary>
        public static double PressureKPa(double depth, double density, double gravity, double surfaceKPa) =>
            surfaceKPa + Math.Max(0, depth) * density * gravity / 1000;

        /// <summary>
        /// How much of a body of height <paramref name="height"/> is under water when its centre is
        /// <paramref name="depth"/> metres under the surface (negative: above it): 0 to 1, linear across its height.
        /// </summary>
        public static double SubmergedFraction(double depth, double height)
        {
            if (height <= 0)
            {
                return depth > 0 ? 1 : 0;
            }

            return Math.Max(0, Math.Min(1, depth / height + 0.5));
        }

        /// <summary>Upward force (N) on <paramref name="volume"/> m³ under water: ρ·g·V.</summary>
        public static double Buoyancy(double volume, double density, double gravity) => density * gravity * Math.Max(0, volume);

        /// <summary>Drag force magnitude (N) at <paramref name="speed"/> m/s on a face of <paramref name="area"/> m²: ½·ρ·v²·Cd·A.</summary>
        public static double Drag(double speed, double area, double density) => 0.5 * density * speed * speed * DragCoefficient * Math.Max(0, area);
    }
}
