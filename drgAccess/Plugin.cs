using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using DavyKager;

namespace drgAccess;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static Harmony HarmonyInstance;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loading...");

        // Load mod configuration
        Helpers.ModConfig.Load();

        // Initialize Tolk for screen reader support
        InitializeTolk();

        // Initialize Harmony for patching
        HarmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);
        HarmonyInstance.PatchAll();

        // Register focus tracker component for settings navigation
        AddComponent<SettingsFocusTracker>();

        // Register gameplay audio components
        AddComponent<Components.EnemyTracker>();
        AddComponent<Components.WallNavigationAudio>();
        AddComponent<Components.EnemyAudioSystem>();
        AddComponent<Components.DropPodAudio>();
        AddComponent<Components.ActivationZoneAudio>();
        AddComponent<Components.HazardWarningAudio>();
        AddComponent<Components.CollectibleAudioSystem>();
        AddComponent<Components.BossAttackAudio>();
        AddComponent<Components.ModSettingsMenu>();
        AddComponent<Components.WalletReaderComponent>();
        AddComponent<Components.EndScreenReaderComponent>();
        AddComponent<Components.PauseReaderComponent>();
        AddComponent<Components.MilestoneReaderComponent>();
        AddComponent<Components.HPReaderComponent>();
        AddComponent<Components.FootstepAudio>();
        AddComponent<Components.DrillBeaconAudio>();
        AddComponent<Components.ObjectiveReaderComponent>();

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} loaded successfully!");
        ScreenReader.Say("DRG Survivor Accessibility mod loaded");
    }

    private void InitializeTolk()
    {
        try
        {
            Tolk.TrySAPI(true);
            Tolk.Load();

            string screenReaderName = Tolk.DetectScreenReader();
            if (!string.IsNullOrEmpty(screenReaderName))
            {
                Log.LogInfo($"Screen reader detected: {screenReaderName}");
            }
            else
            {
                Log.LogWarning("No screen reader detected. Using SAPI as fallback.");
            }
        }
        catch (System.Exception ex)
        {
            Log.LogError($"Failed to initialize Tolk: {ex.Message}");
        }
    }

    public override bool Unload()
    {
        HarmonyInstance?.UnpatchSelf();

        try
        {
            Tolk.Unload();
        }
        catch { }

        return base.Unload();
    }
}
