using System;
using Assets.Scripts.Audio;
using drgAccess.Helpers;
using HarmonyLib;

namespace drgAccess.Patches
{
    /// <summary>
    /// Captures game master volume changes so mod audio cues stay proportional.
    /// Patches both SetMasterVolume (user slider changes) and OnSaveDataLoaded
    /// (initial load — native-to-native calls bypass the SetMasterVolume patch).
    /// </summary>
    [HarmonyPatch(typeof(AudioMastering), nameof(AudioMastering.SetMasterVolume))]
    public static class AudioMasteringSetVolumePatch
    {
        static void Postfix(float volume)
        {
            ModConfig.GameMasterVolume = volume;
            Plugin.Log.LogInfo($"[AudioMastering] Master volume updated: {volume:F2}");
        }
    }

    [HarmonyPatch(typeof(AudioMastering), "OnSaveDataLoaded")]
    public static class AudioMasteringLoadPatch
    {
        static void Postfix(AudioMastering __instance)
        {
            try
            {
                var options = __instance.gameOptionsManager?.AudioOptions;
                if (options != null)
                {
                    ModConfig.GameMasterVolume = options.VolumeMaster;
                    Plugin.Log.LogInfo($"[AudioMastering] Initial master volume from save: {options.VolumeMaster:F2}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AudioMastering] OnSaveDataLoaded error: {e.Message}");
            }
        }
    }
}
