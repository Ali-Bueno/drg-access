using System;
using HarmonyLib;
using Assets.Scripts.LevelObjectives.Handlers;
using drgAccess.Helpers;

namespace drgAccess.Patches
{
    /// <summary>
    /// Patches for Elimination mode: cocoon destruction, boss spawning, and threat level.
    /// All patched methods are declared directly on their respective classes (safe to patch).
    /// </summary>
    [HarmonyPatch]
    public class EliminationPatches
    {
        /// <summary>
        /// Announce when an elite spawns from a destroyed cocoon, with remaining count.
        /// SpawnElite is called after the cocoon is destroyed and the elite is spawned.
        /// </summary>
        [HarmonyPatch(typeof(EliminationMissionHandler), nameof(EliminationMissionHandler.SpawnElite))]
        [HarmonyPostfix]
        public static void SpawnElite_Postfix(EliminationMissionHandler __instance)
        {
            try
            {
                var cocoons = __instance.cocoons;
                int remaining = cocoons != null ? cocoons.Count : 0;

                if (remaining > 0)
                    ScreenReader.Interrupt(ModLocalization.Get("elim_elite_spawned", remaining));
                else
                    ScreenReader.Interrupt(ModLocalization.Get("elim_no_cocoons"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EliminationPatch] SpawnElite error: {e.Message}");
            }
        }

        /// <summary>
        /// Announce when the Dreadnought emerges from its cocoon.
        /// </summary>
        [HarmonyPatch(typeof(EliminationMissionHandler), nameof(EliminationMissionHandler.SpawnBoss))]
        [HarmonyPostfix]
        public static void SpawnBoss_Postfix()
        {
            try
            {
                ScreenReader.Interrupt(ModLocalization.Get("elim_dreadnought_emerging"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EliminationPatch] SpawnBoss error: {e.Message}");
            }
        }

        /// <summary>
        /// Announce alien threat level changes during boss fights.
        /// CoreStatTracker.OnThreatLevel is declared directly on CoreStatTracker.
        /// </summary>
        [HarmonyPatch(typeof(CoreStatTracker), nameof(CoreStatTracker.OnThreatLevel))]
        [HarmonyPostfix]
        public static void OnThreatLevel_Postfix(int level)
        {
            try
            {
                ScreenReader.Interrupt(ModLocalization.Get("elim_threat_level", level));
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EliminationPatch] OnThreatLevel error: {e.Message}");
            }
        }
    }
}
