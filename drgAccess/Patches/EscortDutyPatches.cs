using System;
using HarmonyLib;
using drgAccess.Helpers;

namespace drgAccess.Patches
{
    /// <summary>
    /// Patches for Escort Duty mission phases after Bobby reaches destination:
    /// - TNT phase (stages 1-2): arming detonators
    /// - Ommoran phase (stage 3): destroying heartstone and crystals
    /// All patched methods are declared directly on their respective classes (safe to patch).
    /// </summary>
    [HarmonyPatch]
    public class EscortDutyPatches
    {
        // === TNT Phase ===

        /// <summary>
        /// Announce when escort mission enters PREPARE_TNT phase.
        /// EscortMissionHandler.SetState is declared on EscortMissionHandler (Il2CppSystem.Object).
        /// Note: May not fire if called native-to-native — EscortPhaseAudio has polling fallback.
        /// </summary>
        [HarmonyPatch(typeof(EscortMissionHandler), nameof(EscortMissionHandler.SetState))]
        [HarmonyPostfix]
        public static void SetState_Postfix(EscortMissionHandler __instance,
            EscortMissionHandler.EState state)
        {
            try
            {
                if (state == EscortMissionHandler.EState.PREPARE_TNT)
                {
                    ScreenReader.Interrupt(ModLocalization.Get("escort_arm_tnt"));
                    Components.EscortPhaseAudio.Instance?.OnTNTPhaseStarted();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] SetState error: {e.Message}");
            }
        }

        /// <summary>
        /// Announce TNT arming progress: "Detonator armed, X of Y" or "All armed".
        /// OnTNTProgress is declared on EscortMissionHandler.
        /// </summary>
        [HarmonyPatch(typeof(EscortMissionHandler), nameof(EscortMissionHandler.OnTNTProgress))]
        [HarmonyPostfix]
        public static void OnTNTProgress_Postfix(int current, int target)
        {
            try
            {
                Components.EscortPhaseAudio.Instance?.OnDetonatorArmed();

                if (current >= target)
                    ScreenReader.Interrupt(ModLocalization.Get("escort_tnt_all_armed"));
                else
                    ScreenReader.Interrupt(ModLocalization.Get("escort_tnt_progress", current, target));
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] OnTNTProgress error: {e.Message}");
            }
        }

        /// <summary>
        /// Register new TNT detonator for beacon tracking when it becomes active.
        /// OnDetonatorLive is declared on TNTDetonator.
        /// </summary>
        [HarmonyPatch(typeof(TNTDetonator), nameof(TNTDetonator.OnDetonatorLive))]
        [HarmonyPostfix]
        public static void OnDetonatorLive_Postfix(TNTDetonator __instance)
        {
            try
            {
                Components.EscortPhaseAudio.Instance?.RegisterDetonator(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] OnDetonatorLive error: {e.Message}");
            }
        }

        // === Ommoran Phase ===

        /// <summary>
        /// Announce Ommoran Heartstone state changes (appeared, destroyed).
        /// SetState is declared on OmmoranHeartstone (MonoBehaviour).
        /// </summary>
        [HarmonyPatch(typeof(OmmoranHeartstone), nameof(OmmoranHeartstone.SetState))]
        [HarmonyPostfix]
        public static void OmmoranSetState_Postfix(OmmoranHeartstone __instance,
            OmmoranHeartstone.OmmoranState state)
        {
            try
            {
                if (state == OmmoranHeartstone.OmmoranState.BASIC)
                {
                    ScreenReader.Interrupt(ModLocalization.Get("escort_ommoran_appeared"));
                    Components.EscortPhaseAudio.Instance?.OnOmmoranPhaseStarted(__instance);
                }
                else if (state == OmmoranHeartstone.OmmoranState.DEAD)
                {
                    ScreenReader.Interrupt(ModLocalization.Get("escort_ommoran_destroyed"));
                    Components.EscortPhaseAudio.Instance?.OnOmmoranDestroyed();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] OmmoranSetState error: {e.Message}");
            }
        }

        /// <summary>
        /// Announce when Ommoran crystals spawn and need to be mined.
        /// SpawnCrystals is declared on OmmoranHeartstone.
        /// </summary>
        [HarmonyPatch(typeof(OmmoranHeartstone), nameof(OmmoranHeartstone.SpawnCrystals))]
        [HarmonyPostfix]
        public static void SpawnCrystals_Postfix(OmmoranHeartstone __instance, int crystalCount)
        {
            try
            {
                ScreenReader.Interrupt(ModLocalization.Get("escort_crystals_spawned", crystalCount));
                Components.EscortPhaseAudio.Instance?.OnCrystalsSpawned();
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] SpawnCrystals error: {e.Message}");
            }
        }

        /// <summary>
        /// Announce crystal destruction with remaining count.
        /// OnCrystalDeath is declared on OmmoranHeartstone.
        /// </summary>
        [HarmonyPatch(typeof(OmmoranHeartstone), nameof(OmmoranHeartstone.OnCrystalDeath))]
        [HarmonyPostfix]
        public static void OnCrystalDeath_Postfix(OmmoranHeartstone __instance)
        {
            try
            {
                var live = __instance.LiveCrystals;
                int remaining = live != null ? live.Count : 0;

                if (remaining > 0)
                    ScreenReader.Interrupt(ModLocalization.Get("escort_crystal_destroyed", remaining));
                else
                    ScreenReader.Interrupt(ModLocalization.Get("escort_crystals_cleared"));

                Components.EscortPhaseAudio.Instance?.OnCrystalDestroyed(remaining);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] OnCrystalDeath error: {e.Message}");
            }
        }
    }
}
