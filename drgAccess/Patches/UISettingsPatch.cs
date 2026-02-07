using HarmonyLib;
using DRS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace drgAccess.Patches;

/// <summary>
/// Patches for settings menu accessibility.
/// NOTE: OnToggleVsync CANNOT be patched — its native detour crashes the game.
/// Vsync toggle is handled by the FocusTracker + generic Toggle detection instead.
/// </summary>
public static class UISettingsPatch
{
    /// <summary>
    /// Set by UIFormPatches when UISettingsForm becomes visible/hidden.
    /// Guards patches from firing during initialization and controls FocusTracker.
    /// </summary>
    internal static bool SettingsOpen;

    // === Settings tab changes (PageLeft/PageRight are declared on UISettingsForm) ===

    [HarmonyPatch(typeof(UISettingsForm), nameof(UISettingsForm.PageLeft))]
    public static class SettingsForm_PageLeft
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsForm __instance) => AnnounceCurrentTab(__instance);
    }

    [HarmonyPatch(typeof(UISettingsForm), nameof(UISettingsForm.PageRight))]
    public static class SettingsForm_PageRight
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsForm __instance) => AnnounceCurrentTab(__instance);
    }

    private static void AnnounceCurrentTab(UISettingsForm instance)
    {
        if (!SettingsOpen) return;
        try
        {
            var tabGroup = instance.GetComponentInChildren<UITabGroup>();
            if (tabGroup == null) return;
            var tabs = tabGroup.tabs;
            if (tabs == null) return;
            int index = tabGroup.CurrentIndex;
            if (index < 0 || index >= tabs.Count) return;
            var tab = tabs[index];
            if (tab?.text != null && !string.IsNullOrEmpty(tab.text.text))
                ScreenReader.Interrupt(CleanText(tab.text.text));
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UISettingsPatch.AnnounceCurrentTab error: {ex.Message}");
        }
    }

    // === Settings slider: announce label + value when changed ===

    [HarmonyPatch(typeof(UISettingsSlider), nameof(UISettingsSlider.SetValueText))]
    public static class SettingsSlider_SetValueText
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsSlider __instance)
        {
            try
            {
                var es = EventSystem.current;
                if (es == null || es.currentSelectedGameObject != __instance.gameObject)
                    return;

                string value = __instance.valueText != null ? __instance.valueText.text : null;
                if (string.IsNullOrEmpty(value))
                    return;

                string label = GetControlLabel(__instance.transform);
                string announcement = !string.IsNullOrEmpty(label)
                    ? $"{label}: {CleanText(value)}"
                    : CleanText(value);
                ScreenReader.Interrupt(announcement);
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UISettingsPatch.SetValueText error: {ex.Message}");
            }
        }
    }

    // === Gameplay page toggles ===

    [HarmonyPatch(typeof(UISettingsPageGameplay), nameof(UISettingsPageGameplay.OnToggleDamageNumberToggle))]
    public static class Gameplay_DamageNumber
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageGameplay __instance)
        {
            if (!SettingsOpen) return;
            AnnounceToggle(__instance.damageNumberToggle);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageGameplay), nameof(UISettingsPageGameplay.OnToggleStatusEffectNumberToggle))]
    public static class Gameplay_StatusEffect
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageGameplay __instance)
        {
            if (!SettingsOpen) return;
            AnnounceToggle(__instance.statusEffectNumberToggle);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageGameplay), nameof(UISettingsPageGameplay.OnToggleDmgVignette))]
    public static class Gameplay_DmgVignette
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageGameplay __instance)
        {
            if (!SettingsOpen) return;
            AnnounceToggle(__instance.dmgVignetteToggle);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageGameplay), nameof(UISettingsPageGameplay.OnToggleLoadingWaitForInput))]
    public static class Gameplay_LoadingWait
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageGameplay __instance)
        {
            if (!SettingsOpen) return;
            AnnounceToggle(__instance.loadingWaitForInputToggle);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageGameplay), nameof(UISettingsPageGameplay.OnToggleMenuTips))]
    public static class Gameplay_MenuTips
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageGameplay __instance)
        {
            if (!SettingsOpen) return;
            AnnounceToggle(__instance.menuTipsToggle);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageGameplay), nameof(UISettingsPageGameplay.OnToggleRumble))]
    public static class Gameplay_Rumble
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageGameplay __instance)
        {
            if (!SettingsOpen) return;
            AnnounceToggle(__instance.rumbleToggle);
        }
    }

    // NOTE: OnToggleVsync CANNOT be patched — crashes the game.
    // Vsync toggle state is announced via FocusTracker when navigating to it.

    // === Selectors ===

    [HarmonyPatch(typeof(UISettingsPageGameplay), nameof(UISettingsPageGameplay.UpdateSelectedElement))]
    public static class Gameplay_LanguageSelector
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageGameplay __instance)
        {
            if (!SettingsOpen) return;
            AnnounceSelector(__instance.currentSelectedLocale, __instance.languageSelector?.transform);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageVideo), nameof(UISettingsPageVideo.UpdateSelectedFullScreenMode))]
    public static class Video_FullscreenMode
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageVideo __instance)
        {
            if (!SettingsOpen) return;
            AnnounceSelector(null, __instance.fullscreenModeSelector?.transform);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageVideo), nameof(UISettingsPageVideo.UpdateSelectedResolution))]
    public static class Video_Resolution
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageVideo __instance)
        {
            if (!SettingsOpen) return;
            AnnounceSelector(__instance.currentSelectedResolution, __instance.resolutionSelector?.transform);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageVideo), nameof(UISettingsPageVideo.UpdateSelectedAntiAliasing))]
    public static class Video_AntiAliasing
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageVideo __instance)
        {
            if (!SettingsOpen) return;
            AnnounceSelector(null, __instance.antiAliasingSelector?.transform);
        }
    }

    [HarmonyPatch(typeof(UISettingsPageVideo), nameof(UISettingsPageVideo.UpdateSelectedDisplay))]
    public static class Video_Display
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsPageVideo __instance)
        {
            if (!SettingsOpen) return;
            AnnounceSelector(__instance.currentSelectedDisplay, __instance.displaySelector?.transform);
        }
    }

    // === Helpers ===

    private static void AnnounceToggle(Toggle toggle)
    {
        try
        {
            if (toggle == null) return;
            string stateText = toggle.isOn ? "On" : "Off";
            string label = GetControlLabel(toggle.transform);
            string msg = !string.IsNullOrEmpty(label)
                ? $"{label}: {stateText}"
                : stateText;
            ScreenReader.Interrupt(msg);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UISettingsPatch.AnnounceToggle error: {ex.Message}");
        }
    }

    private static void AnnounceSelector(TextMeshProUGUI displayText, Transform selectorTransform)
    {
        try
        {
            string value = displayText != null ? CleanText(displayText.text) : null;
            if (string.IsNullOrEmpty(value) && selectorTransform != null)
            {
                var texts = selectorTransform.GetComponentsInChildren<TextMeshProUGUI>();
                foreach (var t in texts)
                {
                    if (!string.IsNullOrEmpty(t.text))
                    {
                        value = CleanText(t.text);
                        break;
                    }
                }
            }

            string label = selectorTransform != null ? GetControlLabel(selectorTransform) : null;
            string msg = !string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value)
                ? $"{label}: {value}"
                : value ?? label ?? "";
            if (!string.IsNullOrEmpty(msg))
                ScreenReader.Interrupt(msg);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UISettingsPatch.AnnounceSelector error: {ex.Message}");
        }
    }

    internal static string GetControlLabel(Transform control)
    {
        try
        {
            // 1. Check control's own children for a label (Unity Toggle pattern:
            //    Toggle > Label(TMP), Background, Checkmark)
            for (int i = 0; i < control.childCount; i++)
            {
                var child = control.GetChild(i);
                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                    return CleanText(tmp.text);
            }

            // 2. Check direct siblings (same parent) for TMP
            var parent = control.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child == control) continue;

                    var tmp = child.GetComponent<TextMeshProUGUI>();
                    if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                        return CleanText(tmp.text);
                }
            }
        }
        catch { }
        return null;
    }

    internal static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        return text.Trim();
    }
}
