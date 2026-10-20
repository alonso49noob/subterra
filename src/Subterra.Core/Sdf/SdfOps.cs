using System;

namespace Subterra.Core.Sdf
{
    public static class SdfOps
    {
        /// <summary>
        /// Approximate signed distance to an axis-aligned ellipse with radii (rx, ry) (Quilez' k0·(k0−1)/k1
        /// bound): close to the true distance near the curve, unlike plain (|p/r| − 1)·min(r).
        /// </summary>
        public static float Ellipse(float x, float y, float rx, float ry)
        {
            float k0 = (float)Math.Sqrt(x / rx * (x / rx) + y / ry * (y / ry));
            float k1 = (float)Math.Sqrt(x / (rx * rx) * (x / (rx * rx)) + y / (ry * ry) * (y / (ry * ry)));
            return k1 > 0 ? k0 * (k0 - 1f) / k1 : -Math.Min(rx, ry);
        }

        /// <summary>Polynomial smooth minimum; k is the blend width (0 = plain min).</summary>
        public static float SmoothMin(float a, float b, float k)
        {
            if (k <= 0)
            {
                return Math.Min(a, b);
            }

            float h = Math.Max(k - Math.Abs(a - b), 0f) / k;
            return Math.Min(a, b) - h * h * k * 0.25f;
        }
    }
}
