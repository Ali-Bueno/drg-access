using System;
using System.Collections.Generic;
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

        // --- TNT Phase ---
        private List<TNTDetonator> registeredDetonators = new List<TNTDetonator>();
        private bool tntPhaseDetectedByPatch = false;
        private float nextDetonatorScanTime = 0f;

        // --- Ommoran Phase ---
        private OmmoranHeartstone activeHeartstone;
        private float lastHealthPercent = 1f;
        private bool announcedHp75, announcedHp50, announcedHp25, announcedHp10;
        private float nextHpCheckTime = 0f;
        private bool crystalsActive = false;

        // Proximity announcement state (shared for both phases)
        private enum ProximityZone { None, Nearby, Closer, VeryClose }
        private ProximityZone lastAnnouncedZone = ProximityZone.None;
        private float lastAnnounceTime = 0f;
        private const float ANNOUNCE_COOLDOWN = 4f;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";

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

        // === Public API (called from patches) ===

        public void OnTNTPhaseStarted()
        {
            tntPhaseDetectedByPatch = true;
            EnterPhase(EscortPhase.TNT);
        }

        public void RegisterDetonator(TNTDetonator detonator)
        {
            if (detonator != null && !registeredDetonators.Contains(detonator))
                registeredDetonators.Add(detonator);

            // If patch for SetState didn't fire, detect TNT phase from detonator registration
            if (currentPhase == EscortPhase.None)
            {
                ScreenReader.Interrupt(ModLocalization.Get("escort_arm_tnt"));
                EnterPhase(EscortPhase.TNT);
            }
        }

        public void OnDetonatorArmed()
        {
            // Refresh the detonator list on next scan
            nextDetonatorScanTime = 0f;
        }

        public void OnOmmoranPhaseStarted(OmmoranHeartstone heartstone)
        {
            activeHeartstone = heartstone;
            lastHealthPercent = 1f;
            announcedHp75 = announcedHp50 = announcedHp25 = announcedHp10 = false;
            crystalsActive = false;
            EnterPhase(EscortPhase.Ommoran);
        }

        public void OnOmmoranDestroyed()
        {
            ExitPhase();
        }

        public void OnCrystalsSpawned()
        {
            crystalsActive = true;
            beepGenerator.Mode = BeaconMode.OmmoranCrystal;
            lastAnnouncedZone = ProximityZone.None;
        }

        public void OnCrystalDestroyed(int remaining)
        {
            if (remaining <= 0)
            {
                crystalsActive = false;
                beepGenerator.Active = false;
                lastAnnouncedZone = ProximityZone.None;
            }
        }

        // === Phase Management ===

        private void EnterPhase(EscortPhase phase)
        {
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
            currentPhase = EscortPhase.None;
            beepGenerator.Active = false;
            tntPhaseDetectedByPatch = false;
            registeredDetonators.Clear();
            activeHeartstone = null;
            crystalsActive = false;
            lastAnnouncedZone = ProximityZone.None;

            // Restore drill beacon
            if (DrillBeaconAudio.Instance != null)
                DrillBeaconAudio.Instance.SuppressForEscortPhase = false;

            Plugin.Log.LogInfo("[EscortPhaseAudio] Exited phase");
        }

        // === Update Loop ===

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

                // Fallback: poll for TNT detonators if patch didn't fire
                if (currentPhase == EscortPhase.None && !tntPhaseDetectedByPatch)
                    PollForTNTPhase();

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

        // === TNT Phase Logic ===

        private void PollForTNTPhase()
        {
            if (Time.time < nextDetonatorScanTime) return;
            nextDetonatorScanTime = Time.time + 2f;

            try
            {
                var detonators = UnityEngine.Object.FindObjectsOfType<TNTDetonator>();
                if (detonators != null && detonators.Length > 0)
                {
                    Plugin.Log.LogInfo("[EscortPhaseAudio] TNT detonators detected by polling fallback");
                    ScreenReader.Interrupt(ModLocalization.Get("escort_arm_tnt"));
                    EnterPhase(EscortPhase.TNT);
                }
            }
            catch { }
        }

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
                    // No detonators left — phase complete
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

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0;
            forward.Normalize();
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
                    ExitPhase();
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
            Plugin.Log.LogInfo("[EscortPhaseAudio] Destroyed");
        }
    }
}
