using HarmonyLib;
using DRS.UI;
using drgAccess.Helpers;

namespace drgAccess.Patches;

/// <summary>
/// Patches for UISliderToggle to announce toggle label and state when clicked.
/// </summary>
[HarmonyPatch(typeof(UISliderToggle))]
public static class UISliderTogglePatch
{
    [HarmonyPatch(nameof(UISliderToggle.OnButtonClick))]
    [HarmonyPostfix]
    public static void OnButtonClick_Postfix(UISliderToggle __instance)
    {
        try
        {
            bool isToggled = __instance.IsToggled;
            string state = isToggled ? ModLocalization.Get("ui_on") : ModLocalization.Get("ui_off");
            string label = UISettingsPatch.GetControlLabel(__instance.transform);
            string msg = !string.IsNullOrEmpty(label)
                ? $"{label}: {state}"
                : state;
            ScreenReader.Interrupt(msg);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UISliderTogglePatch.OnButtonClick error: {ex.Message}");
        }
    }
}
