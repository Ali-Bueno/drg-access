using System;
using System.Collections.Generic;
using System.IO;

namespace drgAccess.Helpers;

/// <summary>
/// Loads mod UI strings from external localization/*.txt files.
/// Detects the game's current locale and loads the matching translation file.
/// Falls back to English if the current locale file is missing or a key is not found.
/// Users can edit any .txt file to customize translations.
/// </summary>
public static class ModLocalization
{
    private static Dictionary<string, string> currentStrings;
    private static Dictionary<string, string> englishStrings;
    private static string currentLocale = "en";
    private static string langDir;
    // Deferred locale re-check: SelectedLocale isn't ready during Plugin.Load()
    private static bool startupCheckDone;
    private static float startupCheckUntil;

    /// <summary>
    /// Initialize localization. Call once from Plugin.Load().
    /// </summary>
    public static void Init()
    {
        try
        {
            // localization/ folder lives next to the mod DLL
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            langDir = Path.Combine(Path.GetDirectoryName(dllPath), "localization");

            // Always load English as fallback
            englishStrings = LoadFile(Path.Combine(langDir, "en.txt"));

            // Detect game locale
            currentLocale = DetectLocale();
            Plugin.Log?.LogInfo($"[ModLocalization] Detected locale: {currentLocale}");

            if (currentLocale == "en" || string.IsNullOrEmpty(currentLocale))
            {
                currentStrings = englishStrings;
            }
            else
            {
                string path = Path.Combine(langDir, $"{currentLocale}.txt");
                if (File.Exists(path))
                    currentStrings = LoadFile(path);
                else
                    currentStrings = englishStrings;
            }

            Plugin.Log?.LogInfo($"[ModLocalization] Loaded {currentStrings.Count} strings for locale '{currentLocale}'");

            // SelectedLocale often isn't ready during Plugin.Load(), so re-check
            // on each Get() call for the first 15 seconds after startup.
            startupCheckDone = false;
            startupCheckUntil = UnityEngine.Time.realtimeSinceStartup + 15f;
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"[ModLocalization] Init error: {e.Message}");
            englishStrings ??= new Dictionary<string, string>();
            currentStrings = englishStrings;
        }
    }

    /// <summary>
    /// Get a localized string by key. Falls back to English, then to the key itself.
    /// </summary>
    public static string Get(string key)
    {
        // During startup, SelectedLocale may not be ready yet.
        // Re-check locale on each Get() call until it stabilizes or time expires.
        if (!startupCheckDone)
        {
            if (UnityEngine.Time.realtimeSinceStartup < startupCheckUntil)
            {
                string detected = DetectLocale();
                if (detected != currentLocale)
                {
                    Plugin.Log?.LogInfo($"[ModLocalization] Startup re-check: locale changed from '{currentLocale}' to '{detected}'");
                    currentLocale = detected;
                    ReloadCurrentLocale();
                    startupCheckDone = true;
                }
            }
            else
            {
                startupCheckDone = true;
            }
        }

        if (currentStrings != null && currentStrings.TryGetValue(key, out string val))
            return val;
        if (englishStrings != null && englishStrings.TryGetValue(key, out string enVal))
            return enVal;
        return key;
    }

    /// <summary>
    /// Get a localized string with format arguments.
    /// Example: Get("hp_format", current, max) → "HP: 85 / 120"
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        string template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    /// <summary>
    /// Re-detect locale (call if player changes language mid-session).
    /// </summary>
    public static void RefreshLocale()
    {
        try
        {
            string newLocale = DetectLocale();
            if (newLocale == currentLocale) return;

            currentLocale = newLocale;
            ReloadCurrentLocale();
            Plugin.Log?.LogInfo($"[ModLocalization] Locale changed to '{currentLocale}', {currentStrings.Count} strings loaded");
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"[ModLocalization] RefreshLocale error: {e.Message}");
        }
    }

    /// <summary>
    /// Reload the translation file for the current locale.
    /// </summary>
    private static void ReloadCurrentLocale()
    {
        if (currentLocale == "en" || string.IsNullOrEmpty(currentLocale))
        {
            currentStrings = englishStrings;
        }
        else
        {
            string path = Path.Combine(langDir, $"{currentLocale}.txt");
            currentStrings = File.Exists(path) ? LoadFile(path) : englishStrings;
        }
    }

    private static string DetectLocale()
    {
        try
        {
            var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            if (locale != null)
            {
                string code = locale.Identifier.Code;
                if (!string.IsNullOrEmpty(code))
                    return code.ToLowerInvariant();
            }
        }
        catch { }
        return "en";
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        var dict = new Dictionary<string, string>();
        if (!File.Exists(path)) return dict;

        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("#")) continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                string key = trimmed.Substring(0, eq).Trim();
                string value = trimmed.Substring(eq + 1);
                dict[key] = value;
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"[ModLocalization] Error loading {path}: {e.Message}");
        }

        return dict;
    }
}
