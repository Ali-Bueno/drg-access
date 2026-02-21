using System;
using System.Collections.Generic;
using DavyKager;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Mod settings menu with volume sliders and audio cue preview submenu.
    /// Opens with F1 outside gameplay. Same EventSystem blocking pattern as AudioCueMenu.
    /// Main menu: Up/Down navigate, Left/Right adjust volume, Enter preview/activate.
    /// Audio cue submenu: Up/Down navigate, Enter preview, Escape back to main.
    /// </summary>
    public class ModSettingsMenu : MonoBehaviour
    {
        public static ModSettingsMenu Instance { get; private set; }

        private bool isMenuOpen = false;
        public bool IsOpen => isMenuOpen;

        // Menu state
        private enum MenuState { Main, AudioCuePreview }
        private MenuState currentState = MenuState.Main;
        private int mainIndex = 0;
        private int cueIndex = 0;

        // Menu items
        private List<MainMenuItem> mainItems;
        private List<AudioCueItem> audioCueItems;

        // Temporary volumes being edited (not yet saved)
        private Dictionary<string, float> pendingVolumes;

        // Audio output for previews
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;

        // Preview state
        private float previewEndTime = 0f;
        private bool isPreviewPlaying = false;

        // Enemy beep repeater for preview
        private EnemyAlertSoundGenerator enemyGenerator;
        private float nextEnemyBeepTime = 0f;
        private EnemyAudioType currentEnemyType;
        private int enemyBeepsRemaining = 0;
        private string currentEnemyCategory; // For volume lookup during repeated beeps

        // Game state
        private IGameStateProvider gameStateProvider;

        // EventSystem blocking
        private GameObject eventSystemObject;
        private GameObject savedSelection;

        // Restoration state machine
        private int restoreStep = -1;

        private const float VOLUME_STEP = 0.05f;

        static ModSettingsMenu()
        {
            ClassInjector.RegisterTypeInIl2Cpp<ModSettingsMenu>();
        }

        private enum MainItemType
        {
            VolumeSlider,
            SettingSlider,
            Toggle,
            AudioCueSubmenu,
            SaveButton,
            CancelButton
        }

        private struct MainMenuItem
        {
            public string Category; // Volume or setting key
            public string DisplayName;
            public MainItemType Type;
            public Action PlayPreview;
        }

        private struct AudioCueItem
        {
            public string Name;
            public string Description;
            public Action PlayPreview;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildMainItems();
            BuildAudioCueItems();
            Plugin.Log.LogInfo("[ModSettingsMenu] Initialized");
        }

        private void BuildMainItems()
        {
            mainItems = new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Category = ModConfig.WALL_NAVIGATION,
                    DisplayName = "Wall Navigation",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => PreviewContinuousTone(500, 0f, ModConfig.WALL_NAVIGATION)
                },
                new MainMenuItem
                {
                    Category = ModConfig.ENEMY_DETECTION,
                    DisplayName = "Enemy Detection",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Normal, 1000, 3, ModConfig.ENEMY_DETECTION)
                },
                new MainMenuItem
                {
                    Category = ModConfig.DROP_POD_BEACON,
                    DisplayName = "Drop Pod Beacon",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => PreviewBeacon(1100, 0.15f, BeaconMode.DropPod, ModConfig.DROP_POD_BEACON)
                },
                new MainMenuItem
                {
                    Category = ModConfig.SUPPLY_POD_BEACON,
                    DisplayName = "Supply Pod Beacon",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => PreviewBeacon(500, 0.18f, BeaconMode.SupplyDrop, ModConfig.SUPPLY_POD_BEACON)
                },
                new MainMenuItem
                {
                    Category = ModConfig.HAZARD_WARNING,
                    DisplayName = "Hazard Warning",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => PreviewAlarm(ModConfig.HAZARD_WARNING)
                },
                new MainMenuItem
                {
                    Category = ModConfig.COLLECTIBLES,
                    DisplayName = "Collectibles",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => PreviewCollectible(CollectibleSoundType.RedSugar, 500f, 0.18f, ModConfig.COLLECTIBLES)
                },
                new MainMenuItem
                {
                    Category = ModConfig.FOOTSTEPS_ENABLED,
                    DisplayName = "Footsteps",
                    Type = MainItemType.Toggle
                },
                new MainMenuItem
                {
                    Category = ModConfig.FOOTSTEPS,
                    DisplayName = "Footsteps Volume",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => FootstepAudio.Instance?.PreviewFootstep(GetPendingVolume(ModConfig.FOOTSTEPS))
                },
                new MainMenuItem
                {
                    Category = ModConfig.BOSS_ATTACKS,
                    DisplayName = "Boss Attacks",
                    Type = MainItemType.VolumeSlider,
                    PlayPreview = () => PreviewBossAttack()
                },
                // --- Detection Settings ---
                new MainMenuItem
                {
                    Category = ModConfig.ENEMY_RANGE,
                    DisplayName = "Enemy Detection Range",
                    Type = MainItemType.SettingSlider
                },
                new MainMenuItem
                {
                    Category = ModConfig.HAZARD_RANGE,
                    DisplayName = "Hazard Warning Range",
                    Type = MainItemType.SettingSlider
                },
                new MainMenuItem
                {
                    Category = ModConfig.COLLECTIBLE_RANGE,
                    DisplayName = "Collectible Range",
                    Type = MainItemType.SettingSlider
                },
                new MainMenuItem
                {
                    Category = ModConfig.WALL_RANGE,
                    DisplayName = "Wall Detection Range",
                    Type = MainItemType.SettingSlider
                },
                new MainMenuItem
                {
                    Category = ModConfig.MAX_HAZARD_CHANNELS,
                    DisplayName = "Max Hazard Warnings",
                    Type = MainItemType.SettingSlider
                },
                // --- Submenu ---
                new MainMenuItem
                {
                    DisplayName = "Audio Cue Preview",
                    Type = MainItemType.AudioCueSubmenu
                },
                new MainMenuItem
                {
                    DisplayName = "Save Changes",
                    Type = MainItemType.SaveButton
                },
                new MainMenuItem
                {
                    DisplayName = "Cancel",
                    Type = MainItemType.CancelButton
                }
            };
        }

        private void BuildAudioCueItems()
        {
            audioCueItems = new List<AudioCueItem>
            {
                new AudioCueItem
                {
                    Name = "Wall: Forward",
                    Description = "Wall ahead. Gets louder the closer the wall is.",
                    PlayPreview = () => PreviewContinuousTone(500, 0f, ModConfig.WALL_NAVIGATION)
                },
                new AudioCueItem
                {
                    Name = "Wall: Backward",
                    Description = "Wall behind you. Gets louder the closer it is.",
                    PlayPreview = () => PreviewContinuousTone(180, 0f, ModConfig.WALL_NAVIGATION)
                },
                new AudioCueItem
                {
                    Name = "Wall: Sides",
                    Description = "Wall to the left or right. Pans to the corresponding side.",
                    PlayPreview = () => PreviewContinuousTone(300, -0.8f, ModConfig.WALL_NAVIGATION)
                },
                new AudioCueItem
                {
                    Name = "Enemy: Normal",
                    Description = "Regular enemy nearby. Beeps faster the closer it gets.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Normal, 1000, 4, ModConfig.ENEMY_DETECTION)
                },
                new AudioCueItem
                {
                    Name = "Enemy: Elite",
                    Description = "Elite enemy nearby. Slower vibrating beep, distinct from normal.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Elite, 300, 3, ModConfig.ENEMY_DETECTION)
                },
                new AudioCueItem
                {
                    Name = "Enemy: Boss",
                    Description = "Boss enemy nearby. Deep rumble, unmistakable.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Boss, 70, 2, ModConfig.ENEMY_DETECTION)
                },
                new AudioCueItem
                {
                    Name = "Rare Loot Enemy",
                    Description = "Golden Lootbug or Huuli Hoarder nearby. Bright ascending chime.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Loot, 1800, 3, ModConfig.ENEMY_DETECTION)
                },
                new AudioCueItem
                {
                    Name = "Drop Pod Beacon",
                    Description = "Guides you to the extraction pod. Metallic sonar ping.",
                    PlayPreview = () => PreviewBeacon(1100, 0.15f, BeaconMode.DropPod, ModConfig.DROP_POD_BEACON)
                },
                new AudioCueItem
                {
                    Name = "Drop Pod: Critical",
                    Description = "Very close to the pod, under 8 meters. Fast double-beep.",
                    PlayPreview = () => PreviewBeaconDouble(1400, 0.08f, ModConfig.DROP_POD_BEACON)
                },
                new AudioCueItem
                {
                    Name = "Drop Pod: Ramp Tone",
                    Description = "On the ramp, under 2.5 meters. Continuous tone toward pod entrance.",
                    PlayPreview = () => PreviewContinuousTone(1400, 0f, ModConfig.DROP_POD_BEACON)
                },
                new AudioCueItem
                {
                    Name = "Supply Pod Beacon",
                    Description = "Guides you to supply pod zones. Warbling trill.",
                    PlayPreview = () => PreviewBeacon(500, 0.18f, BeaconMode.SupplyDrop, ModConfig.SUPPLY_POD_BEACON)
                },
                new AudioCueItem
                {
                    Name = "Hazard Warning",
                    Description = "Danger nearby, like exploders or ground spikes. Siren alarm.",
                    PlayPreview = () => PreviewAlarm(ModConfig.HAZARD_WARNING)
                },
                new AudioCueItem
                {
                    Name = "Collectible: Red Sugar",
                    Description = "Health pickup nearby. Water-drop bloop sound.",
                    PlayPreview = () => PreviewCollectible(CollectibleSoundType.RedSugar, 500f, 0.18f, ModConfig.COLLECTIBLES)
                },
                new AudioCueItem
                {
                    Name = "Collectible: Gear Drop",
                    Description = "Weapon or equipment drop nearby. Rich two-tone chord beep.",
                    PlayPreview = () => PreviewCollectible(CollectibleSoundType.GearDrop, 1000f, 0.2f, ModConfig.COLLECTIBLES)
                },
                new AudioCueItem
                {
                    Name = "Collectible: Buff Pickup",
                    Description = "Buff pickup nearby. Electric buzzy tone.",
                    PlayPreview = () => PreviewCollectible(CollectibleSoundType.BuffPickup, 1200f, 0.15f, ModConfig.COLLECTIBLES)
                },
                new AudioCueItem
                {
                    Name = "Collectible: Currency",
                    Description = "Gold or mineral drop nearby. Short crystalline chime.",
                    PlayPreview = () => PreviewCollectible(CollectibleSoundType.CurrencyPickup, 750f, 0.15f, ModConfig.COLLECTIBLES)
                },
                new AudioCueItem
                {
                    Name = "Collectible: Mineral Vein",
                    Description = "Mineable mineral vein nearby. Metallic clink.",
                    PlayPreview = () => PreviewCollectible(CollectibleSoundType.MineralVein, 400f, 0.25f, ModConfig.COLLECTIBLES)
                },
                new AudioCueItem
                {
                    Name = "Collectible: Loot Crate",
                    Description = "Loot crate nearby. Shimmering sparkle beep.",
                    PlayPreview = () => PreviewCollectible(CollectibleSoundType.LootCrate, 1500f, 0.18f, ModConfig.COLLECTIBLES)
                },
                new AudioCueItem
                {
                    Name = "Collectible: XP Nearby",
                    Description = "XP orbs on the ground. Soft continuous tone.",
                    PlayPreview = () => PreviewCollectibleContinuous(CollectibleSoundType.XpNearby, 500f, ModConfig.COLLECTIBLES)
                },
                // --- Boss Attack Cues ---
                new AudioCueItem
                {
                    Name = "Boss: Charge",
                    Description = "Boss charging at you. 3 fast aggressive beeps.",
                    PlayPreview = () => PreviewBossAttackType(BossAttackType.Charge)
                },
                new AudioCueItem
                {
                    Name = "Boss: Spikes",
                    Description = "Boss spawning ground spikes. 3 medium deep beeps.",
                    PlayPreview = () => PreviewBossAttackType(BossAttackType.Spikes)
                },
                new AudioCueItem
                {
                    Name = "Boss: Fireball",
                    Description = "Boss firing projectiles. 3 very fast sharp beeps.",
                    PlayPreview = () => PreviewBossAttackType(BossAttackType.Fireball)
                },
                new AudioCueItem
                {
                    Name = "Boss: Heal",
                    Description = "Boss healing itself. 3 slow gentle beeps.",
                    PlayPreview = () => PreviewBossAttackType(BossAttackType.Heal)
                }
            };
        }

        private void StartAudio()
        {
            DisposeAudio();
            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                mixer = new MixingSampleProvider(format) { ReadFully = true };
                outputDevice = new WaveOutEvent { DesiredLatency = 80 };
                outputDevice.Init(mixer);
                outputDevice.Play();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] StartAudio error: {e.Message}");
            }
        }

        private void DisposeAudio()
        {
            try
            {
                if (mixer != null) mixer.RemoveAllMixerInputs();
                outputDevice?.Stop();
                outputDevice?.Dispose();
            }
            catch { }
            outputDevice = null;
            mixer = null;
        }

        void Update()
        {
            try
            {
                // Restoration state machine
                if (restoreStep > 0)
                {
                    restoreStep--;
                }
                else if (restoreStep == 0)
                {
                    restoreStep = -1;
                    FinishRestore();
                }

                // Handle preview timeout
                if (isPreviewPlaying)
                {
                    if (enemyBeepsRemaining > 0 && Time.unscaledTime >= nextEnemyBeepTime)
                    {
                        float vol = GetPendingVolume(currentEnemyCategory);
                        enemyGenerator?.Play(
                            currentEnemyType == EnemyAudioType.Boss ? 70 :
                            currentEnemyType == EnemyAudioType.Elite ? 300 :
                            currentEnemyType == EnemyAudioType.Loot ? 1800 : 1000,
                            0.35f * vol, currentEnemyType);
                        enemyBeepsRemaining--;

                        float interval = currentEnemyType == EnemyAudioType.Boss ? 0.5f :
                                         currentEnemyType == EnemyAudioType.Elite ? 0.35f :
                                         currentEnemyType == EnemyAudioType.Loot ? 0.3f : 0.2f;
                        nextEnemyBeepTime = Time.unscaledTime + interval;
                    }

                    if (Time.unscaledTime >= previewEndTime)
                        ClearPreview();
                }

                // Toggle menu with F1
                if (InputHelper.ToggleSettingsMenu())
                {
                    if (isMenuOpen)
                        CloseMenu(false);
                    else if (!IsInActiveGameplay())
                        OpenMenu();
                    return;
                }

                if (!isMenuOpen) return;

                // Escape / B button: back or close
                if (InputHelper.Cancel())
                {
                    if (currentState == MenuState.AudioCuePreview)
                        ExitSubmenu();
                    else
                        CloseMenu(false);
                    return;
                }

                // Dispatch input based on state
                if (currentState == MenuState.Main)
                    HandleMainInput();
                else
                    HandleAudioCueInput();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] Update error: {e.Message}");
            }
        }

        // --- Main Menu Input ---

        private void HandleMainInput()
        {
            if (InputHelper.NavigateUp())
                NavigateMain(-1);
            else if (InputHelper.NavigateDown())
                NavigateMain(1);

            var item = mainItems[mainIndex];
            if (item.Type == MainItemType.VolumeSlider)
            {
                if (InputHelper.NavigateLeft())
                    AdjustVolume(item.Category, -VOLUME_STEP);
                else if (InputHelper.NavigateRight())
                    AdjustVolume(item.Category, VOLUME_STEP);
            }
            else if (item.Type == MainItemType.SettingSlider)
            {
                if (InputHelper.NavigateLeft())
                    AdjustSetting(item.Category, false);
                else if (InputHelper.NavigateRight())
                    AdjustSetting(item.Category, true);
            }
            else if (item.Type == MainItemType.Toggle)
            {
                if (InputHelper.NavigateLeft() || InputHelper.NavigateRight())
                    ToggleSetting(item.Category);
            }

            if (InputHelper.Confirm())
                HandleMainConfirm();
        }

        private void NavigateMain(int direction)
        {
            ClearPreview();
            mainIndex += direction;
            if (mainIndex < 0) mainIndex = mainItems.Count - 1;
            if (mainIndex >= mainItems.Count) mainIndex = 0;
            AnnounceMainItem();
        }

        private void AnnounceMainItem()
        {
            var item = mainItems[mainIndex];
            switch (item.Type)
            {
                case MainItemType.VolumeSlider:
                    int pct = Mathf.RoundToInt(GetPendingVolume(item.Category) * 100);
                    SpeakDirect($"{item.DisplayName}. {pct}%");
                    break;
                case MainItemType.SettingSlider:
                    SpeakDirect($"{item.DisplayName}. {FormatSetting(item.Category)}");
                    break;
                case MainItemType.Toggle:
                    bool isOn = GetPendingToggle(item.Category);
                    SpeakDirect($"{item.DisplayName}. {(isOn ? "On" : "Off")}");
                    break;
                default:
                    SpeakDirect(item.DisplayName);
                    break;
            }
        }

        private void HandleMainConfirm()
        {
            var item = mainItems[mainIndex];
            switch (item.Type)
            {
                case MainItemType.VolumeSlider:
                    ClearPreview();
                    item.PlayPreview?.Invoke();
                    break;
                case MainItemType.Toggle:
                    ToggleSetting(item.Category);
                    break;
                case MainItemType.AudioCueSubmenu:
                    EnterSubmenu();
                    break;
                case MainItemType.SaveButton:
                    CloseMenu(true);
                    break;
                case MainItemType.CancelButton:
                    CloseMenu(false);
                    break;
            }
        }

        // --- Audio Cue Submenu ---

        private void EnterSubmenu()
        {
            ClearPreview();
            currentState = MenuState.AudioCuePreview;
            cueIndex = 0;

            var first = audioCueItems[0];
            SpeakDirect($"Audio cue preview. Up and down to navigate, Enter to preview, Escape to go back. {first.Name}. {first.Description}");
        }

        private void ExitSubmenu()
        {
            ClearPreview();
            currentState = MenuState.Main;
            AnnounceMainItem();
        }

        private void HandleAudioCueInput()
        {
            if (InputHelper.NavigateUp())
                NavigateCue(-1);
            else if (InputHelper.NavigateDown())
                NavigateCue(1);

            if (InputHelper.Confirm())
            {
                ClearPreview();
                audioCueItems[cueIndex].PlayPreview?.Invoke();
            }
        }

        private void NavigateCue(int direction)
        {
            ClearPreview();
            cueIndex += direction;
            if (cueIndex < 0) cueIndex = audioCueItems.Count - 1;
            if (cueIndex >= audioCueItems.Count) cueIndex = 0;

            var item = audioCueItems[cueIndex];
            SpeakDirect($"{item.Name}. {item.Description}");
        }

        // --- Volume Adjustment ---

        private void AdjustVolume(string category, float delta)
        {
            ClearPreview();
            if (pendingVolumes == null) return;

            float current = GetPendingVolume(category);
            float newVal = Mathf.Clamp01(current + delta);
            newVal = Mathf.Round(newVal * 20f) / 20f;
            pendingVolumes[category] = newVal;

            int pct = Mathf.RoundToInt(newVal * 100);
            SpeakDirect($"{pct}%");
        }

        private void AdjustSetting(string key, bool increase)
        {
            ClearPreview();
            if (pendingVolumes == null) return;
            if (!ModConfig.SettingDefs.TryGetValue(key, out var def)) return;

            float current = pendingVolumes.ContainsKey(key) ? pendingVolumes[key] : def.Default;
            float newVal = increase ? current + def.Step : current - def.Step;
            newVal = Mathf.Clamp(newVal, def.Min, def.Max);
            // Snap to step
            newVal = Mathf.Round(newVal / def.Step) * def.Step;
            pendingVolumes[key] = newVal;

            SpeakDirect(FormatSettingValue(key, newVal));
        }

        private string FormatSetting(string key)
        {
            if (!ModConfig.SettingDefs.TryGetValue(key, out var def)) return "";
            float val = pendingVolumes != null && pendingVolumes.ContainsKey(key) ? pendingVolumes[key] : def.Default;
            return FormatSettingValue(key, val);
        }

        private string FormatSettingValue(string key, float val)
        {
            if (!ModConfig.SettingDefs.TryGetValue(key, out var def)) return val.ToString();

            if (def.Unit == "m")
                return $"{val:F0} meters";
            if (def.Unit == "x")
                return $"{val:F1}x";
            // No unit (count)
            return $"{val:F0}";
        }

        private void ToggleSetting(string key)
        {
            if (pendingVolumes == null) return;
            bool current = GetPendingToggle(key);
            pendingVolumes[key] = current ? 0f : 1f;
            SpeakDirect(current ? "Off" : "On");
        }

        private bool GetPendingToggle(string key)
        {
            if (pendingVolumes != null && pendingVolumes.TryGetValue(key, out float val))
                return val >= 0.5f;
            if (ModConfig.SettingDefs.TryGetValue(key, out var def))
                return def.Default >= 0.5f;
            return true;
        }

        private float GetPendingVolume(string category)
        {
            if (pendingVolumes != null && pendingVolumes.TryGetValue(category, out float vol))
                return vol;
            return 1.0f;
        }

        // --- Menu Open/Close ---

        private void SpeakDirect(string text)
        {
            try { Tolk.Speak(text, true); }
            catch { }
        }

        private void OpenMenu()
        {
            isMenuOpen = true;
            currentState = MenuState.Main;
            mainIndex = 0;

            pendingVolumes = ModConfig.GetSnapshot();
            StartAudio();
            ScreenReader.Suppressed = true;

            try
            {
                var es = EventSystem.current;
                if (es != null)
                {
                    savedSelection = es.currentSelectedGameObject;
                    eventSystemObject = es.gameObject;
                    eventSystemObject.SetActive(false);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[ModSettingsMenu] Deactivate error: {e.Message}");
            }

            var firstItem = mainItems[0];
            int pct = Mathf.RoundToInt(GetPendingVolume(firstItem.Category) * 100);
            string announcement = "Mod settings. Up and down to navigate, left and right to adjust volume, Enter to preview or activate, F1 or Escape to close.";
            announcement += $" {firstItem.DisplayName}. {pct}%";
            SpeakDirect(announcement);
            Plugin.Log.LogInfo("[ModSettingsMenu] Menu opened");
        }

        private void CloseMenu(bool save)
        {
            ClearPreview();
            DisposeAudio();
            isMenuOpen = false;
            currentState = MenuState.Main;

            if (save)
            {
                ModConfig.ApplySnapshot(pendingVolumes);
                ModConfig.Save();
                SpeakDirect("Settings saved");
            }
            else
            {
                SpeakDirect("Changes cancelled");
            }
            pendingVolumes = null;

            try
            {
                if (eventSystemObject != null)
                {
                    eventSystemObject.SetActive(true);
                    eventSystemObject = null;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[ModSettingsMenu] Reactivate error: {e.Message}");
                eventSystemObject = null;
            }

            restoreStep = 5;
            ScreenReader.Suppressed = false;
            Plugin.Log.LogInfo($"[ModSettingsMenu] Menu closed, save={save}");
        }

        private void FinishRestore()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null)
                {
                    savedSelection = null;
                    return;
                }

                var module = es.GetComponent<InputSystemUIInputModule>();
                if (module != null)
                {
                    module.enabled = false;
                    module.enabled = true;
                }
                else
                {
                    var components = es.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        try
                        {
                            if (comp is Behaviour b && comp.GetType().Name.Contains("Input"))
                            {
                                b.enabled = false;
                                b.enabled = true;
                            }
                        }
                        catch { }
                    }
                }

                if (savedSelection != null)
                {
                    es.SetSelectedGameObject(savedSelection);
                    var selectable = savedSelection.GetComponent<Selectable>();
                    if (selectable != null)
                        selectable.Select();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[ModSettingsMenu] FinishRestore error: {e.Message}");
            }
            savedSelection = null;
        }

        // --- Preview Helpers ---

        private void ClearPreview()
        {
            isPreviewPlaying = false;
            enemyBeepsRemaining = 0;
            enemyGenerator = null;
            if (mixer != null)
                mixer.RemoveAllMixerInputs();
        }

        private void AddToMixer(ISampleProvider mono, float pan = 0f)
        {
            if (mixer == null) return;
            var panProvider = new PanningSampleProvider(mono) { Pan = pan };
            mixer.AddMixerInput(panProvider);
            isPreviewPlaying = true;
        }

        private void PreviewContinuousTone(double frequency, float pan, string category)
        {
            try
            {
                float vol = GetPendingVolume(category);
                var generator = new SineWaveGenerator(frequency, 0.25f * vol);
                AddToMixer(generator, pan);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewTone error: {e.Message}");
            }
        }

        private void PreviewEnemyBeep(EnemyAudioType type, double frequency, int beepCount, string category)
        {
            try
            {
                enemyGenerator = new EnemyAlertSoundGenerator();
                AddToMixer(enemyGenerator);

                float vol = GetPendingVolume(category);
                currentEnemyType = type;
                currentEnemyCategory = category;
                enemyBeepsRemaining = beepCount;
                nextEnemyBeepTime = Time.unscaledTime;

                enemyGenerator.Play(
                    type == EnemyAudioType.Boss ? 70 :
                    type == EnemyAudioType.Elite ? 300 :
                    type == EnemyAudioType.Loot ? 1800 : 1000,
                    0.35f * vol, type);
                enemyBeepsRemaining--;
                nextEnemyBeepTime = Time.unscaledTime + 0.2f;

                float totalDuration = type == EnemyAudioType.Boss ? beepCount * 0.5f + 0.5f :
                                      type == EnemyAudioType.Elite ? beepCount * 0.35f + 0.3f :
                                      type == EnemyAudioType.Loot ? beepCount * 0.3f + 0.3f :
                                      beepCount * 0.2f + 0.2f;
                previewEndTime = Time.unscaledTime + totalDuration;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewEnemyBeep error: {e.Message}");
            }
        }

        private void PreviewBeacon(float frequency, float interval, BeaconMode mode, string category)
        {
            try
            {
                float vol = GetPendingVolume(category);
                var generator = new BeaconBeepGenerator();
                generator.Mode = mode;
                generator.Frequency = frequency;
                generator.Volume = 0.35f * vol;
                generator.Interval = interval;
                generator.Active = true;

                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewBeacon error: {e.Message}");
            }
        }

        private void PreviewBeaconDouble(float frequency, float interval, string category)
        {
            try
            {
                float vol = GetPendingVolume(category);
                var generator = new BeaconBeepGenerator();
                generator.Mode = BeaconMode.DropPod;
                generator.Frequency = frequency;
                generator.Volume = 0.40f * vol;
                generator.Interval = interval;
                generator.DoubleBeep = true;
                generator.Active = true;

                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewBeaconDouble error: {e.Message}");
            }
        }

        private void PreviewAlarm(string category)
        {
            try
            {
                float vol = GetPendingVolume(category);
                var generator = new AlarmSoundGenerator(800, 0.25f * vol);
                generator.AlarmRate = 10;
                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewAlarm error: {e.Message}");
            }
        }

        private void PreviewCollectible(CollectibleSoundType type, float frequency, float interval, string category)
        {
            try
            {
                float vol = GetPendingVolume(category);
                var generator = new CollectibleSoundGenerator();
                generator.SoundType = type;
                generator.Frequency = frequency;
                generator.Volume = 0.30f * vol;
                generator.Interval = interval;
                generator.Active = true;

                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewCollectible error: {e.Message}");
            }
        }

        private void PreviewCollectibleContinuous(CollectibleSoundType type, float frequency, string category)
        {
            try
            {
                float vol = GetPendingVolume(category);
                var generator = new CollectibleSoundGenerator();
                generator.SoundType = type;
                generator.Frequency = frequency;
                generator.Volume = 0.25f * vol;
                generator.Active = true;

                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewCollectibleContinuous error: {e.Message}");
            }
        }

        private void PreviewBossAttack()
        {
            PreviewBossAttackType(BossAttackType.Charge);
        }

        private void PreviewBossAttackType(BossAttackType type)
        {
            try
            {
                float vol = GetPendingVolume(ModConfig.BOSS_ATTACKS);
                var generator = new ChargingSoundGenerator();
                generator.Volume = 0.35f * vol;
                generator.Play(type);
                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModSettingsMenu] PreviewBossAttack error: {e.Message}");
            }
        }

        // --- Utility ---

        private bool IsInActiveGameplay()
        {
            try
            {
                if (gameStateProvider != null)
                {
                    var gc = gameStateProvider.TryCast<GameController>();
                    if (gc == null) gameStateProvider = null;
                }

                if (gameStateProvider == null)
                {
                    var gameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (gameController != null)
                        gameStateProvider = gameController.Cast<IGameStateProvider>();
                    else
                        return false;
                }

                if (gameStateProvider != null)
                    return gameStateProvider.State == GameController.EGameState.CORE;
            }
            catch { }
            return false;
        }

        void OnDestroy()
        {
            ClearPreview();
            DisposeAudio();
            if (eventSystemObject != null)
            {
                try { eventSystemObject.SetActive(true); } catch { }
                eventSystemObject = null;
            }
            ScreenReader.Suppressed = false;
            Instance = null;
            Plugin.Log.LogInfo("[ModSettingsMenu] Destroyed");
        }
    }
}
