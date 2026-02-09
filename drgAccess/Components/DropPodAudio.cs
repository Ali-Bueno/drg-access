using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio beacon system for the Drop Pod.
    /// - Landing warning: Fast urgent beeps when pod is descending (danger zone)
    /// - Extraction beacon: Calm homing signal when pod is waiting for player
    /// </summary>
    public class DropPodAudio : MonoBehaviour
    {
        public static DropPodAudio Instance { get; private set; }

        // Audio channel - using continuous tone like wall navigation
        private WaveOutEvent outputDevice;
        private SineWaveGenerator sineGenerator;
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

        // State tracking
        private bool isInitialized = false;

        // Audio parameters - beacon (enemy-style)
        private float maxDistance = 150f; // Very far to be audible across map
        private float beaconIntervalBase = 0.6f; // Base interval when far
        private float beaconIntervalMin = 0.08f; // Fast when close (like enemies)
        private float nextBeepTime = 0f;
        private float beepDuration = 0.06f; // Very short beep like enemies (60ms)

        // Landing warning parameters
        private float landingWarningDuration = 5f; // Warning lasts 5 seconds
        private float landingWarningEndTime = 0f;

        // Game state
        private IGameStateProvider gameStateProvider;

        static DropPodAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DropPodAudio>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Plugin.Log.LogWarning("[DropPodAudio] Duplicate instance - destroying");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Plugin.Log.LogInfo($"[DropPodAudio] Awake - Initialized (GameObject: {gameObject.name})");
        }

        void OnEnable()
        {
            Plugin.Log.LogInfo("[DropPodAudio] OnEnable called");
        }

        void OnDisable()
        {
            Plugin.Log.LogWarning("[DropPodAudio] OnDisable called - audio will stop!");
        }

        void Start()
        {
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            try
            {
                // Create sine wave generator for continuous beacon tone
                sineGenerator = new SineWaveGenerator(800, 0f); // Start at 800Hz, silenced

                // Add panning for 3D effect
                panProvider = new PanningSampleProvider(sineGenerator)
                {
                    Pan = 0f
                };

                // Add volume control
                volumeProvider = new VolumeSampleProvider(panProvider)
                {
                    Volume = 1.0f // Full volume, control via sine generator
                };

                // Create output device
                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[DropPodAudio] Audio channel created (continuous tone)");
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

                // Only during active gameplay
                if (!IsInActiveGameplay())
                {
                    if (sineGenerator != null)
                        sineGenerator.Volume = 0f; // Silence when not in gameplay
                    if (Time.frameCount % 300 == 0 && (isBeaconActive || isLandingWarning))
                        Plugin.Log.LogDebug($"[DropPodAudio] Audio active but not in gameplay (waiting for CORE state)");
                    return;
                }

                // Search for player periodically
                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                {
                    if (Time.frameCount % 300 == 0 && (isBeaconActive || isLandingWarning))
                        Plugin.Log.LogDebug("[DropPodAudio] Audio active but player not found");
                    return;
                }

                // Landing warning (higher priority - danger zone!)
                if (isLandingWarning && activePod != null)
                {
                    if (Time.time < landingWarningEndTime)
                    {
                        UpdateLandingWarning();
                    }
                    else
                    {
                        isLandingWarning = false;
                        Plugin.Log.LogInfo("[DropPodAudio] Landing warning ended");
                    }
                }
                // If beacon is active and we have a pod, play beacon
                else if (isBeaconActive && activePod != null)
                {
                    if (Time.frameCount % 60 == 0)
                        Plugin.Log.LogInfo("[DropPodAudio] Calling UpdateBeacon()");
                    UpdateBeacon();
                }
                else if (Time.frameCount % 300 == 0)
                {
                    Plugin.Log.LogDebug($"[DropPodAudio] State: beacon={isBeaconActive}, warning={isLandingWarning}, pod={activePod != null}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] Update error: {e.Message}");
            }
        }

        // Scene tracking
        private string lastSceneName = "";

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[DropPodAudio] Scene changed to {currentScene} - resetting");

                    // Reset beacon state
                    isBeaconActive = false;
                    isLandingWarning = false;
                    activePod = null;
                    nextBeepTime = 0f;
                    landingWarningEndTime = 0f;

                    // Reset player references
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;

                    // Reset game state
                    gameStateProvider = null;
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] CheckSceneChange error: {e.Message}");
            }
        }

        /// <summary>
        /// Called by patch when pod starts descending - start landing warning.
        /// </summary>
        public void OnPodDescending(DropPod pod)
        {
            activePod = pod;
            isLandingWarning = true;
            landingWarningEndTime = Time.time + landingWarningDuration;
            nextBeepTime = 0f; // Play immediately
            Plugin.Log.LogInfo("[DropPodAudio] Landing warning activated - pod descending (DANGER ZONE!)");
        }

        /// <summary>
        /// Called by patch when pod lands - start beacon.
        /// </summary>
        public void OnPodLanded(DropPod pod)
        {
            activePod = pod;
            isLandingWarning = false; // Stop warning
            isBeaconActive = true;
            nextBeepTime = 0f; // Play immediately
            Plugin.Log.LogInfo("[DropPodAudio] Beacon activated - extraction pod landed!");
        }

        /// <summary>
        /// Called by patch when player enters pod - stop beacon.
        /// </summary>
        public void OnPlayerEntered()
        {
            isBeaconActive = false;
            isLandingWarning = false;
            if (sineGenerator != null)
                sineGenerator.Volume = 0f; // Silence immediately
            Plugin.Log.LogInfo("[DropPodAudio] Audio deactivated - player entered pod");
        }

        private void UpdateLandingWarning()
        {
            try
            {
                // Get pod position
                var podTransform = activePod.podTransform;
                if (podTransform == null)
                {
                    sineGenerator.Volume = 0f; // Silence if no pod
                    if (Time.frameCount % 60 == 0)
                        Plugin.Log.LogWarning("[DropPodAudio] Pod transform is null (landing warning)");
                    return;
                }

                Vector3 podPos = podTransform.position;
                Vector3 playerPos = playerTransform.position;
                float distance = Vector3.Distance(playerPos, podPos);

                // Calculate direction and pan for 3D audio
                Vector3 toPod = podPos - playerPos;
                toPod.y = 0;
                toPod.Normalize();

                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float pan = Vector3.Dot(toPod, right);
                pan = Mathf.Clamp(pan, -1f, 1f);

                panProvider.Pan = pan;

                // URGENT WARNING: Pulsing alarm sound
                float proximityFactor = 1f - Mathf.Clamp01(distance / 50f);

                // Pulsing effect: volume oscillates for urgency
                float pulseSpeed = 8f; // Fast pulsing
                float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // 0-1
                float baseVolume = 0.6f + proximityFactor * 0.3f; // 0.6 to 0.9
                float volume = baseVolume * (0.5f + pulse * 0.5f); // Pulsing between 50-100% of base

                // High urgent frequency
                double frequency = 900 + proximityFactor * 500; // 900-1400 Hz

                sineGenerator.Frequency = frequency;
                sineGenerator.Volume = volume;

                if (Time.frameCount % 60 == 0)
                {
                    Plugin.Log.LogInfo($"[DropPodAudio] LANDING WARNING at {distance:F1}m, vol={volume:F2}, freq={frequency:F0}Hz");
                }
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
                // Get pod position
                var podTransform = activePod.podTransform;
                if (podTransform == null)
                {
                    sineGenerator.Volume = 0f; // Silence if no pod
                    if (Time.frameCount % 60 == 0)
                        Plugin.Log.LogWarning("[DropPodAudio] Pod transform is null");
                    return;
                }

                Vector3 podPos = podTransform.position;
                Vector3 playerPos = playerTransform.position;
                float distance = Vector3.Distance(playerPos, podPos);

                // Calculate direction and pan for 3D audio
                Vector3 toPod = podPos - playerPos;
                toPod.y = 0;
                toPod.Normalize();

                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float pan = Vector3.Dot(toPod, right);
                pan = Mathf.Clamp(pan, -1f, 1f);

                panProvider.Pan = pan;

                // Calculate proximity factor (0 = far, 1 = close)
                float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                proximityFactor = proximityFactor * proximityFactor; // Square for more dramatic change

                // Calculate interval (like enemies - faster when closer)
                float currentInterval = Mathf.Lerp(beaconIntervalBase, beaconIntervalMin, proximityFactor);

                // Check if it's time for a new beep
                if (Time.time >= nextBeepTime)
                {
                    // Play short beep (enemy-style)
                    // Volume increases with proximity (like enemies)
                    float volume = 0.2f + proximityFactor * 0.15f; // 0.2-0.35

                    // Frequency increases with proximity (like enemies)
                    double frequency = 500 + proximityFactor * 400; // 500-900 Hz (lower than enemies)

                    sineGenerator.Frequency = frequency;
                    sineGenerator.Volume = volume;

                    nextBeepTime = Time.time + currentInterval;

                    if (Time.frameCount % 60 == 0)
                    {
                        Plugin.Log.LogInfo($"[DropPodAudio] BEACON! dist={distance:F1}m, interval={currentInterval:F2}s, vol={volume:F2}, freq={frequency:F0}Hz");
                    }

                    // Schedule silence after beep duration
                    // (In next frame, will check if beep is over)
                }
                else
                {
                    // Check if beep duration has passed
                    float timeSinceBeep = Time.time - (nextBeepTime - currentInterval);
                    if (timeSinceBeep > beepDuration)
                    {
                        sineGenerator.Volume = 0f; // Silence between beeps
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] UpdateBeacon error: {e.Message}");
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
                    if (cam != null)
                    {
                        cameraTransform = cam.transform;
                    }
                }
            }
            catch { }
        }

        private bool IsInActiveGameplay()
        {
            try
            {
                // Primary check: time scale (catches pause)
                if (Time.timeScale <= 0.1f)
                {
                    return false;
                }

                // Validate gameStateProvider using Unity's null check (IL2CPP-safe)
                if (gameStateProvider != null)
                {
                    // Try to cast back to GameController to test if destroyed
                    var gc = gameStateProvider.TryCast<GameController>();
                    if (gc == null) // Unity's overloaded null check detects destroyed objects
                    {
                        if (Time.frameCount % 300 == 0)
                            Plugin.Log.LogInfo("[DropPodAudio] GameController was destroyed, searching for new one");
                        gameStateProvider = null;
                    }
                }

                // Find game state provider if not cached (search every frame until found!)
                if (gameStateProvider == null)
                {
                    var gameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (gameController != null)
                    {
                        gameStateProvider = gameController.Cast<IGameStateProvider>();
                        Plugin.Log.LogInfo("[DropPodAudio] Found new GameController");
                    }
                    else
                    {
                        // Not found yet, keep searching next frame
                        return false;
                    }
                }

                if (gameStateProvider != null)
                {
                    var state = gameStateProvider.State;
                    // Drop pod beacon needs to work during:
                    // - CORE: normal gameplay
                    // - CORE_OUTRO: extraction phase (countdown to enter pod) - CRITICAL!
                    return state == GameController.EGameState.CORE ||
                           state == GameController.EGameState.CORE_OUTRO;
                }
            }
            catch { }

            return false;
        }

        void OnDestroy()
        {
            Plugin.Log.LogWarning($"[DropPodAudio] OnDestroy called! Stack trace: {UnityEngine.StackTraceUtility.ExtractStackTrace()}");

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
