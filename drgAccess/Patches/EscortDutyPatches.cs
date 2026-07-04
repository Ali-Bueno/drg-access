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
    ///
    /// These patches are accelerators only: IL2CPP native-to-native calls can bypass
    /// them, so EscortPhaseAudio also polls the real game state every 2 seconds.
    /// Announcements live in EscortPhaseAudio and are guarded against duplicates.
    /// </summary>
    [HarmonyPatch]
    public class EscortDutyPatches
    {
        // === TNT Phase ===

        /// <summary>
        /// Detect when escort mission enters PREPARE_TNT phase, and capture the
        /// handler instance so EscortPhaseAudio polling can read its state directly.
        /// </summary>
        [HarmonyPatch(typeof(EscortMissionHandler), nameof(EscortMissionHandler.SetState))]
        [HarmonyPostfix]
        public static void SetState_Postfix(EscortMissionHandler __instance,
            EscortMissionHandler.EState state)
        {
            try
            {
                Components.EscortPhaseAudio.Instance?.SetMissionHandler(__instance);

                if (state == EscortMissionHandler.EState.PREPARE_TNT)
                    Components.EscortPhaseAudio.Instance?.OnTNTPhaseStarted();
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
        public static void OnTNTProgress_Postfix(EscortMissionHandler __instance,
            int current, int target)
        {
            try
            {
                Components.EscortPhaseAudio.Instance?.SetMissionHandler(__instance);
                Components.EscortPhaseAudio.Instance?.OnDetonatorArmed(current, target);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] OnTNTProgress error: {e.Message}");
            }
        }

        /// <summary>
        /// A detonator became live (armable) — reliable signal that the TNT phase started.
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
                    Components.EscortPhaseAudio.Instance?.OnOmmoranPhaseStarted(__instance);
                else if (state == OmmoranHeartstone.OmmoranState.DEAD)
                    Components.EscortPhaseAudio.Instance?.OnOmmoranDestroyed();
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
                Components.EscortPhaseAudio.Instance?.OnCrystalsSpawned(crystalCount);
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
                Components.EscortPhaseAudio.Instance?.OnCrystalDestroyed(remaining);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortDutyPatch] OnCrystalDeath error: {e.Message}");
            }
        }
    }
}
