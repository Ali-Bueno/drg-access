using HarmonyLib;
using drgAccess.Components;

namespace drgAccess.Patches
{
    /// <summary>
    /// Patches for DropPod to trigger audio beacons.
    /// </summary>
    [HarmonyPatch]
    public class DropPodPatches
    {
        /// <summary>
        /// Called when the drop pod lands (extraction available).
        /// </summary>
        [HarmonyPatch(typeof(DropPod), nameof(DropPod.OnLand))]
        [HarmonyPostfix]
        public static void OnLand_Postfix(DropPod __instance)
        {
            try
            {
                // Check pod state - only activate beacon for extraction pod
                // Initial pod: state is ANIMATING_IN or transitions quickly
                // Extraction pod: state transitions to WAITING_FOR_PLAYER
                var podState = __instance.state;

                Plugin.Log.LogInfo($"[DropPodPatch] Pod landed, state: {podState}");

                // Only activate beacon if this is the extraction pod
                // WAITING_FOR_PLAYER means it's waiting for player to enter (extraction)
                // We check this with a small delay to let the state settle
                if (podState == DropPod.EState.WAITING_FOR_PLAYER ||
                    podState == DropPod.EState.ARRIVING_COUNTDOWN)
                {
                    var audioSystem = DropPodAudio.Instance;
                    if (audioSystem != null)
                    {
                        // Start playing extraction beacon
                        audioSystem.OnPodLanded(__instance);
                        Plugin.Log.LogInfo($"[DropPodPatch] Extraction pod detected - beacon activated");
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"[DropPodPatch] Initial pod detected (state: {podState}) - beacon NOT activated");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[DropPodPatch] OnLand error: {e.Message}");
            }
        }

        /// <summary>
        /// Called when player enters the drop pod (with player).
        /// </summary>
        [HarmonyPatch(typeof(DropPod), nameof(DropPod.AnimateOutWithPlayer))]
        [HarmonyPostfix]
        public static void AnimateOutWithPlayer_Postfix(DropPod __instance)
        {
            try
            {
                Plugin.Log.LogInfo($"[DropPodPatch] Player entering pod (extraction)");

                var audioSystem = DropPodAudio.Instance;
                if (audioSystem != null)
                {
                    // Player entered, stop beacon
                    audioSystem.OnPlayerEntered();
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[DropPodPatch] AnimateOutWithPlayer error: {e.Message}");
            }
        }

    }
}
