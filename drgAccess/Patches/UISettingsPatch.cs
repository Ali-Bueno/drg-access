using HarmonyLib;
using DRS.UI;
using Assets.Scripts.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using drgAccess.Helpers;

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
                ScreenReader.Interrupt(TextHelper.CleanText(tab.text.text));
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

                // If focus just changed to this slider, SettingsFocusTracker already
                // announced "label: value" — skip to avoid interrupting it.
                if (UnityEngine.Time.frameCount <= SettingsFocusTracker.LastSliderFocusFrame + 1)
                    return;

                string value = GetSliderValue(__instance);
                if (string.IsNullOrEmpty(value))
                    return;

                // Only announce the value (left/right changes) — the label was
                // already announced when the slider received focus.
                ScreenReader.Interrupt(value);
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

    // === Selectors (generic: handles all StepSelectorBase value changes in settings) ===
    // NOTE: SetIndex can't be patched — IncreaseIndex/DecreaseIndex call it natively
    // in IL2CPP, bypassing the managed wrapper. Patch the entry points instead.

    [HarmonyPatch(typeof(StepSelectorBase), nameof(StepSelectorBase.IncreaseIndex))]
    public static class StepSelector_IncreaseIndex
    {
        [HarmonyPostfix]
        public static void Postfix(StepSelectorBase __instance) => AnnounceSelectorValue(__instance);
    }

    [HarmonyPatch(typeof(StepSelectorBase), nameof(StepSelectorBase.DecreaseIndex))]
    public static class StepSelector_DecreaseIndex
    {
        [HarmonyPostfix]
        public static void Postfix(StepSelectorBase __instance) => AnnounceSelectorValue(__instance);
    }

    private static void AnnounceSelectorValue(StepSelectorBase selector)
    {
        if (!SettingsOpen) return;
        try
        {
            // Find label first, then find value skipping the label TMP
            string label = GetControlLabel(selector.transform);
            string value = GetSelectorValue(selector, label);
            string msg = !string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value)
                ? $"{label}: {value}"
                : value ?? label ?? "";
            if (!string.IsNullOrEmpty(msg))
                ScreenReader.Interrupt(msg);

            // Check if locale changed (handles language selector changes)
            ModLocalization.RefreshLocale();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"AnnounceSelectorValue error: {ex.Message}");
        }
    }

    // === Tab group changes (handles gear inventory tabs and other non-settings tabs) ===

    private static int _lastTabAnnouncedFrame = -1;

    [HarmonyPatch(typeof(UITabGroup), nameof(UITabGroup.SetActiveTab), new System.Type[] { typeof(UITab) })]
    public static class UITabGroup_SetActiveTab_Tab
    {
        [HarmonyPostfix]
        public static void Postfix(UITabGroup __instance, UITab uiTab)
        {
            if (SettingsOpen) return;
            if (!UIFormPatches.GearInventoryOpen) return;
            // Suppress during form init (default tab set on open/startup)
            if (Time.frameCount <= UIFormPatches.GearInventoryOpenFrame + 5) return;
            if (Time.frameCount == _lastTabAnnouncedFrame) return;
            try
            {
                if (uiTab?.text != null && !string.IsNullOrEmpty(uiTab.text.text))
                {
                    _lastTabAnnouncedFrame = Time.frameCount;
                    UIButtonPatch.QueueUntilTime = Time.unscaledTime + 0.5f;
                    ScreenReader.Interrupt(TextHelper.CleanText(uiTab.text.text));
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UITabGroup_SetActiveTab error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(UITabGroup), nameof(UITabGroup.SetActiveTab), new System.Type[] { typeof(int) })]
    public static class UITabGroup_SetActiveTab_Index
    {
        [HarmonyPostfix]
        public static void Postfix(UITabGroup __instance, int index)
        {
            if (SettingsOpen) return;
            if (!UIFormPatches.GearInventoryOpen) return;
            if (Time.frameCount <= UIFormPatches.GearInventoryOpenFrame + 5) return;
            if (Time.frameCount == _lastTabAnnouncedFrame) return;
            try
            {
                var tabs = __instance.tabs;
                if (tabs == null || index < 0 || index >= tabs.Count) return;
                var tab = tabs[index];
                if (tab?.text != null && !string.IsNullOrEmpty(tab.text.text))
                {
                    _lastTabAnnouncedFrame = Time.frameCount;
                    UIButtonPatch.QueueUntilTime = Time.unscaledTime + 0.5f;
                    ScreenReader.Interrupt(TextHelper.CleanText(tab.text.text));
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UITabGroup_SetActiveTab_Index error: {ex.Message}");
            }
        }
    }

    // === Helpers ===

    private static void AnnounceToggle(Toggle toggle)
    {
        try
        {
            if (toggle == null) return;
            string stateText = toggle.isOn ? ModLocalization.Get("ui_on") : ModLocalization.Get("ui_off");
            string label = GetControlLabel(toggle.transform);
            string msg = !string.IsNullOrEmpty(label)
                ? $"{label}: {stateText}"
                : stateText;
            // Prevent FocusTracker from double-announcing this toggle change
            SettingsFocusTracker.LastToggleAnnouncedFrame = Time.frameCount;
            ScreenReader.Interrupt(msg);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UISettingsPatch.AnnounceToggle error: {ex.Message}");
        }
    }

    /// <summary>
    /// Read display value from a StepSelectorBase by finding the TMP child
    /// that isn't inside the left or right buttons and isn't the label.
    /// </summary>
    private static string GetSelectorValue(StepSelectorBase selector, string labelToSkip = null)
    {
        for (int i = 0; i < selector.transform.childCount; i++)
        {
            var child = selector.transform.GetChild(i);
            if (selector.leftButton != null && child.gameObject == selector.leftButton.gameObject)
                continue;
            if (selector.rightButton != null && child.gameObject == selector.rightButton.gameObject)
                continue;

            var tmp = child.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
                tmp = child.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            {
                string text = TextHelper.CleanText(tmp.text);
                // Skip if this matches the label text
                if (!string.IsNullOrEmpty(labelToSkip) && text == labelToSkip)
                    continue;
                return text;
            }
        }
        return null;
    }

    /// <summary>
    /// Find label for a slider. The hierarchy is:
    /// UISettingsSlider → MasterSlider → NameText (TMP label), ValueText (TMP value)
    /// We search all descendant TMP components, skipping the valueText.
    /// </summary>
    internal static string GetSliderLabel(UISettingsSlider slider)
    {
        try
        {
            var tmps = slider.GetComponentsInChildren<TextMeshProUGUI>();
            foreach (var tmp in tmps)
            {
                if (tmp == slider.valueText) continue;
                if (!string.IsNullOrEmpty(tmp.text))
                    return TextHelper.CleanText(tmp.text);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"GetSliderLabel error: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Read slider display value. The Target Framerate slider has
    /// textMultiplier=100, making valueText show "6000" instead of "60".
    /// We correct only that specific slider.
    /// </summary>
    internal static string GetSliderValue(UISettingsSlider slider)
    {
        if (slider.valueText == null || string.IsNullOrEmpty(slider.valueText.text))
            return null;

        string raw = TextHelper.CleanText(slider.valueText.text);

        if (slider.gameObject.name == "Target_Framerate_SettingsSlider"
            && slider.textMultiplier > 1
            && int.TryParse(raw, out int numVal))
            return (numVal / slider.textMultiplier).ToString();

        return raw;
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
                    return TextHelper.CleanText(tmp.text);
            }

            // 2. Check grandchildren (deeper UI hierarchies like shop toggles)
            var allTmps = control.GetComponentsInChildren<TextMeshProUGUI>();
            if (allTmps != null)
            {
                foreach (var tmp in allTmps)
                {
                    if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                        return TextHelper.CleanText(tmp.text);
                }
            }

            // 3. Check direct siblings (same parent) for TMP
            var parent = control.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child == control) continue;

                    var tmp = child.GetComponent<TextMeshProUGUI>();
                    if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                        return TextHelper.CleanText(tmp.text);
                }

                // 4. Check grandparent's children (label might be a level up)
                var grandparent = parent.parent;
                if (grandparent != null)
                {
                    for (int i = 0; i < grandparent.childCount; i++)
                    {
                        var child = grandparent.GetChild(i);
                        if (child == parent) continue;

                        var tmp = child.GetComponent<TextMeshProUGUI>();
                        if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                            return TextHelper.CleanText(tmp.text);
                    }
                }
            }
        }
        catch { }
        return null;
    }

}
