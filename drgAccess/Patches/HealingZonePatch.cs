using HarmonyLib;
using drgAccess.Helpers;

namespace drgAccess.Patches;

/// <summary>
/// Patches for AzureWealdBuffPillars (healing zones in Azure Weald biome).
/// Announces when player enters/exits a healing zone.
/// </summary>
[HarmonyPatch(typeof(AzureWealdBuffPillars))]
public static class HealingZonePatch
{
    [HarmonyPatch(nameof(AzureWealdBuffPillars.BeginTickOnPlayer))]
    [HarmonyPostfix]
    public static void BeginTick_Postfix()
    {
        try
        {
            ScreenReader.Interrupt(ModLocalization.Get("healing_zone_enter"));
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"HealingZonePatch.BeginTick error: {ex.Message}");
        }
    }

    [HarmonyPatch(nameof(AzureWealdBuffPillars.EndTickOnPlayer))]
    [HarmonyPostfix]
    public static void EndTick_Postfix()
    {
        try
        {
            ScreenReader.Say(ModLocalization.Get("healing_zone_exit"));
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"HealingZonePatch.EndTick error: {ex.Message}");
        }
    }
}
