using System;
using HarmonyLib;

namespace drgAccess.Patches
{
    /// <summary>
    /// Detects the player being flung by launch ramps/catapults.
    /// Player.TryLaunchIntoAir is declared directly on Player (safe to patch).
    /// Used for diagnostics: launch pads have no dedicated managed class, so the
    /// nearby-trigger log from OnPlayerLaunched reveals pad prefab names to add to
    /// CollectibleAudioSystem.launchPadNameFragments.
    /// </summary>
    [HarmonyPatch]
    public class LaunchPadPatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.TryLaunchIntoAir))]
        [HarmonyPostfix]
        public static void TryLaunchIntoAir_Postfix(bool __result, bool jetBoots)
        {
            try
            {
                // jetBoots launches are the player's own artifact, not a map pad
                if (!__result || jetBoots) return;
                Components.CollectibleAudioSystem.Instance?.OnPlayerLaunched();
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[LaunchPad] TryLaunchIntoAir error: {e.Message}");
            }
        }
    }
}
