using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Audio beacon for escort mission phases after Bobby reaches destination:
    /// - TNT phase (stages 1-2): guides player to unarmed detonators
    /// - Ommoran phase (stage 3): guides player to crystals beaming Bobby
    /// Uses a single shared WaveOutEvent with BeaconBeepGenerator.
    /// Volume shares with DRILL_BEACON (same mission, never simultaneous).
    ///
    /// Phase detection is polling-driven (source of truth), with Harmony patches
    /// acting as accelerators — IL2CPP native-to-native calls can bypass patches,
    /// so nothing here may rely on a patch having fired. All announcements are
    /// guarded so patch + polling never double-announce.
    /// </summary>
    public class EscortPhaseAudio : MonoBehaviour
    {
        public static EscortPhaseAudio Instance { get; private set; }

        // Audio
        private WaveOutEvent outputDevice;
        private BeaconBeepGenerator beepGenerator;
        private PanningSampleProvider panProvider;
        private VolumeSampleProvider volumeProvider;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State
        private bool isInitialized = false;
        private float maxDistance = 80f;
        private float checkInterval = 0.2f;
        private float nextCheckTime = 0f;

        // Phase tracking
        private enum EscortPhase { None, TNT, Ommoran }
        private EscortPhase currentPhase = EscortPhase.None;
        private float nextPhasePollTime = 0f;
        private const float PHASE_POLL_INTERVAL = 2f;

        // Captured by patches when they fire (authoritative mission state source)
        private EscortMissionHandler missionHandler;

        // --- TNT Phase ---
        private int lastArmedCount = -1;
        private bool announcedArmTnt = false;

        // --- Ommoran Phase ---
        private OmmoranHeartstone activeHeartstone;
        private float lastHealthPercent = 1f;
        private bool announcedHp75, announcedHp50, announcedHp25, announcedHp10;
        private float nextHpCheckTime = 0f;
        private bool crystalsActive = false;
        private int lastCrystalCount = 0;
        private bool announcedOmmoranAppeared = false;
        private bool announcedOmmoranDestroyed = false;

        // Proximity announcement state (shared for both phases)
        private enum ProximityZone { None, Nearby, Closer, VeryClose }
        private ProximityZone lastAnnouncedZone = ProximityZone.None;
        private float lastAnnounceTime = 0f;
        private const float ANNOUNCE_COOLDOWN = 4f;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";
        private int lastRunGeneration = -1;

        static EscortPhaseAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<EscortPhaseAudio>();
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
            Plugin.Log.LogInfo("[EscortPhaseAudio] Initialized");
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
                beepGenerator.Mode = BeaconMode.TNT; // Will be switched as needed
                panProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 1.0f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[EscortPhaseAudio] Audio channel created");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EscortPhaseAudio] Failed to initialize: {e.Message}");
            }
        }

        // === Public API (called from patches AND from the polling fallback) ===

        /// <summary>Capture the mission handler so polling can read its state directly.</summary>
        public void SetMissionHandler(EscortMissionHandler handler)
        {
            if (handler != null) missionHandler = handler;
        }

        public void OnTNTPhaseStarted()
        {
            if (!announcedArmTnt)
            {
                announcedArmTnt = true;
                ScreenReader.Interrupt(ModLocalization.Get("escort_arm_tnt"));
            }
            EnterPhase(EscortPhase.TNT);
        }

        /// <summary>A detonator became live (OnDetonatorLive patch) — TNT phase is running.</summary>
        public void RegisterDetonator(TNTDetonator detonator)
        {
            OnTNTPhaseStarted();
        }

        /// <summary>
        /// Announce arming progress. Called by the OnTNTProgress patch and by polling.
        /// Guarded so the two sources never repeat an announcement.
        /// </summary>
        public void OnDetonatorArmed(int current, int target)
        {
            if (current < 1 || target < 1) return;
            if (current <= lastArmedCount) return;
            lastArmedCount = current;

            if (current >= target)
                ScreenReader.Interrupt(ModLocalization.Get("escort_tnt_all_armed"));
            else
                ScreenReader.Interrupt(ModLocalization.Get("escort_tnt_progress", current, target));
        }

        public void OnOmmoranPhaseStarted(OmmoranHeartstone heartstone)
        {
            if (heartstone != null) activeHeartstone = heartstone;

            if (!announcedOmmoranAppeared)
            {
                announcedOmmoranAppeared = true;
                ScreenReader.Interrupt(ModLocalization.Get("escort_ommoran_appeared"));
            }

            if (currentPhase != EscortPhase.Ommoran)
            {
                lastHealthPercent = 1f;
                announcedHp75 = announcedHp50 = announcedHp25 = announcedHp10 = false;
                crystalsActive = false;
                lastCrystalCount = 0;
                EnterPhase(EscortPhase.Ommoran);
            }
        }

        public void OnOmmoranDestroyed()
        {
            if (!announcedOmmoranDestroyed)
            {
                announcedOmmoranDestroyed = true;
                ScreenReader.Interrupt(ModLocalization.Get("escort_ommoran_destroyed"));
            }
            ExitPhase();
        }

        /// <summary>Crystals appeared. Called by the SpawnCrystals patch and by polling.</summary>
        public void OnCrystalsSpawned(int count)
        {
            if (count < 1 || crystalsActive) return;

            crystalsActive = true;
            lastCrystalCount = count;
            beepGenerator.Mode = BeaconMode.OmmoranCrystal;
            lastAnnouncedZone = ProximityZone.None;
            ScreenReader.Interrupt(ModLocalization.Get("escort_crystals_spawned", count));
        }

        /// <summary>Crystal destroyed. Called by the OnCrystalDeath patch and by polling.</summary>
        public void OnCrystalDestroyed(int remaining)
        {
            if (!crystalsActive || remaining >= lastCrystalCount) return;
            lastCrystalCount = remaining;

            if (remaining > 0)
            {
                ScreenReader.Interrupt(ModLocalization.Get("escort_crystal_destroyed", remaining));
            }
            else
            {
                ScreenReader.Interrupt(ModLocalization.Get("escort_crystals_cleared"));
                crystalsActive = false;
                beepGenerator.Active = false;
                lastAnnouncedZone = ProximityZone.None;
            }
        }

        // === Phase Management ===

        private void EnterPhase(EscortPhase phase)
        {
            if (currentPhase == phase) return;

            currentPhase = phase;
            lastAnnouncedZone = ProximityZone.None;

            if (phase == EscortPhase.TNT)
                beepGenerator.Mode = BeaconMode.TNT;
            else if (phase == EscortPhase.Ommoran)
                beepGenerator.Mode = BeaconMode.OmmoranCrystal;

            // Suppress drill beacon while escort phase is active
            if (DrillBeaconAudio.Instance != null)
                DrillBeaconAudio.Instance.SuppressForEscortPhase = true;

            Plugin.Log.LogInfo($"[EscortPhaseAudio] Entered {phase} phase");
        }

        private void ExitPhase()
        {
            if (currentPhase == EscortPhase.None) return;

            currentPhase = EscortPhase.None;
            beepGenerator.Active = false;
            activeHeartstone = null;
            crystalsActive = false;
            lastAnnouncedZone = ProximityZone.None;

            // Restore drill beacon
            if (DrillBeaconAudio.Instance != null)
                DrillBeaconAudio.Instance.SuppressForEscortPhase = false;

            Plugin.Log.LogInfo("[EscortPhaseAudio] Exited phase");
        }

        /// <summary>Full reset for a new stage/run (new GameController or scene).</summary>
        private void ResetForNewStage()
        {
            ExitPhase();
            missionHandler = null;
            lastArmedCount = -1;
            announcedArmTnt = false;
            announcedOmmoranAppeared = false;
            announcedOmmoranDestroyed = false;
            lastCrystalCount = 0;
        }

        // === Update Loop ===

        void Update()
        {
            if (!isInitialized) return;

            try
            {
                CheckSceneChange();

                // New GameController instance == new run/retry/stage: reset phase state
                if (GameStateHelper.RunGeneration != lastRunGeneration)
                {
                    lastRunGeneration = GameStateHelper.RunGeneration;
                    ResetForNewStage();
                }

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

                if (Time.time >= nextPhasePollTime)
                {
                    nextPhasePollTime = Time.time + PHASE_POLL_INTERVAL;
                    PollPhases();
                }

                if (currentPhase == EscortPhase.TNT)
                    UpdateTNTPhase();
                else if (currentPhase == EscortPhase.Ommoran)
                    UpdateOmmoranPhase();
                else
                    beepGenerator.Active = false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EscortPhaseAudio] Update error: {e.Message}");
            }
        }

        // === Phase Polling (source of truth) ===

        private void PollPhases()
        {
            // --- Ommoran phase (takes priority over TNT) ---
            try
            {
                // Validate cached heartstone (destroyed on stage end)
                if (activeHeartstone != null)
                {
                    try { var _ = activeHeartstone.state; }
                    catch { activeHeartstone = null; }
                }
                if (activeHeartstone == null)
                    activeHeartstone = UnityEngine.Object.FindObjectOfType<OmmoranHeartstone>();

                if (activeHeartstone != null)
                {
                    var st = activeHeartstone.state;
                    if (st == OmmoranHeartstone.OmmoranState.BASIC ||
                        st == OmmoranHeartstone.OmmoranState.FLINCH)
                    {
                        if (currentPhase != EscortPhase.Ommoran)
                        {
                            Plugin.Log.LogInfo("[EscortPhaseAudio] Ommoran phase detected by polling");
                            OnOmmoranPhaseStarted(activeHeartstone);
                        }

                        // Crystal fallback (SpawnCrystals/OnCrystalDeath patches may be
                        // bypassed by native-to-native calls)
                        var live = activeHeartstone.LiveCrystals;
                        int liveCount = live != null ? live.Count : 0;
                        if (!crystalsActive && liveCount > 0)
                            OnCrystalsSpawned(liveCount);
                        else if (crystalsActive && liveCount < lastCrystalCount)
                            OnCrystalDestroyed(liveCount);
                        return;
                    }

                    if (st == OmmoranHeartstone.OmmoranState.DEAD &&
                        currentPhase == EscortPhase.Ommoran)
                    {
                        OnOmmoranDestroyed();
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortPhaseAudio] Ommoran poll error: {e.Message}");
            }

            if (currentPhase == EscortPhase.Ommoran) return;

            // --- TNT phase ---
            // Authoritative source when a patch captured the handler:
            bool handlerSaysTnt = false, handlerKnown = false;
            try
            {
                if (missionHandler != null)
                {
                    handlerSaysTnt = missionHandler.state == EscortMissionHandler.EState.PREPARE_TNT;
                    handlerKnown = true;
                }
            }
            catch { missionHandler = null; }

            // Detonators are map blocks that exist from level generation, so their mere
            // presence must NOT trigger the phase (that suppressed the drill beacon for
            // the whole mission — the original escort bug). Only detonators whose Beacon
            // is active count: those are live and waiting to be armed.
            try
            {
                var detonators = UnityEngine.Object.FindObjectsOfType<TNTDetonator>();
                int liveUnarmed = 0, total = 0;
                if (detonators != null)
                {
                    total = detonators.Length;
                    foreach (var det in detonators)
                    {
                        if (det == null) continue;
                        try
                        {
                            var beacon = det.Beacon;
                            if (beacon != null && beacon.activeInHierarchy) liveUnarmed++;
                        }
                        catch { }
                    }
                }

                bool tntActive = handlerKnown ? handlerSaysTnt : liveUnarmed > 0;

                if (tntActive && currentPhase == EscortPhase.None)
                {
                    // Guard against beacons that might be active from level generation:
                    // TNT phase only happens after Bobby finished escorting.
                    if (!handlerKnown && !BobbyFinishedEscort()) return;

                    Plugin.Log.LogInfo($"[EscortPhaseAudio] TNT phase detected by polling ({liveUnarmed}/{total} live)");
                    if (lastArmedCount < 0) lastArmedCount = total - liveUnarmed;
                    OnTNTPhaseStarted();
                }
                else if (!tntActive && currentPhase == EscortPhase.TNT && liveUnarmed == 0)
                {
                    // All armed — announce (guarded) and hand control back to the drill beacon
                    OnDetonatorArmed(total, total);
                    ExitPhase();
                }
                else if (currentPhase == EscortPhase.TNT && liveUnarmed > 0 && total > 0)
                {
                    // Progress fallback when the OnTNTProgress patch is bypassed
                    OnDetonatorArmed(total - liveUnarmed, total);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortPhaseAudio] TNT poll error: {e.Message}");
            }
        }

        /// <summary>
        /// True when Bobby exists and is past the escort drive (TNT arming happens
        /// after Bobby reaches its destination). Used to reject false TNT-phase
        /// positives before/while Bobby is still drilling.
        /// </summary>
        private bool BobbyFinishedEscort()
        {
            try
            {
                var bobby = UnityEngine.Object.FindObjectOfType<Bobby>();
                if (bobby == null) return false;
                var st = bobby.State;
                return st != Bobby.EState.INTRO && st != Bobby.EState.ESCORT;
            }
            catch { return false; }
        }

        // === TNT Phase Logic ===

        private void UpdateTNTPhase()
        {
            if (Time.time < nextCheckTime) return;
            nextCheckTime = Time.time + checkInterval;

            // Find nearest unarmed detonator
            TNTDetonator nearest = null;
            float nearestDist = float.MaxValue;

            try
            {
                // Scan for active detonators in the scene
                var detonators = UnityEngine.Object.FindObjectsOfType<TNTDetonator>();
                if (detonators == null || detonators.Length == 0)
                {
                    beepGenerator.Active = false;
                    return;
                }

                Vector3 playerPos = playerTransform.position;

                foreach (var det in detonators)
                {
                    if (det == null) continue;

                    // Check if the detonator's beacon is still active (unarmed)
                    try
                    {
                        var beacon = det.Beacon;
                        if (beacon == null || !beacon.activeInHierarchy) continue;
                    }
                    catch { continue; }

                    float dist = Vector3.Distance(playerPos, det.transform.position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = det;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortPhaseAudio] TNT scan error: {e.Message}");
            }

            if (nearest == null)
            {
                beepGenerator.Active = false;
                return;
            }

            UpdateBeaconAudio(nearest.transform.position, nearestDist,
                400f, 800f, "escort_tnt");
        }

        // === Ommoran Phase Logic ===

        private void UpdateOmmoranPhase()
        {
            // Track heartstone HP
            if (activeHeartstone != null && Time.time >= nextHpCheckTime)
            {
                nextHpCheckTime = Time.time + 0.5f;
                TrackHeartstoneHP();
            }

            // If crystals are active, guide to nearest crystal
            if (crystalsActive && activeHeartstone != null)
            {
                if (Time.time < nextCheckTime) return;
                nextCheckTime = Time.time + checkInterval;

                UpdateCrystalBeacon();
            }
            else
            {
                beepGenerator.Active = false;
            }
        }

        private void TrackHeartstoneHP()
        {
            try
            {
                if (activeHeartstone == null) return;

                float hp = activeHeartstone.GetHealthNormalized();
                if (hp >= lastHealthPercent)
                {
                    lastHealthPercent = hp;
                    return;
                }

                lastHealthPercent = hp;
                float percent = hp * 100f;

                if (percent <= 10f && !announcedHp10)
                {
                    announcedHp10 = true;
                    ScreenReader.Interrupt(ModLocalization.Get("escort_ommoran_hp", 10));
                }
                else if (percent <= 25f && !announcedHp25)
                {
                    announcedHp25 = true;
                    ScreenReader.Say(ModLocalization.Get("escort_ommoran_hp", 25));
                }
                else if (percent <= 50f && !announcedHp50)
                {
                    announcedHp50 = true;
                    ScreenReader.Say(ModLocalization.Get("escort_ommoran_hp", 50));
                }
                else if (percent <= 75f && !announcedHp75)
                {
                    announcedHp75 = true;
                    ScreenReader.Say(ModLocalization.Get("escort_ommoran_hp", 75));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortPhaseAudio] HP tracking error: {e.Message}");
            }
        }

        private void UpdateCrystalBeacon()
        {
            try
            {
                var liveCrystals = activeHeartstone?.LiveCrystals;
                if (liveCrystals == null || liveCrystals.Count == 0)
                {
                    beepGenerator.Active = false;
                    return;
                }

                // Find nearest live crystal
                Vector3 playerPos = playerTransform.position;
                OmmoranCrystal nearest = null;
                float nearestDist = float.MaxValue;

                for (int i = 0; i < liveCrystals.Count; i++)
                {
                    var crystal = liveCrystals[i];
                    if (crystal == null) continue;

                    float dist = Vector3.Distance(playerPos, crystal.transform.position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = crystal;
                    }
                }

                if (nearest == null)
                {
                    beepGenerator.Active = false;
                    return;
                }

                UpdateBeaconAudio(nearest.transform.position, nearestDist,
                    700f, 1200f, "escort_crystal");
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EscortPhaseAudio] Crystal beacon error: {e.Message}");
            }
        }

        // === Shared Beacon Audio ===

        private void UpdateBeaconAudio(Vector3 targetPos, float distance,
            float freqLow, float freqHigh, string announcePrefix)
        {
            if (distance > maxDistance)
            {
                beepGenerator.Active = false;
                return;
            }

            Vector3 playerPos = playerTransform.position;

            // Direction
            Vector3 toTarget = targetPos - playerPos;
            toTarget.y = 0;
            toTarget.Normalize();

            Vector3 forward = AudioDirectionHelper.GetReferenceForward(cameraTransform);
            Vector3 right = new Vector3(forward.z, 0, -forward.x);

            float pan = Mathf.Clamp(Vector3.Dot(toTarget, right), -1f, 1f);
            panProvider.Pan = pan;

            // Proximity factor (0 = far, 1 = close)
            float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
            proximityFactor = proximityFactor * proximityFactor;

            // Interval: 250ms far → 30ms close
            float interval = Mathf.Lerp(0.25f, 0.03f, proximityFactor);

            float pitchMultiplier = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toTarget);

            beepGenerator.Frequency = (freqLow + proximityFactor * (freqHigh - freqLow)) * pitchMultiplier;
            beepGenerator.Volume = (0.20f + proximityFactor * 0.20f) * ModConfig.GetVolume(ModConfig.DRILL_BEACON);
            beepGenerator.Interval = interval;
            beepGenerator.Active = true;

            // Proximity announcements
            AnnounceProximity(distance, toTarget, forward, right, announcePrefix);
        }

        private void AnnounceProximity(float distance, Vector3 toTarget,
            Vector3 forward, Vector3 right, string prefix)
        {
            if (Time.time - lastAnnounceTime < ANNOUNCE_COOLDOWN) return;

            float ratio = distance / maxDistance;
            ProximityZone zone;

            if (ratio < 0.15f)
                zone = ProximityZone.VeryClose;
            else if (ratio < 0.35f)
                zone = ProximityZone.Closer;
            else if (ratio < 0.65f)
                zone = ProximityZone.Nearby;
            else
                zone = ProximityZone.None;

            if (zone == ProximityZone.None || zone == lastAnnouncedZone) return;

            string direction = GetDirectionString(toTarget, forward, right);
            string dirSuffix = string.IsNullOrEmpty(direction) ? "" : $" {direction}";

            string key = zone switch
            {
                ProximityZone.VeryClose => $"{prefix}_very_close",
                ProximityZone.Closer => $"{prefix}_closer",
                ProximityZone.Nearby => $"{prefix}_nearby",
                _ => null
            };

            if (key != null)
            {
                lastAnnouncedZone = zone;
                lastAnnounceTime = Time.time;
                ScreenReader.Say(ModLocalization.Get(key, dirSuffix));
            }
        }

        private string GetDirectionString(Vector3 toTarget, Vector3 forward, Vector3 right)
        {
            float fwdDot = Vector3.Dot(toTarget, forward);
            float rightDot = Vector3.Dot(toTarget, right);

            bool isFwd = fwdDot > 0.3f;
            bool isBack = fwdDot < -0.3f;
            bool isRight = rightDot > 0.3f;
            bool isLeft = rightDot < -0.3f;

            if (isFwd && isRight) return ModLocalization.Get("dir_up_right");
            if (isFwd && isLeft) return ModLocalization.Get("dir_up_left");
            if (isBack && isRight) return ModLocalization.Get("dir_down_right");
            if (isBack && isLeft) return ModLocalization.Get("dir_down_left");
            if (isFwd) return ModLocalization.Get("dir_up");
            if (isBack) return ModLocalization.Get("dir_down");
            if (isRight) return ModLocalization.Get("dir_right");
            if (isLeft) return ModLocalization.Get("dir_left");
            return ModLocalization.Get("dir_ahead");
        }

        // === Standard Infrastructure ===

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[EscortPhaseAudio] Scene changed to {currentScene} - resetting");
                    ResetForNewStage();
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EscortPhaseAudio] CheckSceneChange error: {e.Message}");
            }
        }

        private void FindPlayer()
        {
            try
            {
                if (playerTransform == null)
                {
                    // Player component lookup (name search broke in the Unity 6 update)
                    playerTransform = drgAccess.Helpers.PlayerLocator.FindPlayerTransform();
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
            return drgAccess.Helpers.GameStateHelper.IsInActiveGameplay();
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
            Plugin.Log.LogInfo("[EscortPhaseAudio] Destroyed");
        }
    }
}
