using System;
using HarmonyLib;
using DRS.UI;
using drgAccess.Components;

namespace drgAccess.Patches;

/// <summary>
/// Activates PauseReaderComponent when the pause form opens.
/// The reader is fully self-contained (handles its own close/Escape/settings),
/// so no hide patch is needed.
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
