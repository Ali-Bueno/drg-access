using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Beep generator that handles timing internally in the audio thread.
    /// Produces short chirp beeps (descending frequency) distinct from enemy sine beeps.
    /// Set Frequency, Volume, and Interval from Update(); audio thread does the rest.
    /// </summary>
    public class BeaconBeepGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private readonly int sampleRate;
        private double phase;

        // Beep parameters (set from main thread)
        private volatile float targetFrequency = 600f;
        private volatile float targetVolume = 0f;
        private volatile int targetIntervalSamples;
        private volatile bool active = false;

        // Internal state (audio thread only)
        private int sampleCounter = 0;
        private int beepDurationSamples;
        private int intervalSamples;
        private float currentFrequency = 600f;
        private float currentVolume = 0f;

        public WaveFormat WaveFormat => waveFormat;

        public float Frequency
        {
            set => targetFrequency = Math.Max(100f, Math.Min(5000f, value));
        }

        public float Volume
        {
            set => targetVolume = Math.Max(0, Math.Min(1, value));
        }

        /// <summary>
        /// Set beep interval in seconds. Beeps repeat at this rate.
        /// </summary>
        public float Interval
        {
            set => targetIntervalSamples = (int)(sampleRate * Math.Max(0.02f, Math.Min(2f, value)));
        }

        public bool Active
        {
            set => active = value;
        }

        public BeaconBeepGenerator(int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            beepDurationSamples = (int)(sampleRate * 0.05); // 50ms beep
            intervalSamples = (int)(sampleRate * 0.25); // 250ms default
            targetIntervalSamples = intervalSamples;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Sync parameters from main thread
            currentFrequency = targetFrequency;
            currentVolume = targetVolume;
            intervalSamples = targetIntervalSamples;

            for (int i = 0; i < count; i++)
            {
                if (!active || currentVolume < 0.001f)
                {
                    buffer[offset + i] = 0;
                    continue;
                }

                bool isInBeep = sampleCounter < beepDurationSamples;

                if (isInBeep)
                {
                    float progress = (float)sampleCounter / beepDurationSamples;

                    // Sharp attack (5%), exponential decay
                    float envelope;
                    if (progress < 0.05f)
                        envelope = progress / 0.05f;
                    else
                        envelope = (float)Math.Exp(-(progress - 0.05) * 5.0);

                    // Chirp: frequency descends 25% during beep (distinct from enemy flat beeps)
                    double chirpFreq = (double)currentFrequency * (1.0 - progress * 0.25);

                    // Pure sine wave
                    double sample = Math.Sin(2.0 * Math.PI * phase);

                    buffer[offset + i] = (float)(currentVolume * envelope * sample);

                    phase += chirpFreq / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                }
                else
                {
                    buffer[offset + i] = 0;
                    phase = 0; // Reset phase for clean attack on next beep
                }

                sampleCounter++;
                if (sampleCounter >= intervalSamples)
                    sampleCounter = 0;
            }
            return count;
        }
    }

    /// <summary>
    /// Audio beacon for the Drop Pod using short chirp beeps.
    /// Beeps accelerate as the player approaches the pod.
    /// </summary>
    public class DropPodAudio : MonoBehaviour
    {
        public static DropPodAudio Instance { get; private set; }

        // Audio
        private WaveOutEvent outputDevice;
        private BeaconBeepGenerator beepGenerator;
        private PanningSampleProvider panProvider;
        private VolumeSampleProvider volumeProvider;

        // Drop pod reference
        private DropPod activePod;
        private bool isBeaconActive = false;
        private bool isLandingWarning = false;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State
        private bool isInitialized = false;
        private float maxDistance = 150f;

        // Landing warning
        private float landingWarningDuration = 5f;
        private float landingWarningEndTime = 0f;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";

        static DropPodAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DropPodAudio>();
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
            Plugin.Log.LogInfo("[DropPodAudio] Initialized");
        }

        void Start()
        {
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            try
            {
                beepGenerator = new BeaconBeepGenerator();
                panProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 1.0f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[DropPodAudio] Audio channel created (beacon chirp beeps)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] Failed to initialize: {e.Message}");
            }
        }

        void Update()
        {
            if (!isInitialized) return;

            try
            {
                CheckSceneChange();

                if (!IsInActiveGameplay())
                {
                    beepGenerator.Active = false;
                    return;
                }

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                {
                    beepGenerator.Active = false;
                    return;
                }

                if (isLandingWarning && activePod != null)
                {
                    if (Time.time < landingWarningEndTime)
                        UpdateLandingWarning();
                    else
                    {
                        isLandingWarning = false;
                        beepGenerator.Active = false;
                        Plugin.Log.LogInfo("[DropPodAudio] Landing warning ended");
                    }
                }
                else if (isBeaconActive && activePod != null)
                {
                    UpdateBeacon();
                }
                else
                {
                    beepGenerator.Active = false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] Update error: {e.Message}");
            }
        }

        public void OnPodDescending(DropPod pod)
        {
            activePod = pod;
            isLandingWarning = true;
            landingWarningEndTime = Time.time + landingWarningDuration;
            Plugin.Log.LogInfo("[DropPodAudio] Landing warning activated");
        }

        public void OnPodLanded(DropPod pod)
        {
            activePod = pod;
            isLandingWarning = false;
            isBeaconActive = true;
            Plugin.Log.LogInfo("[DropPodAudio] Beacon activated - extraction pod landed!");
        }

        public void OnPlayerEntered()
        {
            isBeaconActive = false;
            isLandingWarning = false;
            beepGenerator.Active = false;
            Plugin.Log.LogInfo("[DropPodAudio] Audio deactivated - player entered pod");
        }

        private (float distance, float pan) GetPodDirectionInfo()
        {
            var podTransform = activePod.podTransform;
            if (podTransform == null) return (-1f, 0f);

            Vector3 podPos = podTransform.position;
            Vector3 playerPos = playerTransform.position;
            float distance = Vector3.Distance(playerPos, podPos);

            Vector3 toPod = podPos - playerPos;
            toPod.y = 0;
            toPod.Normalize();

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0, -forward.x);

            float pan = Mathf.Clamp(Vector3.Dot(toPod, right), -1f, 1f);
            return (distance, pan);
        }

        private void UpdateLandingWarning()
        {
            try
            {
                var (distance, pan) = GetPodDirectionInfo();
                if (distance < 0) { beepGenerator.Active = false; return; }

                panProvider.Pan = pan;

                float proximityFactor = 1f - Mathf.Clamp01(distance / 50f);

                beepGenerator.Frequency = 1000 + proximityFactor * 400; // 1000-1400 Hz
                beepGenerator.Volume = 0.35f + proximityFactor * 0.2f; // 0.35-0.55
                beepGenerator.Interval = 0.08f; // Fast urgent beeps
                beepGenerator.Active = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] UpdateLandingWarning error: {e.Message}");
            }
        }

        private void UpdateBeacon()
        {
            try
            {
                var (distance, pan) = GetPodDirectionInfo();
                if (distance < 0) { beepGenerator.Active = false; return; }

                panProvider.Pan = pan;

                // Proximity (0 = far, 1 = close)
                float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                proximityFactor = proximityFactor * proximityFactor;

                // Interval: 250ms far → 30ms very close
                float interval = Mathf.Lerp(0.25f, 0.03f, proximityFactor);

                beepGenerator.Frequency = 500 + proximityFactor * 400; // 500-900 Hz
                beepGenerator.Volume = 0.25f + proximityFactor * 0.2f; // 0.25-0.45
                beepGenerator.Interval = interval;
                beepGenerator.Active = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] UpdateBeacon error: {e.Message}");
            }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[DropPodAudio] Scene changed to {currentScene} - resetting");
                    isBeaconActive = false;
                    isLandingWarning = false;
                    activePod = null;
                    landingWarningEndTime = 0f;
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
                    beepGenerator.Active = false;
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] CheckSceneChange error: {e.Message}");
            }
        }

        private void FindPlayer()
        {
            try
            {
                if (playerTransform == null)
                {
                    string[] playerNames = { "Player", "PlayerCharacter", "Hero", "Character" };
                    foreach (var name in playerNames)
                    {
                        var obj = GameObject.Find(name);
                        if (obj != null && !obj.name.Contains("Camera"))
                        {
                            playerTransform = obj.transform;
                            break;
                        }
                    }
                }
                if (cameraTransform == null)
                {
                    var cam = Camera.main;
                    if (cam != null) cameraTransform = cam.transform;
                }
            }
            catch { }
        }

        private bool IsInActiveGameplay()
        {
            try
            {
                if (Time.timeScale <= 0.1f) return false;

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
                {
                    var state = gameStateProvider.State;
                    return state == GameController.EGameState.CORE ||
                           state == GameController.EGameState.CORE_OUTRO;
                }
            }
            catch { }
            return false;
        }

        void OnDestroy()
        {
            try
            {
                outputDevice?.Stop();
                outputDevice?.Dispose();
            }
            catch { }
            Instance = null;
            Plugin.Log.LogInfo("[DropPodAudio] Destroyed");
        }
    }
}
