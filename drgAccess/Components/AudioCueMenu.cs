using System;
using System.Collections.Generic;
using DavyKager;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio cue preview menu. Opens with Backspace outside gameplay.
    /// Navigate with W/S or Up/Down arrows, preview with Enter, close with Backspace/Escape.
    /// Deactivates EventSystem GameObject while open, then toggles InputSystemUIInputModule
    /// on close to force full reinitialization of navigation actions.
    /// </summary>
    public class AudioCueMenu : MonoBehaviour
    {
        public static AudioCueMenu Instance { get; private set; }

        private bool isMenuOpen = false;
        private int selectedIndex = 0;
        private List<AudioCueItem> menuItems;

        // Audio output (created when menu opens, disposed when it closes)
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;

        // Preview state
        private float previewEndTime = 0f;
        private bool isPreviewPlaying = false;

        // Enemy beep repeater
        private EnemyAlertSoundGenerator enemyGenerator;
        private float nextEnemyBeepTime = 0f;
        private EnemyAudioType currentEnemyType;
        private int enemyBeepsRemaining = 0;

        // Game state
        private IGameStateProvider gameStateProvider;

        // EventSystem blocking
        private GameObject eventSystemObject;
        private GameObject savedSelection;

        // Restoration state machine: -1=idle, >0=waiting, 0=restore now
        private int restoreStep = -1;

        static AudioCueMenu()
        {
            ClassInjector.RegisterTypeInIl2Cpp<AudioCueMenu>();
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

            BuildMenuItems();
            Plugin.Log.LogInfo("[AudioCueMenu] Initialized");
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
                Plugin.Log.LogError($"[AudioCueMenu] StartAudio error: {e.Message}");
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

        private void BuildMenuItems()
        {
            menuItems = new List<AudioCueItem>
            {
                new AudioCueItem
                {
                    Name = "Wall: Forward",
                    Description = "Wall ahead. Gets louder the closer the wall is.",
                    PlayPreview = () => PreviewContinuousTone(500, 0f)
                },
                new AudioCueItem
                {
                    Name = "Wall: Backward",
                    Description = "Wall behind you. Gets louder the closer it is.",
                    PlayPreview = () => PreviewContinuousTone(180, 0f)
                },
                new AudioCueItem
                {
                    Name = "Wall: Sides",
                    Description = "Wall to the left or right. Pans to the corresponding side.",
                    PlayPreview = () => PreviewContinuousTone(300, -0.8f)
                },
                new AudioCueItem
                {
                    Name = "Enemy: Normal",
                    Description = "Regular enemy nearby. Beeps faster the closer it gets.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Normal, 1000, 4)
                },
                new AudioCueItem
                {
                    Name = "Enemy: Elite",
                    Description = "Elite enemy nearby. Slower vibrating beep, distinct from normal.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Elite, 300, 3)
                },
                new AudioCueItem
                {
                    Name = "Enemy: Boss",
                    Description = "Boss enemy nearby. Deep rumble, unmistakable.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Boss, 70, 2)
                },
                new AudioCueItem
                {
                    Name = "Enemy: Loot",
                    Description = "Loot enemy nearby. Bright ascending chime. Kill it for rewards.",
                    PlayPreview = () => PreviewEnemyBeep(EnemyAudioType.Loot, 1800, 3)
                },
                new AudioCueItem
                {
                    Name = "Drop Pod Beacon",
                    Description = "Guides you to the extraction pod. High-pitched chirp, faster as you approach.",
                    PlayPreview = () => PreviewBeacon(1100, 0.15f)
                },
                new AudioCueItem
                {
                    Name = "Supply Pod Beacon",
                    Description = "Guides you to supply pod zones. Lower chirp, distinct from drop pod.",
                    PlayPreview = () => PreviewBeacon(500, 0.18f)
                },
                new AudioCueItem
                {
                    Name = "Hazard Warning",
                    Description = "Danger nearby, like exploders or ground spikes. Siren alarm that speeds up.",
                    PlayPreview = () => PreviewAlarm()
                }
            };
        }

        private static bool KeyPressed(Key key)
        {
            try
            {
                var kb = Keyboard.current;
                if (kb == null) return false;
                return kb[key].wasPressedThisFrame;
            }
            catch { return false; }
        }

        void Update()
        {
            try
            {
                // Restoration state machine (runs after EventSystem reactivation)
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
                        enemyGenerator?.Play(
                            currentEnemyType == EnemyAudioType.Boss ? 70 :
                            currentEnemyType == EnemyAudioType.Elite ? 300 :
                            currentEnemyType == EnemyAudioType.Loot ? 1800 : 1000,
                            0.35f, currentEnemyType);
                        enemyBeepsRemaining--;

                        float interval = currentEnemyType == EnemyAudioType.Boss ? 0.5f :
                                         currentEnemyType == EnemyAudioType.Elite ? 0.35f :
                                         currentEnemyType == EnemyAudioType.Loot ? 0.3f : 0.2f;
                        nextEnemyBeepTime = Time.unscaledTime + interval;
                    }

                    if (Time.unscaledTime >= previewEndTime)
                        ClearPreview();
                }

                // Toggle menu with Backspace
                if (KeyPressed(Key.Backspace))
                {
                    if (isMenuOpen)
                        CloseMenu();
                    else if (!IsInActiveGameplay())
                        OpenMenu();
                    return;
                }

                // Escape also closes
                if (isMenuOpen && KeyPressed(Key.Escape))
                {
                    CloseMenu();
                    return;
                }

                if (!isMenuOpen) return;

                // Navigation
                if (KeyPressed(Key.W) || KeyPressed(Key.UpArrow))
                    NavigateMenu(-1);
                else if (KeyPressed(Key.S) || KeyPressed(Key.DownArrow))
                    NavigateMenu(1);

                // Preview
                if (KeyPressed(Key.Enter) || KeyPressed(Key.NumpadEnter))
                    PlayCurrentPreview();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AudioCueMenu] Update error: {e.Message}");
            }
        }

        private void SpeakDirect(string text)
        {
            try { Tolk.Speak(text, true); }
            catch { }
        }

        private void OpenMenu()
        {
            isMenuOpen = true;
            selectedIndex = 0;

            // Create audio pipeline and warmup the Windows audio endpoint
            StartAudio();

            ScreenReader.Suppressed = true;

            // Deactivate EventSystem GameObject to block all game UI input
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
                Plugin.Log.LogDebug($"[AudioCueMenu] Deactivate error: {e.Message}");
            }

            string announcement = "Audio cue menu. Up and down to navigate, Enter to preview, Backspace to close.";
            announcement += $" {menuItems[0].Name}. {menuItems[0].Description}";
            SpeakDirect(announcement);
            Plugin.Log.LogInfo("[AudioCueMenu] Menu opened");
        }

        private void CloseMenu()
        {
            ClearPreview();
            DisposeAudio();
            isMenuOpen = false;

            // Reactivate the EventSystem GameObject
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
                Plugin.Log.LogDebug($"[AudioCueMenu] Reactivate error: {e.Message}");
                eventSystemObject = null;
            }

            // Wait 5 frames then do the full restore
            restoreStep = 5;

            ScreenReader.Suppressed = false;
            SpeakDirect("Menu closed");
            Plugin.Log.LogInfo("[AudioCueMenu] Menu closed, restore scheduled");
        }

        /// <summary>
        /// Called 5 frames after EventSystem reactivation.
        /// Toggles the InputSystemUIInputModule to force full reinitialization,
        /// then restores the saved selection.
        /// </summary>
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

                // Toggle the InputSystemUIInputModule off->on to reinitialize navigation actions
                var module = es.GetComponent<InputSystemUIInputModule>();
                if (module != null)
                {
                    module.enabled = false;
                    module.enabled = true;
                }
                else
                {
                    // Fallback: toggle all components that look like input modules
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

                // Restore selection
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
                Plugin.Log.LogDebug($"[AudioCueMenu] FinishRestore error: {e.Message}");
            }
            savedSelection = null;
        }

        private void NavigateMenu(int direction)
        {
            ClearPreview();

            selectedIndex += direction;
            if (selectedIndex < 0) selectedIndex = menuItems.Count - 1;
            if (selectedIndex >= menuItems.Count) selectedIndex = 0;

            var item = menuItems[selectedIndex];
            SpeakDirect($"{item.Name}. {item.Description}");
        }

        private void PlayCurrentPreview()
        {
            // Clear previous preview but keep audio device alive
            ClearPreview();
            var item = menuItems[selectedIndex];
            item.PlayPreview?.Invoke();
        }

        /// <summary>
        /// Stops current preview sound without disposing the audio device.
        /// The device stays alive for the next preview.
        /// </summary>
        private void ClearPreview()
        {
            isPreviewPlaying = false;
            enemyBeepsRemaining = 0;
            enemyGenerator = null;
            if (mixer != null)
                mixer.RemoveAllMixerInputs();
        }

        // --- Preview methods ---

        private void AddToMixer(ISampleProvider mono, float pan = 0f)
        {
            if (mixer == null) return;

            var panProvider = new PanningSampleProvider(mono) { Pan = pan };
            mixer.AddMixerInput(panProvider);
            isPreviewPlaying = true;
        }

        private void PreviewContinuousTone(double frequency, float pan)
        {
            try
            {
                var generator = new SineWaveGenerator(frequency, 0.25f);
                AddToMixer(generator, pan);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AudioCueMenu] PreviewContinuousTone error: {e.Message}");
            }
        }

        private void PreviewEnemyBeep(EnemyAudioType type, double frequency, int beepCount)
        {
            try
            {
                enemyGenerator = new EnemyAlertSoundGenerator();
                AddToMixer(enemyGenerator);

                currentEnemyType = type;
                enemyBeepsRemaining = beepCount;
                nextEnemyBeepTime = Time.unscaledTime;

                float totalDuration = type == EnemyAudioType.Boss ? beepCount * 0.5f + 0.5f :
                                      type == EnemyAudioType.Elite ? beepCount * 0.35f + 0.3f :
                                      type == EnemyAudioType.Loot ? beepCount * 0.3f + 0.3f :
                                      beepCount * 0.2f + 0.2f;
                previewEndTime = Time.unscaledTime + totalDuration;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AudioCueMenu] PreviewEnemyBeep error: {e.Message}");
            }
        }

        private void PreviewBeacon(float frequency, float interval)
        {
            try
            {
                var generator = new BeaconBeepGenerator();
                generator.Frequency = frequency;
                generator.Volume = 0.35f;
                generator.Interval = interval;
                generator.Active = true;

                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AudioCueMenu] PreviewBeacon error: {e.Message}");
            }
        }

        private void PreviewAlarm()
        {
            try
            {
                var generator = new AlarmSoundGenerator(800, 0.25f);
                generator.AlarmRate = 10;
                AddToMixer(generator);
                previewEndTime = Time.unscaledTime + 1.5f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AudioCueMenu] PreviewAlarm error: {e.Message}");
            }
        }

        // --- Game state check ---

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
            Plugin.Log.LogInfo("[AudioCueMenu] Destroyed");
        }
    }
}
