using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio beacon system for the Drop Pod using sharp beeps (like enemy audio).
    /// - Landing warning: Rapid urgent beeps when pod is descending
    /// - Extraction beacon: Short beeps that accelerate as player approaches
    /// </summary>
    public class DropPodAudio : MonoBehaviour
    {
        public static DropPodAudio Instance { get; private set; }

        // Audio channel - sharp beeps like enemy audio
        private WaveOutEvent outputDevice;
        private EnemyAlertSoundGenerator beepGenerator;
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

        // Beacon intervals
        private float maxDistance = 150f;
        private float beaconIntervalBase = 0.25f; // 250ms when far
        private float beaconIntervalMin = 0.03f; // 30ms when very close
        private float nextBeepTime = 0f;

        // Landing warning
        private float landingWarningDuration = 5f;
        private float landingWarningEndTime = 0f;
        private float landingBeepInterval = 0.08f; // Fast beeps during landing

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
                // Use EnemyAlertSoundGenerator for sharp, distinct beeps
                beepGenerator = new EnemyAlertSoundGenerator();
                panProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 0.3f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[DropPodAudio] Audio channel created (sharp beeps)");
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
                    return;

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                    return;

                if (isLandingWarning && activePod != null)
                {
                    if (Time.time < landingWarningEndTime)
                        UpdateLandingWarning();
                    else
                    {
                        isLandingWarning = false;
                        Plugin.Log.LogInfo("[DropPodAudio] Landing warning ended");
                    }
                }
                else if (isBeaconActive && activePod != null)
                {
                    UpdateBeacon();
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
            nextBeepTime = 0f;
            Plugin.Log.LogInfo("[DropPodAudio] Landing warning activated");
        }

        public void OnPodLanded(DropPod pod)
        {
            activePod = pod;
            isLandingWarning = false;
            isBeaconActive = true;
            nextBeepTime = 0f;
            Plugin.Log.LogInfo("[DropPodAudio] Beacon activated - extraction pod landed!");
        }

        public void OnPlayerEntered()
        {
            isBeaconActive = false;
            isLandingWarning = false;
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
                if (distance < 0) return;

                panProvider.Pan = pan;

                if (Time.time >= nextBeepTime)
                {
                    float proximityFactor = 1f - Mathf.Clamp01(distance / 50f);

                    // Urgent high-pitched beeps
                    double frequency = 1000 + proximityFactor * 400; // 1000-1400 Hz
                    float volume = 0.4f + proximityFactor * 0.2f;

                    beepGenerator.Play(frequency, volume, EnemyAudioType.Normal);
                    nextBeepTime = Time.time + landingBeepInterval;
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
                var (distance, pan) = GetPodDirectionInfo();
                if (distance < 0) return;

                panProvider.Pan = pan;

                if (Time.time < nextBeepTime) return;

                // Proximity factor (0 = far, 1 = close)
                float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                proximityFactor = proximityFactor * proximityFactor;

                // Interval: 250ms far → 30ms close
                float currentInterval = Mathf.Lerp(beaconIntervalBase, beaconIntervalMin, proximityFactor);

                // Volume: louder when closer
                float volume = 0.2f + proximityFactor * 0.2f; // 0.2-0.4

                // Frequency: higher when closer
                double frequency = 500 + proximityFactor * 400; // 500-900 Hz

                beepGenerator.Play(frequency, volume, EnemyAudioType.Normal);
                nextBeepTime = Time.time + currentInterval;
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
                    nextBeepTime = 0f;
                    landingWarningEndTime = 0f;
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
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
