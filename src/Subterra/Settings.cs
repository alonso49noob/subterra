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
        public string RimMaterial = "texture";

        /// <summary>Round openings cut into the terrain itself with a smooth edge (true), or whole terrain triangles removed and covered by a rim (false).</summary>
        public bool TerrainClip = true;
        public bool Waypoints = true;
        public bool Debug;
        public float DigSpeed = 1;
        public bool Excavation = true;

        /// <summary>Cave water lifts, slows and crushes what goes in (sub-project 4).</summary>
        public bool WaterPhysics = true;

        /// <summary>Rover ramps beside skylights (off: their mouths cut long gaps in the ground; being fixed).</summary>
        public bool Ramps;

        /// <summary>The cheat menu's Set Position with a negative altitude (safety check off) carves a pocket the size of the vessel
        /// around it when it lands in solid rock.</summary>
        public bool CarveOnSetPosition = true;

        /// <summary>Size of that pocket: a multiple of the craft's own size (1 = just room for it), and the least radius and height it
        /// takes (m). Lucas: room for something bigger. The pocket grows up and out from under the craft. [Claude, revisar]</summary>
        public float PocketScale = 1;
        public float PocketMinRadius;
        public float PocketMinHeight;

        /// <summary>Share of the real weight of the rock the PresMat adds to the air and water (Lucas liked pressures some 1000 times
        /// under real life; the rock at full weight would swamp the rest). [Claude, revisar]</summary>
        public double RockPressureScale = 0.01;

        /// <summary>Boulders, rubble and slabs on the floors of caves and tunnels (Lucas: a very optional switch to take them out).</summary>
        public bool TunnelRocks = true;

        /// <summary>Plumes of ice over the vents of lakes on airless bodies (cryovolcanism: Lucas). [Claude, revisar]</summary>
        public bool CryovolcanoPlumes = true;

        /// <summary>Parts are crushed past their maxPressure in deep water, and by KSP's own overpressure check underground (Lucas:
        /// off, it was a pain to test and held players back).</summary>
        public bool WaterCrush;

        /// <summary>Layers below the surface one get the texture of their kind of rock (basalt, granite, ice...) instead of the
        /// body's own ground texture. [Claude, revisar]</summary>
        public bool LayerTextures = true;

        /// <summary>Below the datum the air's pressure grows going down and it takes the rock's temperature (KSP's curves clamp or
        /// wrap round there).</summary>
        public bool UndergroundAir = true;

        /// <summary>How the deep, hot rock looks: "glow" (it glows as hot rock does, dull red to white-hot: Lucas), "iridescent"
        /// (thin-film colours) or "none". [Claude, revisar]</summary>
        public string DeepRockLook = "glow";
        public int MaxTunnelSegments = 5000;
        public float VoidStrengthMPa = 150;

        /// <summary>Radar overlay (scanned ground, hints and detections) on the focused body in the map view and the Tracking Station, with its toolbar button.</summary>
        public bool RadarOverlay = true;
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
                settings.TerrainClip = ReadBool(n, "terrainClip", settings.TerrainClip);
                settings.Waypoints = ReadBool(n, "waypoints", settings.Waypoints);
                settings.Debug = ReadBool(n, "debug", settings.Debug);
                settings.DigSpeed = (float)Math.Max(0, Read(n, "digSpeed", settings.DigSpeed));
                settings.Excavation = ReadBool(n, "excavation", settings.Excavation);
                settings.WaterPhysics = ReadBool(n, "waterPhysics", settings.WaterPhysics);
                settings.Ramps = ReadBool(n, "ramps", settings.Ramps);
                settings.CarveOnSetPosition = ReadBool(n, "carveOnSetPosition", settings.CarveOnSetPosition);
                settings.PocketScale = (float)Math.Max(1, Math.Min(50, Read(n, "pocketScale", settings.PocketScale)));
                settings.PocketMinRadius = (float)Math.Max(0, Math.Min(2000, Read(n, "pocketMinRadius", settings.PocketMinRadius)));
                settings.PocketMinHeight = (float)Math.Max(0, Math.Min(2000, Read(n, "pocketMinHeight", settings.PocketMinHeight)));
                settings.RockPressureScale = Math.Max(0, Math.Min(1, Read(n, "rockPressureScale", settings.RockPressureScale)));
                settings.TunnelRocks = ReadBool(n, "tunnelRocks", settings.TunnelRocks);
                settings.CryovolcanoPlumes = ReadBool(n, "cryovolcanoPlumes", settings.CryovolcanoPlumes);
                settings.WaterCrush = ReadBool(n, "waterCrush", settings.WaterCrush);
                settings.LayerTextures = ReadBool(n, "layerTextures", settings.LayerTextures);
                settings.UndergroundAir = ReadBool(n, "undergroundAir", settings.UndergroundAir);
                string look = n.GetValue("deepRockLook")?.Trim().ToLowerInvariant();
                if (look == "glow" || look == "iridescent" || look == "none")
                {
                    settings.DeepRockLook = look;
                }
                settings.MaxTunnelSegments = (int)Math.Max(0, Read(n, "maxTunnelSegments", settings.MaxTunnelSegments));
                settings.VoidStrengthMPa = (float)Math.Max(1, Read(n, "voidStrengthMPa", settings.VoidStrengthMPa));
                settings.RadarOverlay = ReadBool(n, "radarOverlay", settings.RadarOverlay);
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
