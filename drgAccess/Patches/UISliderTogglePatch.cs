using HarmonyLib;
using DRS.UI;

namespace drgAccess.Patches;

/// <summary>
/// Patches for UISliderToggle to announce toggle state when clicked.
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
            // After the click, read the new toggle state
            bool isToggled = __instance.IsToggled;
            string state = isToggled ? "On" : "Off";
            ScreenReader.Interrupt(state);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UISliderTogglePatch.OnButtonClick error: {ex.Message}");
        }
    }
}
