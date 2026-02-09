using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio beacon for ActivationZone (supply pod zones).
    /// Sharp short beeps that accelerate as the player approaches.
    /// </summary>
    public class ActivationZoneAudio : MonoBehaviour
    {
        public static ActivationZoneAudio Instance { get; private set; }

        // Audio channel - sharp beeps like enemy audio
        private WaveOutEvent outputDevice;
        private EnemyAlertSoundGenerator beepGenerator;
        private PanningSampleProvider panProvider;
        private VolumeSampleProvider volumeProvider;

        // Active zone reference
        private ActivationZone activeZone;
        private bool isZoneActive = false;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State
        private bool isInitialized = false;

        // Audio parameters
        private float maxDistance = 100f;
        private float checkInterval = 0.1f;
        private float nextCheckTime = 0f;

        // Beeping control
        private float nextBeepTime = 0f;
        private float beaconIntervalBase = 0.25f; // 250ms when far
        private float beaconIntervalMin = 0.03f; // 30ms when very close

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";

        static ActivationZoneAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<ActivationZoneAudio>();
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
            Plugin.Log.LogInfo("[ActivationZoneAudio] Initialized");
        }

        void Start()
        {
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            try
            {
                beepGenerator = new EnemyAlertSoundGenerator();
                panProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 0.25f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[ActivationZoneAudio] Audio channel created (sharp beeps)");
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

                if (!IsInActiveGameplay())
                    return;

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null) return;

                if (Time.time >= nextCheckTime)
                {
                    CheckForActiveZone();
                    nextCheckTime = Time.time + checkInterval;
                }

                if (isZoneActive && activeZone != null)
                    UpdateZoneBeacon();
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
                var zones = UnityEngine.Object.FindObjectsOfType<ActivationZone>();
                ActivationZone nearestActiveZone = null;
                float nearestDistance = float.MaxValue;

                foreach (var zone in zones)
                {
                    if (zone == null) continue;
                    if (zone.state == ActivationZone.EState.DONE) continue;

                    float distance = Vector3.Distance(playerTransform.position, zone.transform.position);
                    if (distance < maxDistance && distance < nearestDistance)
                    {
                        nearestActiveZone = zone;
                        nearestDistance = distance;
                    }
                }

                if (nearestActiveZone != activeZone && nearestActiveZone != null)
                    Plugin.Log.LogInfo($"[ActivationZoneAudio] Found zone at {nearestDistance:F1}m");

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

                bool isInside = distance <= radius;
                var state = activeZone.state;

                // Silent when inside or activating
                if (isInside || state == ActivationZone.EState.ACTIVATING)
                    return;

                if (Time.time < nextBeepTime) return;

                // Direction and pan
                Vector3 toZone = zonePos - playerPos;
                toZone.y = 0;
                toZone.Normalize();

                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();
                Vector3 right = new Vector3(forward.z, 0, -forward.x);

                float pan = Mathf.Clamp(Vector3.Dot(toZone, right), -1f, 1f);
                panProvider.Pan = pan;

                // Proximity factor
                float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                proximityFactor = proximityFactor * proximityFactor;

                // Interval: 250ms far → 30ms close
                float currentInterval = Mathf.Lerp(beaconIntervalBase, beaconIntervalMin, proximityFactor);

                // Volume and frequency
                float volume = 0.18f + proximityFactor * 0.15f; // 0.18-0.33
                double frequency = 350 + proximityFactor * 300; // 350-650 Hz

                beepGenerator.Play(frequency, volume, EnemyAudioType.Normal);
                nextBeepTime = Time.time + currentInterval;
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
                    isZoneActive = false;
                    activeZone = null;
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
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
                    return gameStateProvider.State == GameController.EGameState.CORE;
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
            Plugin.Log.LogInfo("[ActivationZoneAudio] Destroyed");
        }
    }
}
