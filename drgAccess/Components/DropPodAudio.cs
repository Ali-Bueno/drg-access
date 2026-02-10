using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private volatile bool doubleBeep = false;

        // Internal state (audio thread only)
        private int sampleCounter = 0;
        private int beepDurationSamples;
        private int gapSamples;
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

        /// <summary>
        /// When true, plays two quick beeps per cycle instead of one (dit-dit pattern).
        /// </summary>
        public bool DoubleBeep
        {
            set => doubleBeep = value;
        }

        public BeaconBeepGenerator(int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            beepDurationSamples = (int)(sampleRate * 0.05); // 50ms beep
            gapSamples = (int)(sampleRate * 0.03); // 30ms gap between double beeps
            intervalSamples = (int)(sampleRate * 0.25); // 250ms default
            targetIntervalSamples = intervalSamples;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Sync parameters from main thread
            currentFrequency = targetFrequency;
            currentVolume = targetVolume;
            intervalSamples = targetIntervalSamples;
            bool isDouble = doubleBeep;

            // Double beep layout: [beep1][gap][beep2][silence...]
            int secondBeepStart = beepDurationSamples + gapSamples;
            int secondBeepEnd = secondBeepStart + beepDurationSamples;

            for (int i = 0; i < count; i++)
            {
                if (!active || currentVolume < 0.001f)
                {
                    buffer[offset + i] = 0;
                    continue;
                }

                // Determine if we're in a beep region
                bool isInFirstBeep = sampleCounter < beepDurationSamples;
                bool isInSecondBeep = isDouble &&
                    sampleCounter >= secondBeepStart &&
                    sampleCounter < secondBeepEnd;

                if (isInFirstBeep || isInSecondBeep)
                {
                    // Progress within the current beep (0-1)
                    int beepOffset = isInFirstBeep ? sampleCounter : (sampleCounter - secondBeepStart);
                    float progress = (float)beepOffset / beepDurationSamples;

                    // Sharp attack (5%), exponential decay
                    float envelope;
                    if (progress < 0.05f)
                        envelope = progress / 0.05f;
                    else
                        envelope = (float)Math.Exp(-(progress - 0.05) * 5.0);

                    // Chirp: frequency descends 25% during beep (distinct from enemy flat beeps)
                    // Second beep starts slightly higher for a "dit-DIT" effect
                    double freqBase = isInSecondBeep ? currentFrequency * 1.15 : currentFrequency;
                    double chirpFreq = freqBase * (1.0 - progress * 0.25);

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
    /// At very close range (< 3m), switches to a distinct continuous pulsing tone
    /// on the ramp position so the player knows exactly where to walk.
    /// </summary>
    public class DropPodAudio : MonoBehaviour
    {
        public static DropPodAudio Instance { get; private set; }

        // Shared audio output
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;

        // Beacon chirp (normal + critical distance)
        private BeaconBeepGenerator beepGenerator;
        private PanningSampleProvider beepPanProvider;

        // Ramp proximity tone (< 3m, continuous pulsing)
        private SineWaveGenerator rampToneGenerator;
        private PanningSampleProvider rampTonePanProvider;
        private VolumeSampleProvider rampToneVolumeProvider;

        // Drop pod reference
        private DropPod activePod;
        private bool isBeaconActive = false;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State
        private bool isInitialized = false;
        private float maxDistance = 150f;
        private const float CRITICAL_DISTANCE = 8f;
        private bool announcedCriticalProximity = false;
        private bool insidePodTonePlaying = false;

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
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                mixer = new MixingSampleProvider(format) { ReadFully = true };

                // Beacon chirp beeps (normal navigation)
                beepGenerator = new BeaconBeepGenerator();
                beepPanProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                var beepVolumeProvider = new VolumeSampleProvider(beepPanProvider) { Volume = 1.0f };
                mixer.AddMixerInput(beepVolumeProvider);

                // Ramp proximity tone (continuous pulsing, distinct from chirps)
                rampToneGenerator = new SineWaveGenerator(1600, 0f);
                rampTonePanProvider = new PanningSampleProvider(rampToneGenerator) { Pan = 0f };
                rampToneVolumeProvider = new VolumeSampleProvider(rampTonePanProvider) { Volume = 0f };
                mixer.AddMixerInput(rampToneVolumeProvider);

                outputDevice = new WaveOutEvent();
                outputDevice.Init(mixer);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[DropPodAudio] Audio initialized (beacon + ramp proximity tone)");
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
                    rampToneGenerator.Volume = 0f;
                    insidePodTonePlaying = false;
                    return;
                }

                // Update pulsing effect for inside-pod confirmation tone
                if (insidePodTonePlaying)
                {
                    float pulse = Mathf.Sin(Time.time * 8f);
                    rampToneGenerator.Frequency = 1400f + pulse * 200f;
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
                    rampToneGenerator.Volume = 0f;
                    return;
                }

                if (isBeaconActive && activePod != null)
                {
                    UpdateBeacon();
                    HandleCompassKey();
                }
                else
                {
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] Update error: {e.Message}");
            }
        }

        public void OnPodLanded(DropPod pod)
        {
            activePod = pod;
            isBeaconActive = true;
            announcedCriticalProximity = false;
            insidePodTonePlaying = false;
            Plugin.Log.LogInfo("[DropPodAudio] Beacon activated - extraction pod landed!");
        }

        public void OnPlayerEntered()
        {
            isBeaconActive = false;
            beepGenerator.Active = false;

            // Play continuous pulsing tone to confirm player is inside the pod
            insidePodTonePlaying = true;
            rampToneGenerator.Frequency = 1400f;
            rampToneGenerator.Volume = 0.35f;
            rampToneVolumeProvider.Volume = 1.0f;
            rampTonePanProvider.Pan = 0f;

            ScreenReader.Interrupt("Inside the pod");
            Plugin.Log.LogInfo("[DropPodAudio] Player entered pod - confirmation tone active");
        }

        /// <summary>
        /// Gets the target position for beacon guidance.
        /// Prefers the ramp (entry side) over the pod center, since the player
        /// needs to walk onto the ramp to enter the pod.
        /// </summary>
        private Vector3 GetBeaconTargetPosition()
        {
            try
            {
                // Prefer ramp detector position - that's where the player enters
                var ramp = activePod.rampDetector;
                if (ramp != null)
                    return ramp.position;
            }
            catch { }

            // Fall back to pod transform
            var podTransform = activePod.podTransform;
            return podTransform != null ? podTransform.position : Vector3.zero;
        }

        private (float distance, float pan, float facingDot) GetPodDirectionInfo()
        {
            Vector3 targetPos = GetBeaconTargetPosition();
            if (targetPos == Vector3.zero) return (-1f, 0f, 0f);

            Vector3 playerPos = playerTransform.position;
            float distance = Vector3.Distance(playerPos, targetPos);

            Vector3 toTarget = targetPos - playerPos;
            toTarget.y = 0;
            toTarget.Normalize();

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0, -forward.x);

            float pan = Mathf.Clamp(Vector3.Dot(toTarget, right), -1f, 1f);
            float facingDot = Vector3.Dot(forward, toTarget); // 1=facing target, -1=facing away
            return (distance, pan, facingDot);
        }

        /// <summary>
        /// Get screen-relative direction from player to pod ramp.
        /// Top-down game: camera is fixed, so "up" = W key direction, "right" = D key direction, etc.
        /// </summary>
        private string GetScreenDirection()
        {
            try
            {
                Vector3 targetPos = GetBeaconTargetPosition();
                if (targetPos == Vector3.zero) return "unknown";

                Vector3 toTarget = targetPos - playerTransform.position;
                toTarget.y = 0;
                toTarget.Normalize();

                // Camera forward projected to XZ = "up on screen" (W key direction)
                Vector3 screenUp = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                screenUp.y = 0;
                screenUp.Normalize();
                Vector3 screenRight = new Vector3(screenUp.z, 0, -screenUp.x);

                float upDot = Vector3.Dot(toTarget, screenUp);     // >0 = up on screen
                float rightDot = Vector3.Dot(toTarget, screenRight); // >0 = right on screen

                // Determine direction using thresholds
                bool isUp = upDot > 0.38f;
                bool isDown = upDot < -0.38f;
                bool isRight = rightDot > 0.38f;
                bool isLeft = rightDot < -0.38f;

                if (isUp && isRight) return "up-right";
                if (isUp && isLeft) return "up-left";
                if (isDown && isRight) return "down-right";
                if (isDown && isLeft) return "down-left";
                if (isUp) return "up";
                if (isDown) return "down";
                if (isRight) return "right";
                if (isLeft) return "left";
                return "here";
            }
            catch { return "unknown"; }
        }

        private void UpdateBeacon()
        {
            try
            {
                var (distance, pan, facingDot) = GetPodDirectionInfo();
                if (distance < 0)
                {
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
                    return;
                }

                // Top-down game: facingDot tells us if pod is "up" or "down" on screen
                // +1 = pod is toward top of screen (W direction), -1 = toward bottom (S direction)
                // Higher pitch = pod is above, lower pitch = pod is below
                // Combined with stereo panning (left/right), gives full 2D direction
                float verticalFactor = (facingDot + 1f) / 2f; // 0 = bottom, 1 = top
                float pitchMultiplier = 0.6f + verticalFactor * 0.4f;
                float facingVolumeMultiplier = 0.8f + verticalFactor * 0.2f;

                bool isCritical = distance < CRITICAL_DISTANCE;

                // Set panning for beacon chirps
                beepPanProvider.Pan = pan;

                if (isCritical)
                {
                    // Critical proximity: double-beep pattern, high pitch, louder
                    float criticalFactor = 1f - Mathf.Clamp01(distance / CRITICAL_DISTANCE);

                    beepGenerator.Frequency = (1200 + criticalFactor * 400) * pitchMultiplier;
                    beepGenerator.Volume = (0.4f + criticalFactor * 0.15f) * facingVolumeMultiplier;
                    beepGenerator.Interval = Mathf.Lerp(0.12f, 0.06f, criticalFactor);
                    beepGenerator.DoubleBeep = true;
                    beepGenerator.Active = true;

                    if (!announcedCriticalProximity)
                    {
                        announcedCriticalProximity = true;
                        ScreenReader.Interrupt("Drop pod very close");
                    }
                }
                else
                {
                    // Normal beacon chirps
                    float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                    proximityFactor = proximityFactor * proximityFactor;

                    float interval = Mathf.Lerp(0.25f, 0.03f, proximityFactor);

                    beepGenerator.Frequency = (800 + proximityFactor * 600) * pitchMultiplier;
                    beepGenerator.Volume = (0.25f + proximityFactor * 0.2f) * facingVolumeMultiplier;
                    beepGenerator.Interval = interval;
                    beepGenerator.DoubleBeep = false;
                    beepGenerator.Active = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] UpdateBeacon error: {e.Message}");
            }
        }

        private void HandleCompassKey()
        {
            try
            {
                var kb = Keyboard.current;
                if (kb == null) return;
                if (!kb[Key.F].wasPressedThisFrame) return;
                if (playerTransform == null) return;

                Vector3 targetPos = GetBeaconTargetPosition();
                if (targetPos == Vector3.zero) return;

                float distance = Vector3.Distance(playerTransform.position, targetPos);
                string direction = GetScreenDirection();
                int meters = Mathf.RoundToInt(distance);

                ScreenReader.Interrupt($"Drop pod: {direction}, {meters} meters");
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[DropPodAudio] HandleCompassKey error: {e.Message}");
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
                    activePod = null;
                    announcedCriticalProximity = false;
                    insidePodTonePlaying = false;
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
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
