using System;
using HarmonyLib;
using DRS.UI;
using drgAccess.Components;

namespace drgAccess.Patches;

/// <summary>
/// Activates PauseReaderComponent when the pause form opens.
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

/// <summary>
/// Resumes PauseReaderComponent when the abort popup closes (user pressed Continue/Escape).
/// HidePopup is declared on UIAbortPopupForm (overrides base), safe to patch.
/// </summary>
[HarmonyPatch(typeof(UIAbortPopupForm), nameof(UIAbortPopupForm.HidePopup))]
public static class AbortPopupHidePatch
{
    public static void Postfix()
    {
        try
        {
            var reader = PauseReaderComponent.Instance;
            if (reader != null && reader.IsSuspendedForMenu)
                reader.ResumeFromOverlayClose();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"AbortPopupHidePatch error: {ex.Message}");
        }
    }
}

