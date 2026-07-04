using System;
using HarmonyLib;
using UnityEngine;
using Assets.Scripts.LevelGeneration;
using drgAccess.Components;

namespace drgAccess.Patches
{
    /// <summary>
    /// Patches for hazard detection (ground spikes).
    /// Registers hazard positions with HazardWarningAudio for audio warnings.
    /// </summary>
    [HarmonyPatch]
    public class HazardPatches
    {
        /// <summary>
        /// Detect when a ground spike spawns (Dreadnought boss attack).
        /// GroundSpike.OnSpawn is declared directly on GroundSpike, safe to patch.
        /// </summary>
        [HarmonyPatch(typeof(GroundSpike), "OnSpawn")]
        [HarmonyPostfix]
        public static void GroundSpike_OnSpawn_Postfix(GroundSpike __instance, Vector3 pos, float baseLife, float damageRadius)
        {
            try
            {
                HazardWarningAudio.RegisterGroundSpike(pos, baseLife);
                DodgeAssistComponent.Instance?.OnSpikeSpawned(pos, baseLife, damageRadius);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[HazardPatch] GroundSpike.OnSpawn error: {e.Message}");
            }
        }
    }
}
