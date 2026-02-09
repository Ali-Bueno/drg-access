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
                Plugin.Log.LogInfo($"[DropPodPatch] Pod landed");

                var audioSystem = DropPodAudio.Instance;
                if (audioSystem != null)
                {
                    // Start playing extraction beacon
                    audioSystem.OnPodLanded(__instance);
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
