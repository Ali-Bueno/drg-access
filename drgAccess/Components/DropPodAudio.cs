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

        // Audio channel
        private WaveOutEvent outputDevice;
        private EnemyAlertSoundGenerator soundGenerator;
        private PanningSampleProvider panProvider;
        private VolumeSampleProvider volumeProvider;

        // Drop pod reference
        private DropPod activePod;
        private bool isBeaconActive = false;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State tracking
        private bool isInitialized = false;

        // Audio parameters - continuous beacon audible from far away
        private float maxDistance = 150f; // Very far to be audible across map
        private float minVolume = 0.15f; // Always audible even at max distance
        private float beepInterval = 0.8f; // Slow calm beeps
        private float nextBeepTime = 0f;

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
                // Create sound generator (using enemy system's generator)
                soundGenerator = new EnemyAlertSoundGenerator();

                // Add panning for 3D effect
                panProvider = new PanningSampleProvider(soundGenerator)
                {
                    Pan = 0f
                };

                // Add volume control
                volumeProvider = new VolumeSampleProvider(panProvider)
                {
                    Volume = 0.3f // Moderate volume
                };

                // Create output device
                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[DropPodAudio] Audio channel created");
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
                    if (Time.frameCount % 300 == 0 && isBeaconActive)
                        Plugin.Log.LogDebug($"[DropPodAudio] Beacon active but not in gameplay (waiting for CORE state)");
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
                    if (Time.frameCount % 300 == 0 && isBeaconActive)
                        Plugin.Log.LogDebug("[DropPodAudio] Beacon active but player not found");
                    return;
                }

                // If beacon is active and we have a pod, play beacon
                if (isBeaconActive && activePod != null)
                {
                    if (Time.frameCount % 60 == 0)
                        Plugin.Log.LogInfo("[DropPodAudio] Calling UpdateBeacon()");
                    UpdateBeacon();
                }
                else if (Time.frameCount % 300 == 0)
                {
                    Plugin.Log.LogDebug($"[DropPodAudio] Beacon state: active={isBeaconActive}, pod={activePod != null}");
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
                    activePod = null;
                    nextBeepTime = 0f;

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
        /// Called by patch when pod lands - start beacon.
        /// </summary>
        public void OnPodLanded(DropPod pod)
        {
            activePod = pod;
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
            Plugin.Log.LogInfo("[DropPodAudio] Beacon deactivated - player entered pod");
        }

        private void UpdateBeacon()
        {
            try
            {
                // Get pod position
                var podTransform = activePod.podTransform;
                if (podTransform == null)
                {
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

                // Play beacon beep if it's time
                if (Time.time >= nextBeepTime)
                {
                    // Volume: audible even from far away, louder when close
                    float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                    float volume = minVolume + proximityFactor * 0.35f; // 0.15 to 0.5

                    // Frequency: low calm beacon (200-350 Hz)
                    double frequency = 200 + proximityFactor * 150;

                    soundGenerator.Play(frequency, volume, EnemyAudioType.Elite);
                    nextBeepTime = Time.time + beepInterval;

                    if (Time.frameCount % 120 == 0)
                    {
                        Plugin.Log.LogInfo($"[DropPodAudio] Beacon at {distance:F1}m, vol={volume:F2}, freq={frequency:F0}Hz, pan={pan:F2}");
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

                // Validate gameStateProvider is still valid (not destroyed)
                if (gameStateProvider != null)
                {
                    try
                    {
                        var _ = gameStateProvider.State; // Test if destroyed
                    }
                    catch
                    {
                        Plugin.Log.LogInfo("[DropPodAudio] GameStateProvider destroyed, will search for new one");
                        gameStateProvider = null;
                    }
                }

                // Find game state provider if not cached
                if (gameStateProvider == null)
                {
                    var gameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (gameController != null)
                    {
                        gameStateProvider = gameController.Cast<IGameStateProvider>();
                        Plugin.Log.LogInfo("[DropPodAudio] Found new GameController");
                    }
                }

                if (gameStateProvider != null)
                {
                    var state = gameStateProvider.State;
                    return state == GameController.EGameState.CORE;
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
