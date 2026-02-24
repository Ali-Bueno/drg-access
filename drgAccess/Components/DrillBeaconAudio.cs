using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio beacon for Bobby the drill during escort missions.
    /// Rhythmic chug/pulse that guides the player toward the drill.
    /// Follows ActivationZoneAudio pattern with state-aware behavior.
    /// </summary>
    public class DrillBeaconAudio : MonoBehaviour
    {
        public static DrillBeaconAudio Instance { get; private set; }

        // Audio
        private WaveOutEvent outputDevice;
        private BeaconBeepGenerator beepGenerator;
        private PanningSampleProvider panProvider;
        private VolumeSampleProvider volumeProvider;

        // Bobby reference
        private Bobby activeBobby;
        private bool isBeaconActive = false;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;
        private float nextBobbySearchTime = 0f;

        // State
        private bool isInitialized = false;

        // Bobby state tracking (polling)
        private Bobby.EState lastBobbyState = Bobby.EState.INTRO;
        private Bobby.EAnimState lastAnimState = Bobby.EAnimState.NONE;
        private bool wasPlayerInRange = true;
        private float lastOutOfRangeTime = 0f;

        // Fuel tracking
        private float lastFuelPercent = 1f;
        private bool fuelTrackingStarted = false;
        private bool announcedFuel50 = false;
        private bool announcedFuel25 = false;
        private bool announcedFuel10 = false;

        // Progress tracking
        private int lastProgressMilestone = 0; // 0, 25, 50, 75
        private float lastProgressAnnounceTime = 0f;

        // State announcement flags
        private bool announcedIntro = false;
        private bool announcedEscort = false;
        private bool announcedStopped = false;
        private bool announcedMiningHeart = false;
        private bool announcedBroken = false;
        private bool announcedOutro = false;

        // Audio parameters
        private float maxDistance = 80f;
        private float checkInterval = 0.1f;
        private float nextCheckTime = 0f;

        // NavMesh path result
        private NavMeshPathHelper.PathResult currentPathResult;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";

        private const float OUT_OF_RANGE_COOLDOWN = 5f;
        private const float PROGRESS_ANNOUNCE_MIN_INTERVAL = 10f;

        static DrillBeaconAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DrillBeaconAudio>();
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
            Plugin.Log.LogInfo("[DrillBeaconAudio] Initialized");
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
                beepGenerator.Mode = BeaconMode.Drill;
                panProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 1.0f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[DrillBeaconAudio] Audio channel created (drill beacon)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DrillBeaconAudio] Failed to initialize: {e.Message}");
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

                if (Time.time >= nextBobbySearchTime)
                {
                    FindBobby();
                    nextBobbySearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                {
                    beepGenerator.Active = false;
                    return;
                }

                if (activeBobby != null)
                {
                    TrackBobbyState();
                    HandleCompassKey();

                    if (isBeaconActive && Time.time >= nextCheckTime)
                    {
                        UpdateBeaconAudio();
                        nextCheckTime = Time.time + checkInterval;
                    }
                    else if (!isBeaconActive)
                    {
                        beepGenerator.Active = false;
                    }
                }
                else
                {
                    beepGenerator.Active = false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DrillBeaconAudio] Update error: {e.Message}");
            }
        }

        private void FindBobby()
        {
            try
            {
                if (activeBobby != null)
                {
                    // Validate existing reference
                    try { var _ = activeBobby.State; }
                    catch { activeBobby = null; }
                }

                if (activeBobby == null)
                {
                    activeBobby = UnityEngine.Object.FindObjectOfType<Bobby>();
                    if (activeBobby != null)
                    {
                        Plugin.Log.LogInfo("[DrillBeaconAudio] Found Bobby");
                        ResetTrackingState();
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DrillBeaconAudio] FindBobby error: {e.Message}");
                activeBobby = null;
            }
        }

        private void TrackBobbyState()
        {
            try
            {
                var state = activeBobby.State;
                var animState = activeBobby.animState;

                // State change announcements
                if (state != lastBobbyState)
                {
                    OnBobbyStateChanged(state);
                    lastBobbyState = state;
                }

                // Anim state change (for beacon sound variation)
                if (animState != lastAnimState)
                {
                    if (state == Bobby.EState.ESCORT)
                    {
                        if (animState == Bobby.EAnimState.STOPPED && !announcedStopped)
                        {
                            announcedStopped = true;
                            ScreenReader.Interrupt(ModLocalization.Get("drill_stopped"));
                        }
                        else if (animState == Bobby.EAnimState.RUNNING)
                        {
                            announcedStopped = false; // Allow re-announcement next time it stops
                        }
                    }
                    lastAnimState = animState;
                    beepGenerator.DrillRunning = animState == Bobby.EAnimState.RUNNING;
                }

                // Beacon active logic
                isBeaconActive = state == Bobby.EState.ESCORT || state == Bobby.EState.MINING_HEART;

                // Player range tracking
                if (state == Bobby.EState.ESCORT)
                {
                    TrackPlayerRange();
                    TrackFuel();
                    TrackProgress();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DrillBeaconAudio] TrackBobbyState error: {e.Message}");
            }
        }

        private void OnBobbyStateChanged(Bobby.EState newState)
        {
            switch (newState)
            {
                case Bobby.EState.INTRO:
                    if (!announcedIntro)
                    {
                        announcedIntro = true;
                        ScreenReader.Say(ModLocalization.Get("drill_arriving"));
                    }
                    break;

                case Bobby.EState.ESCORT:
                    if (!announcedEscort)
                    {
                        announcedEscort = true;
                        ScreenReader.Interrupt(ModLocalization.Get("drill_started"));
                        NavMeshPathHelper.Reset();
                    }
                    break;

                case Bobby.EState.MINING_HEART:
                    if (!announcedMiningHeart)
                    {
                        announcedMiningHeart = true;
                        ScreenReader.Interrupt(ModLocalization.Get("drill_mining"));
                    }
                    break;

                case Bobby.EState.BROKEN:
                    if (!announcedBroken)
                    {
                        announcedBroken = true;
                        ScreenReader.Interrupt(ModLocalization.Get("drill_broken"));
                    }
                    break;

                case Bobby.EState.OUTRO:
                    if (!announcedOutro)
                    {
                        announcedOutro = true;
                        ScreenReader.Interrupt(ModLocalization.Get("drill_finished"));
                    }
                    break;
            }
        }

        private void TrackPlayerRange()
        {
            try
            {
                bool inRange = activeBobby.playerInRange;

                if (!inRange && wasPlayerInRange)
                {
                    if (Time.time - lastOutOfRangeTime > OUT_OF_RANGE_COOLDOWN)
                    {
                        lastOutOfRangeTime = Time.time;
                        ScreenReader.Interrupt(ModLocalization.Get("drill_out_of_range"));
                    }
                }

                wasPlayerInRange = inRange;
            }
            catch { }
        }

        private void TrackFuel()
        {
            try
            {
                float fuel = activeBobby.fuel;
                if (fuel <= 0f && !fuelTrackingStarted) return; // No fuel system yet
                fuelTrackingStarted = true;

                float fuelPercent = Mathf.Clamp01(fuel / 100f); // Bobby.MAX_FUEL assumed as 100
                // Only announce when fuel is decreasing
                if (fuelPercent < lastFuelPercent)
                {
                    if (fuelPercent <= 0.10f && !announcedFuel10)
                    {
                        announcedFuel10 = true;
                        ScreenReader.Interrupt(ModLocalization.Get("drill_fuel_critical"));
                    }
                    else if (fuelPercent <= 0.25f && !announcedFuel25)
                    {
                        announcedFuel25 = true;
                        ScreenReader.Say(ModLocalization.Get("drill_fuel_25"));
                    }
                    else if (fuelPercent <= 0.50f && !announcedFuel50)
                    {
                        announcedFuel50 = true;
                        ScreenReader.Say(ModLocalization.Get("drill_fuel_50"));
                    }
                }
                else if (fuelPercent > lastFuelPercent + 0.05f)
                {
                    // Fuel refilled, reset flags above current level
                    if (fuelPercent > 0.50f) announcedFuel50 = false;
                    if (fuelPercent > 0.25f) announcedFuel25 = false;
                    if (fuelPercent > 0.10f) announcedFuel10 = false;
                }

                lastFuelPercent = fuelPercent;
            }
            catch { }
        }

        private void TrackProgress()
        {
            try
            {
                float totalDist = activeBobby.totalDistance;
                if (totalDist <= 0f) return;

                float progress = activeBobby.distMoved / totalDist;
                int milestone = progress >= 0.75f ? 75 : progress >= 0.50f ? 50 : progress >= 0.25f ? 25 : 0;

                if (milestone > lastProgressMilestone && Time.time - lastProgressAnnounceTime > PROGRESS_ANNOUNCE_MIN_INTERVAL)
                {
                    lastProgressMilestone = milestone;
                    lastProgressAnnounceTime = Time.time;

                    string msg = milestone == 50 ? ModLocalization.Get("drill_progress_halfway") : ModLocalization.Get("drill_progress_percent", milestone);
                    ScreenReader.Say(msg);
                }
            }
            catch { }
        }

        private void UpdateBeaconAudio()
        {
            try
            {
                Vector3 bobbyPos = activeBobby.transform.position;
                Vector3 playerPos = playerTransform.position;
                float directDistance = Vector3.Distance(playerPos, bobbyPos);

                // Use NavMesh pathfinding for guidance around obstacles
                currentPathResult = NavMeshPathHelper.GetNextWaypoint(playerPos, bobbyPos);
                float pathDistance = currentPathResult.IsValid ? currentPathResult.TotalPathDistance : directDistance;

                // Direction toward next waypoint (or Bobby directly)
                Vector3 targetPos = currentPathResult.IsValid ? currentPathResult.NextWaypoint : bobbyPos;
                Vector3 toTarget = targetPos - playerPos;
                toTarget.y = 0;
                toTarget.Normalize();

                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();
                Vector3 right = new Vector3(forward.z, 0, -forward.x);

                // Stereo panning
                float pan = Mathf.Clamp(Vector3.Dot(toTarget, right), -1f, 1f);
                panProvider.Pan = pan;

                // Proximity factor (0 = far, 1 = close)
                float proximityFactor = 1f - Mathf.Clamp01(pathDistance / maxDistance);
                proximityFactor = proximityFactor * proximityFactor;

                // Directional pitch modulation
                float pitchMultiplier = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toTarget);

                // Frequency: 200-450 Hz based on proximity + direction
                float baseFreqLow = activeBobby.animState == Bobby.EAnimState.RUNNING ? 250f : 200f;
                float baseFreqHigh = activeBobby.animState == Bobby.EAnimState.RUNNING ? 450f : 350f;
                beepGenerator.Frequency = (baseFreqLow + proximityFactor * (baseFreqHigh - baseFreqLow)) * pitchMultiplier;

                // Volume: gentle 0.20-0.40 to avoid annoyance
                beepGenerator.Volume = (0.20f + proximityFactor * 0.20f) * ModConfig.GetVolume(ModConfig.DRILL_BEACON);

                // Interval: 300ms far → 50ms close
                beepGenerator.Interval = Mathf.Lerp(0.30f, 0.05f, proximityFactor);

                beepGenerator.Active = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DrillBeaconAudio] UpdateBeaconAudio error: {e.Message}");
            }
        }

        private void HandleCompassKey()
        {
            try
            {
                if (!InputHelper.Compass()) return;
                if (!isBeaconActive) return;
                if (playerTransform == null) return;

                // Drop pod gets priority for compass if its beacon is active
                if (DropPodAudio.Instance != null && DropPodAudio.Instance.IsBeaconActive)
                    return;

                Vector3 bobbyPos = activeBobby.transform.position;
                Vector3 playerPos = playerTransform.position;

                float distance = currentPathResult.IsValid
                    ? currentPathResult.TotalPathDistance
                    : Vector3.Distance(playerPos, bobbyPos);

                string direction = GetScreenDirection(bobbyPos);
                int meters = Mathf.RoundToInt(distance);

                ScreenReader.Interrupt(ModLocalization.Get("drill_compass", direction, meters));
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[DrillBeaconAudio] HandleCompassKey error: {e.Message}");
            }
        }

        private string GetScreenDirection(Vector3 targetPos)
        {
            try
            {
                Vector3 waypointPos = currentPathResult.IsValid
                    ? currentPathResult.NextWaypoint
                    : targetPos;

                Vector3 toTarget = waypointPos - playerTransform.position;
                toTarget.y = 0;
                toTarget.Normalize();

                Vector3 screenUp = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                screenUp.y = 0;
                screenUp.Normalize();
                Vector3 screenRight = new Vector3(screenUp.z, 0, -screenUp.x);

                float upDot = Vector3.Dot(toTarget, screenUp);
                float rightDot = Vector3.Dot(toTarget, screenRight);

                bool isUp = upDot > 0.38f;
                bool isDown = upDot < -0.38f;
                bool isRight = rightDot > 0.38f;
                bool isLeft = rightDot < -0.38f;

                if (isUp && isRight) return ModLocalization.Get("dir_up_right");
                if (isUp && isLeft) return ModLocalization.Get("dir_up_left");
                if (isDown && isRight) return ModLocalization.Get("dir_down_right");
                if (isDown && isLeft) return ModLocalization.Get("dir_down_left");
                if (isUp) return ModLocalization.Get("dir_up");
                if (isDown) return ModLocalization.Get("dir_down");
                if (isRight) return ModLocalization.Get("dir_right");
                if (isLeft) return ModLocalization.Get("dir_left");
                return "here";
            }
            catch { return "unknown"; }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[DrillBeaconAudio] Scene changed to {currentScene} - resetting");
                    activeBobby = null;
                    isBeaconActive = false;
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    nextBobbySearchTime = 0f;
                    gameStateProvider = null;
                    beepGenerator.Active = false;
                    ResetTrackingState();
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DrillBeaconAudio] CheckSceneChange error: {e.Message}");
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

        private void ResetTrackingState()
        {
            lastBobbyState = Bobby.EState.INTRO;
            lastAnimState = Bobby.EAnimState.NONE;
            wasPlayerInRange = true;
            lastOutOfRangeTime = 0f;
            lastFuelPercent = 1f;
            fuelTrackingStarted = false;
            announcedFuel50 = false;
            announcedFuel25 = false;
            announcedFuel10 = false;
            lastProgressMilestone = 0;
            lastProgressAnnounceTime = 0f;
            announcedIntro = false;
            announcedEscort = false;
            announcedStopped = false;
            announcedMiningHeart = false;
            announcedBroken = false;
            announcedOutro = false;
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
            Plugin.Log.LogInfo("[DrillBeaconAudio] Destroyed");
        }
    }
}
