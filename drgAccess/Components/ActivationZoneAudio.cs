using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio beacon for ActivationZone (supply pod zones).
    /// Short chirp beeps that accelerate as the player approaches.
    /// Uses BeaconBeepGenerator for reliable audio-thread timing.
    /// </summary>
    public class ActivationZoneAudio : MonoBehaviour
    {
        public static ActivationZoneAudio Instance { get; private set; }

        // Audio
        private WaveOutEvent outputDevice;
        private BeaconBeepGenerator beepGenerator;
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

        // Zone tracking state
        private bool wasInsideZone = false;
        private ActivationZone.EState lastZoneState = ActivationZone.EState.INACTIVE;
        private bool announcedCompletion = false;
        private float lastProgressTime = 0f;
        private int lastRocksAnnounced = -1;

        // Audio parameters
        private float maxDistance = 100f;
        private float checkInterval = 0.1f;
        private float nextCheckTime = 0f;

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
                beepGenerator = new BeaconBeepGenerator();
                beepGenerator.Mode = BeaconMode.SupplyDrop;
                panProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 1.0f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[ActivationZoneAudio] Audio channel created (beacon chirp beeps)");
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

                if (Time.time >= nextCheckTime)
                {
                    CheckForActiveZone();
                    nextCheckTime = Time.time + checkInterval;
                }

                if (isZoneActive && activeZone != null)
                    UpdateZoneBeacon();
                else
                    beepGenerator.Active = false;
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

                if (nearestActiveZone != activeZone)
                {
                    if (nearestActiveZone != null)
                        Plugin.Log.LogInfo($"[ActivationZoneAudio] Found zone at {nearestDistance:F1}m");
                    // Reset tracking state for new zone
                    ResetZoneTracking();
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

                bool isInside = distance <= radius;
                var state = activeZone.state;

                // Announce enter/exit transitions
                if (isInside && !wasInsideZone)
                {
                    ScreenReader.Interrupt("Inside supply zone");
                    lastRocksAnnounced = -1;
                }
                else if (!isInside && wasInsideZone)
                {
                    if (state == ActivationZone.EState.ACTIVATING)
                        ScreenReader.Interrupt("Left supply zone, return to zone");
                    else
                        ScreenReader.Interrupt("Left supply zone");
                }
                wasInsideZone = isInside;

                // Announce state transitions
                if (state != lastZoneState)
                {
                    if (state == ActivationZone.EState.ACTIVATING)
                    {
                        AnnounceActivating();
                    }
                    else if (state == ActivationZone.EState.DONE && !announcedCompletion)
                    {
                        announcedCompletion = true;
                        ScreenReader.Interrupt("Supply zone complete");
                    }
                    lastZoneState = state;
                }

                // Announce progress during activation
                if (state == ActivationZone.EState.ACTIVATING)
                    AnnounceProgress();

                // Beacon behavior depends on state and position
                if (state == ActivationZone.EState.DONE)
                {
                    beepGenerator.Active = false;
                    return;
                }

                if (isInside)
                {
                    // Inside zone: silence beacon (player is where they need to be)
                    beepGenerator.Active = false;
                    return;
                }

                // Outside zone (INACTIVE or ACTIVATING): guide player to zone
                UpdateBeaconAudio(zonePos, playerPos, distance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ActivationZoneAudio] UpdateZoneBeacon error: {e.Message}");
            }
        }

        private void UpdateBeaconAudio(Vector3 zonePos, Vector3 playerPos, float distance)
        {
            Vector3 toZone = zonePos - playerPos;
            toZone.y = 0;
            toZone.Normalize();

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0, -forward.x);

            float pan = Mathf.Clamp(Vector3.Dot(toZone, right), -1f, 1f);
            panProvider.Pan = pan;

            // Proximity factor (0 = far, 1 = close)
            float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
            proximityFactor = proximityFactor * proximityFactor;

            // Interval: 250ms far → 30ms close
            float interval = Mathf.Lerp(0.25f, 0.03f, proximityFactor);

            float pitchMultiplier = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toZone);
            beepGenerator.Frequency = (350 + proximityFactor * 300) * pitchMultiplier; // 350-650 Hz * direction
            beepGenerator.Volume = 0.2f + proximityFactor * 0.18f; // 0.2-0.38
            beepGenerator.Interval = interval;
            beepGenerator.Active = true;
        }

        private void AnnounceActivating()
        {
            try
            {
                var rocks = activeZone.rocksInArea;
                int rockCount = rocks != null ? rocks.Count : 0;
                if (rockCount > 0)
                    ScreenReader.Say($"Clearing zone, {rockCount} rocks to mine");
                else
                    ScreenReader.Say("Zone activating");
            }
            catch { }
        }

        private void AnnounceProgress()
        {
            try
            {
                // Announce rocks remaining (list shrinks as rocks are mined)
                var rocks = activeZone.rocksInArea;
                int remaining = rocks != null ? rocks.Count : 0;
                if (remaining != lastRocksAnnounced && lastRocksAnnounced >= 0)
                {
                    lastRocksAnnounced = remaining;
                    if (remaining > 0)
                        ScreenReader.Say($"{remaining} rocks remaining");
                    else
                        ScreenReader.Say("All rocks cleared");
                    return;
                }
                if (lastRocksAnnounced < 0)
                    lastRocksAnnounced = remaining;

                // Announce timer milestones every 10 seconds
                float timeLeft = activeZone.timeLeft;
                if (timeLeft > 0 && Time.time - lastProgressTime >= 10f)
                {
                    lastProgressTime = Time.time;
                    int seconds = Mathf.RoundToInt(timeLeft);
                    if (seconds > 0)
                        ScreenReader.Say($"{seconds} seconds remaining");
                }
            }
            catch { }
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
                    ResetZoneTracking();
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

        private void ResetZoneTracking()
        {
            wasInsideZone = false;
            lastZoneState = ActivationZone.EState.INACTIVE;
            announcedCompletion = false;
            lastProgressTime = 0f;
            lastRocksAnnounced = -1;
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
