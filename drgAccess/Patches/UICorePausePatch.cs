using System;
using HarmonyLib;
using DRS.UI;
using drgAccess.Components;

namespace drgAccess.Patches;

/// <summary>
/// Patches for pause menu: activates the PauseReaderComponent for navigable
/// weapon/artifact/stat reading. Deactivates when pause form hides.
/// </summary>

[HarmonyPatch(typeof(UICorePauseForm), nameof(UICorePauseForm.Show))]
public static class PauseFormShowPatch
{
    public static void Postfix(UICorePauseForm __instance)
    {
        try
        {
            var reader = PauseReaderComponent.Instance;
            if (reader != null)
                reader.Activate(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"PauseFormShowPatch error: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(UICorePauseForm), nameof(UICorePauseForm.SetVisibility))]
public static class PauseFormHidePatch
{
    public static void Postfix(UICorePauseForm __instance, bool visible)
    {
        try
        {
            if (!visible)
            {
                var reader = PauseReaderComponent.Instance;
                if (reader != null)
                    reader.Deactivate();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"PauseFormHidePatch error: {ex.Message}");
        }
    }
}
