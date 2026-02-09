using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio beacon system for ActivationZone (supply pod zones).
    /// Helps player locate and stay inside the capture zone.
    /// </summary>
    public class ActivationZoneAudio : MonoBehaviour
    {
        public static ActivationZoneAudio Instance { get; private set; }

        // Audio channel
        private WaveOutEvent outputDevice;
        private SineWaveGenerator sineGenerator;
        private PanningSampleProvider panProvider;
        private VolumeSampleProvider volumeProvider;

        // Active zone reference
        private ActivationZone activeZone;
        private bool isZoneActive = false;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State tracking
        private bool isInitialized = false;

        // Audio parameters
        private float maxDistance = 100f; // Audible from 100m
        private float checkInterval = 0.1f; // Check zone status every 0.1s
        private float nextCheckTime = 0f;

        // Beeping control (enemy-style)
        private float nextBeepTime = 0f;
        private float beaconIntervalBase = 0.5f; // Base interval
        private float beaconIntervalMin = 0.1f; // Fast when close
        private float beepDuration = 0.06f; // Short beep like enemies (60ms)

        // Game state
        private IGameStateProvider gameStateProvider;

        // Scene tracking
        private string lastSceneName = "";

        static ActivationZoneAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<ActivationZoneAudio>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Plugin.Log.LogWarning("[ActivationZoneAudio] Duplicate instance - destroying");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Plugin.Log.LogInfo($"[ActivationZoneAudio] Awake - Initialized (GameObject: {gameObject.name})");
        }

        void OnEnable()
        {
            Plugin.Log.LogInfo("[ActivationZoneAudio] OnEnable called");
        }

        void OnDisable()
        {
            Plugin.Log.LogWarning("[ActivationZoneAudio] OnDisable called - audio will stop!");
        }

        void Start()
        {
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            try
            {
                // Create sine wave generator
                sineGenerator = new SineWaveGenerator(600, 0f); // Start at 600Hz, silenced

                // Add panning for 3D effect
                panProvider = new PanningSampleProvider(sineGenerator)
                {
                    Pan = 0f
                };

                // Add volume control
                volumeProvider = new VolumeSampleProvider(panProvider)
                {
                    Volume = 1.0f
                };

                // Create output device
                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[ActivationZoneAudio] Audio channel created");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ActivationZoneAudio] Failed to initialize: {e.Message}");
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
                        sineGenerator.Volume = 0f;
                    return;
                }

                // Search for player periodically
                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                    return;

                // Check for active zone periodically
                if (Time.time >= nextCheckTime)
                {
                    CheckForActiveZone();
                    nextCheckTime = Time.time + checkInterval;
                }

                // Update beacon if zone is active
                if (isZoneActive && activeZone != null)
                {
                    UpdateZoneBeacon();
                }
                else
                {
                    // No active zone - silence
                    if (sineGenerator != null)
                        sineGenerator.Volume = 0f;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ActivationZoneAudio] Update error: {e.Message}");
            }
        }

        private void CheckForActiveZone()
        {
            try
            {
                // Find all ActivationZones in scene
                var zones = UnityEngine.Object.FindObjectsOfType<ActivationZone>();

                ActivationZone nearestActiveZone = null;
                float nearestDistance = float.MaxValue;

                foreach (var zone in zones)
                {
                    if (zone == null) continue;

                    // Only consider INACTIVE or ACTIVATING zones (not DONE)
                    var state = zone.state;
                    if (state == ActivationZone.EState.DONE)
                        continue;

                    // Check distance to player
                    float distance = Vector3.Distance(playerTransform.position, zone.transform.position);

                    // If within detection range and closer than current nearest
                    if (distance < maxDistance && distance < nearestDistance)
                    {
                        nearestActiveZone = zone;
                        nearestDistance = distance;
                    }
                }

                // Update active zone
                if (nearestActiveZone != activeZone)
                {
                    if (nearestActiveZone != null)
                    {
                        Plugin.Log.LogInfo($"[ActivationZoneAudio] Found active zone at {nearestDistance:F1}m, state: {nearestActiveZone.state}");
                    }
                    else if (activeZone != null)
                    {
                        Plugin.Log.LogInfo("[ActivationZoneAudio] No active zone nearby");
                    }
                }

                activeZone = nearestActiveZone;
                isZoneActive = activeZone != null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ActivationZoneAudio] CheckForActiveZone error: {e.Message}");
            }
        }

        private void UpdateZoneBeacon()
        {
            try
            {
                Vector3 zonePos = activeZone.transform.position;
                Vector3 playerPos = playerTransform.position;
                float distance = Vector3.Distance(playerPos, zonePos);
                float radius = activeZone.radius;

                // Check if player is inside zone
                bool isInside = distance <= radius;

                // Calculate direction and pan for 3D audio
                Vector3 toZone = zonePos - playerPos;
                toZone.y = 0;
                toZone.Normalize();

                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float pan = Vector3.Dot(toZone, right);
                pan = Mathf.Clamp(pan, -1f, 1f);

                panProvider.Pan = pan;

                // Different audio based on state
                var state = activeZone.state;

                // INSIDE or ACTIVATING: Stop beeping (you're there!)
                if (isInside || state == ActivationZone.EState.ACTIVATING)
                {
                    sineGenerator.Volume = 0f; // Silent when inside or activating
                    return;
                }

                // OUTSIDE: Enemy-style beeps (short, fast when close)
                float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                proximityFactor = proximityFactor * proximityFactor; // Square for more dramatic change

                // Calculate interval (like enemies)
                float currentInterval = Mathf.Lerp(beaconIntervalBase, beaconIntervalMin, proximityFactor);

                // Check if it's time for a new beep
                if (Time.time >= nextBeepTime)
                {
                    // Play short beep (enemy-style)
                    // Volume increases with proximity
                    float volume = 0.18f + proximityFactor * 0.12f; // 0.18-0.3 (lower than drop pod)

                    // Frequency increases with proximity
                    double frequency = 350 + proximityFactor * 300; // 350-650 Hz (lower than drop pod)

                    sineGenerator.Frequency = frequency;
                    sineGenerator.Volume = volume;

                    nextBeepTime = Time.time + currentInterval;

                    if (Time.frameCount % 120 == 0)
                    {
                        Plugin.Log.LogInfo($"[ActivationZoneAudio] Zone beacon: dist={distance:F1}m, interval={currentInterval:F2}s, vol={volume:F2}, freq={frequency:F0}Hz");
                    }
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

                if (Time.frameCount % 120 == 0)
                {
                    Plugin.Log.LogInfo($"[ActivationZoneAudio] Zone beacon: dist={distance:F1}m, radius={radius:F1}m, inside={isInside}, state={state}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ActivationZoneAudio] UpdateZoneBeacon error: {e.Message}");
            }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[ActivationZoneAudio] Scene changed to {currentScene} - resetting");

                    // Reset state
                    isZoneActive = false;
                    activeZone = null;

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
                Plugin.Log.LogError($"[ActivationZoneAudio] CheckSceneChange error: {e.Message}");
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
                if (Time.timeScale <= 0.1f)
                    return false;

                // Validate gameStateProvider
                if (gameStateProvider != null)
                {
                    var gc = gameStateProvider.TryCast<GameController>();
                    if (gc == null)
                    {
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
                    }
                    else
                    {
                        return false;
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
            Plugin.Log.LogWarning($"[ActivationZoneAudio] OnDestroy called!");

            try
            {
                outputDevice?.Stop();
                outputDevice?.Dispose();
            }
            catch { }

            Instance = null;
            Plugin.Log.LogInfo("[ActivationZoneAudio] Destroyed");
        }
    }
}
