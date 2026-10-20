using System;
using System.Globalization;

namespace Subterra.DevTools
{
    /// <summary>Typed access to the values of a STEP node. Numbers use the invariant culture.</summary>
    public static class StepArgs
    {
        /// <summary>Value of <paramref name="key"/>, trimmed and without one pair of surrounding double quotes.</summary>
        public static string Arg(this ConfigNode node, string key, string fallback = null)
        {
            string value = node.GetValue(key);
            if (value == null)
            {
                return fallback;
            }

            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
            }

            return value;
        }

        public static string RequireArg(this ConfigNode node, string key)
        {
            string value = node.Arg(key);
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException($"missing '{key}'");
            }

            return value;
        }

        public static double ArgDouble(this ConfigNode node, string key, double fallback)
        {
            string value = node.Arg(key);
            if (value == null)
            {
                return fallback;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                throw new ArgumentException($"'{key}' is not a number: {value}");
            }

            return result;
        }

        public static bool ArgBool(this ConfigNode node, string key, bool fallback)
        {
            string value = node.Arg(key);
            if (value == null)
            {
                return fallback;
            }

            if (!bool.TryParse(value, out bool result))
            {
                throw new ArgumentException($"'{key}' is not true/false: {value}");
            }

            return result;
        }
    }
}
