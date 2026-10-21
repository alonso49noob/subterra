using System;
using System.Collections.Generic;
using System.Globalization;

namespace Subterra
{
    /// <summary>User settings from <c>SUBTERRA_SETTINGS</c> (GameData/Subterra/Settings.cfg).</summary>
    public sealed class Settings
    {
        private static Settings current;

        /// <summary>The settings, read from the game database the first time they are needed.</summary>
        public static Settings Current
        {
            get
            {
                if (current == null)
                {
                    Load();
                }

                return current;
            }
        }

        public int MemoryCapMB = 300;
        public float MainThreadBudgetMs = 2;
        public float ColliderRadius = 150;
        public float VisualRange = 4000;
        public float CapRange = 8000;
        public float CapMinDistance = 1500;

        /// <summary>Skylight rims: "terrain" (the body's own terrain material and colours) or "texture" (a textured approximation).</summary>
        public string RimMaterial = "terrain";
        public bool Waypoints = true;
        public bool Debug;
        public float DigSpeed = 1;
        public bool Excavation = true;
        public int MaxTunnelSegments = 5000;
        public float VoidStrengthMPa = 150;
        public HashSet<string> DisabledBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DisabledPatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Load()
        {
            var settings = new Settings();
            ConfigNode[] nodes = GameDatabase.Instance != null ? GameDatabase.Instance.GetConfigNodes("SUBTERRA_SETTINGS") : new ConfigNode[0];
            if (nodes.Length > 0)
            {
                ConfigNode n = nodes[0];
                settings.MemoryCapMB = (int)Read(n, "memoryCapMB", settings.MemoryCapMB);
                settings.MainThreadBudgetMs = (float)Read(n, "mainThreadBudgetMs", settings.MainThreadBudgetMs);
                settings.ColliderRadius = (float)Read(n, "colliderRadius", settings.ColliderRadius);
                settings.VisualRange = (float)Read(n, "visualRange", settings.VisualRange);
                settings.CapRange = (float)Read(n, "capRange", settings.CapRange);
                settings.CapMinDistance = (float)Read(n, "capMinDistance", settings.CapMinDistance);
                settings.RimMaterial = (n.GetValue("rimMaterial") ?? settings.RimMaterial).Trim().ToLowerInvariant();
                settings.Waypoints = ReadBool(n, "waypoints", settings.Waypoints);
                settings.Debug = ReadBool(n, "debug", settings.Debug);
                settings.DigSpeed = (float)Math.Max(0, Read(n, "digSpeed", settings.DigSpeed));
                settings.Excavation = ReadBool(n, "excavation", settings.Excavation);
                settings.MaxTunnelSegments = (int)Math.Max(0, Read(n, "maxTunnelSegments", settings.MaxTunnelSegments));
                settings.VoidStrengthMPa = (float)Math.Max(1, Read(n, "voidStrengthMPa", settings.VoidStrengthMPa));
                foreach (string body in Split(n.GetValue("disabledBodies")))
                {
                    settings.DisabledBodies.Add(body);
                }

                foreach (string patch in Split(n.GetValue("disabledPatches")))
                {
                    settings.DisabledPatches.Add(patch);
                }
            }

            current = settings;
        }

        private static double Read(ConfigNode n, string key, double fallback)
        {
            string text = n.GetValue(key);
            if (text == null)
            {
                return fallback;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            Log.Warn($"Settings.cfg: '{key}' is not a number: {text}");
            return fallback;
        }

        private static bool ReadBool(ConfigNode n, string key, bool fallback)
        {
            string text = n.GetValue(key);
            if (text == null)
            {
                return fallback;
            }

            if (bool.TryParse(text.Trim(), out bool value))
            {
                return value;
            }

            Log.Warn($"Settings.cfg: '{key}' is not true/false: {text}");
            return fallback;
        }

        private static IEnumerable<string> Split(string list)
        {
            if (string.IsNullOrEmpty(list))
            {
                yield break;
            }

            foreach (string part in list.Split(','))
            {
                string item = part.Trim();
                if (item.Length > 0)
                {
                    yield return item;
                }
            }
        }
    }
}
