using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace drgAccess.Helpers
{
    /// <summary>
    /// Persistent mod configuration. Stores volume multipliers and detection settings.
    /// Saves next to the mod DLL, wherever it is installed.
    /// </summary>
    public static class ModConfig
    {
        private static readonly string ConfigFilePath =
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "drgAccess_settings.cfg");

        // --- Volume categories ---
        public const string WALL_NAVIGATION = "WallNavigation";
        public const string ENEMY_DETECTION = "EnemyDetection";
        public const string DROP_POD_BEACON = "DropPodBeacon";
        public const string SUPPLY_POD_BEACON = "SupplyPodBeacon";
        public const string HAZARD_WARNING = "HazardWarning";
        public const string COLLECTIBLES = "Collectibles";
        public const string FOOTSTEPS = "Footsteps";
        public const string BOSS_ATTACKS = "BossAttacks";

        public static readonly string[] VolumeCategories = new[]
        {
            WALL_NAVIGATION, ENEMY_DETECTION, DROP_POD_BEACON,
            SUPPLY_POD_BEACON, HAZARD_WARNING, COLLECTIBLES, FOOTSTEPS,
            BOSS_ATTACKS
        };

        // --- Toggle settings ---
        public const string FOOTSTEPS_ENABLED = "FootstepsEnabled";

        // --- Detection settings ---
        public const string ENEMY_RANGE = "EnemyRange";
        public const string HAZARD_RANGE = "HazardRange";
        public const string COLLECTIBLE_RANGE = "CollectibleRange";
        public const string WALL_RANGE = "WallRange";
        public const string MAX_HAZARD_CHANNELS = "MaxHazardChannels";

        /// <summary>
        /// Metadata for each setting: default, min, max, step, display name, format.
        /// </summary>
        public struct SettingDef
        {
            public float Default, Min, Max, Step;
            public string DisplayName, Unit;
        }

        public static readonly Dictionary<string, SettingDef> SettingDefs = new()
        {
            { FOOTSTEPS_ENABLED,   new SettingDef { Default = 1f,   Min = 0f,  Max = 1f,   Step = 1f,  DisplayName = "Footsteps",               Unit = "toggle" } },
            { ENEMY_RANGE,         new SettingDef { Default = 35f,  Min = 10f, Max = 60f,  Step = 5f,  DisplayName = "Enemy Detection Range",   Unit = "m" } },
            { HAZARD_RANGE,        new SettingDef { Default = 25f,  Min = 10f, Max = 50f,  Step = 5f,  DisplayName = "Hazard Warning Range",    Unit = "m" } },
            { COLLECTIBLE_RANGE,   new SettingDef { Default = 1.0f, Min = 0.5f, Max = 2.0f, Step = 0.1f, DisplayName = "Collectible Range",     Unit = "x" } },
            { WALL_RANGE,          new SettingDef { Default = 12f,  Min = 5f,  Max = 25f,  Step = 1f,  DisplayName = "Wall Detection Range",    Unit = "m" } },
            { MAX_HAZARD_CHANNELS, new SettingDef { Default = 3f,   Min = 1f,  Max = 5f,   Step = 1f,  DisplayName = "Max Hazard Warnings",     Unit = "" } }
        };

        public static readonly string[] AllSettingKeys = new[]
        {
            FOOTSTEPS_ENABLED, ENEMY_RANGE, HAZARD_RANGE, COLLECTIBLE_RANGE, WALL_RANGE, MAX_HAZARD_CHANNELS
        };

        // --- State ---
        private static readonly Dictionary<string, float> volumes = new();
        private static readonly Dictionary<string, float> settings = new();

        /// <summary>
        /// Game master volume (0-1), updated at runtime via AudioMastering patch.
        /// </summary>
        public static float GameMasterVolume { get; set; } = 1.0f;

        // --- Load / Save ---

        public static void Load()
        {
            // Set defaults
            foreach (var cat in VolumeCategories)
                volumes[cat] = 1.0f;
            foreach (var kvp in SettingDefs)
                settings[kvp.Key] = kvp.Value.Default;

            if (!File.Exists(ConfigFilePath))
            {
                Plugin.Log.LogInfo("[ModConfig] No config file found, using defaults");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(ConfigFilePath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                    var parts = trimmed.Split('=');
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim();
                    if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        continue;

                    if (volumes.ContainsKey(key))
                        volumes[key] = Math.Max(0f, Math.Min(1f, value));
                    else if (SettingDefs.TryGetValue(key, out var def))
                        settings[key] = Math.Max(def.Min, Math.Min(def.Max, value));
                }
                Plugin.Log.LogInfo($"[ModConfig] Loaded settings from {ConfigFilePath}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModConfig] Load error: {e.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# DRG Access Mod Settings",
                    "",
                    "# Volume (0.00 = muted, 1.00 = full)"
                };

                foreach (var cat in VolumeCategories)
                {
                    float vol = volumes.ContainsKey(cat) ? volumes[cat] : 1.0f;
                    lines.Add($"{cat}={vol.ToString("F2", CultureInfo.InvariantCulture)}");
                }

                lines.Add("");
                lines.Add("# Detection settings");

                foreach (var key in AllSettingKeys)
                {
                    float val = settings.ContainsKey(key) ? settings[key] : SettingDefs[key].Default;
                    lines.Add($"{key}={val.ToString("F1", CultureInfo.InvariantCulture)}");
                }

                File.WriteAllLines(ConfigFilePath, lines);
                Plugin.Log.LogInfo($"[ModConfig] Saved settings to {ConfigFilePath}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModConfig] Save error: {e.Message}");
            }
        }

        // --- Volume access ---

        /// <summary>
        /// Effective volume: category multiplier * game master volume.
        /// </summary>
        public static float GetVolume(string category)
        {
            float catVol = volumes.TryGetValue(category, out float vol) ? vol : 1.0f;
            return catVol * GameMasterVolume;
        }

        // --- Settings access ---

        public static float GetSetting(string key)
        {
            if (settings.TryGetValue(key, out float val)) return val;
            if (SettingDefs.TryGetValue(key, out var def)) return def.Default;
            return 0f;
        }

        public static int GetSettingInt(string key) => (int)Math.Round(GetSetting(key));

        public static bool GetBool(string key) => GetSetting(key) >= 0.5f;

        // --- Snapshot for settings menu editing ---

        public static Dictionary<string, float> GetSnapshot()
        {
            var snap = new Dictionary<string, float>(volumes);
            foreach (var kvp in settings)
                snap[kvp.Key] = kvp.Value;
            return snap;
        }

        public static void ApplySnapshot(Dictionary<string, float> snapshot)
        {
            foreach (var kvp in snapshot)
            {
                if (volumes.ContainsKey(kvp.Key))
                    volumes[kvp.Key] = Math.Max(0f, Math.Min(1f, kvp.Value));
                else if (SettingDefs.TryGetValue(kvp.Key, out var def))
                    settings[kvp.Key] = Math.Max(def.Min, Math.Min(def.Max, kvp.Value));
            }
        }
    }
}
