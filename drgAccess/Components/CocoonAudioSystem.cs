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
    /// Audio beacon for cocoons in Elimination mode.
    /// Guides the player to the nearest cocoon with organic pulsing beeps.
    /// Boss cocoon (BIG_COCOON) uses a higher frequency range to distinguish.
    /// </summary>
    public class CocoonAudioSystem : MonoBehaviour
    {
        public static CocoonAudioSystem Instance { get; private set; }

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

        // Current target
        private Enemy nearestCocoon;
        private bool isBossCocoon = false;

        // Proximity announcement state
        private enum ProximityZone { None, Nearby, Closer, VeryClose }
        private ProximityZone lastAnnouncedZone = ProximityZone.None;
        private float lastAnnounceTime = 0f;
        private const float ANNOUNCE_COOLDOWN = 4f;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";

        static CocoonAudioSystem()
        {
            ClassInjector.RegisterTypeInIl2Cpp<CocoonAudioSystem>();
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
            Plugin.Log.LogInfo("[CocoonAudioSystem] Initialized");
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
                beepGenerator.Mode = BeaconMode.Cocoon;
                panProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 1.0f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[CocoonAudioSystem] Audio channel created (cocoon beacon)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CocoonAudioSystem] Failed to initialize: {e.Message}");
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
                    nearestCocoon = null;
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
                    FindNearestCocoon();
                    nextCheckTime = Time.time + checkInterval;
                }

                if (nearestCocoon != null)
                    UpdateCocoonBeacon();
                else
                    beepGenerator.Active = false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CocoonAudioSystem] Update error: {e.Message}");
            }
        }

        private void FindNearestCocoon()
        {
            try
            {
                var tracker = EnemyTracker.Instance;
                if (tracker == null || tracker.GetCocoonCount() == 0)
                {
                    if (nearestCocoon != null)
                    {
                        nearestCocoon = null;
                        lastAnnouncedZone = ProximityZone.None;
                    }
                    return;
                }

                Vector3 playerPos = playerTransform.position;
                Enemy closest = null;
                float closestDist = float.MaxValue;
                bool closestIsBoss = false;

                foreach (var cocoon in tracker.GetActiveCocoons())
                {
                    if (cocoon == null || !cocoon.isAlive) continue;

                    float dist = Vector3.Distance(playerPos, cocoon.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = cocoon;
                        closestIsBoss = cocoon.type == EEnemyType.BIG_COCOON;
                    }
                }

                if (closest != nearestCocoon)
                {
                    lastAnnouncedZone = ProximityZone.None;
                }

                nearestCocoon = closest;
                isBossCocoon = closestIsBoss;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CocoonAudioSystem] FindNearestCocoon error: {e.Message}");
            }
        }

        private void UpdateCocoonBeacon()
        {
            try
            {
                Vector3 cocoonPos = nearestCocoon.position;
                Vector3 playerPos = playerTransform.position;
                float distance = Vector3.Distance(playerPos, cocoonPos);

                if (distance > maxDistance)
                {
                    beepGenerator.Active = false;
                    return;
                }

                // Direction calculation
                Vector3 toCocoon = cocoonPos - playerPos;
                toCocoon.y = 0;
                toCocoon.Normalize();

                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();
                Vector3 right = new Vector3(forward.z, 0, -forward.x);

                float pan = Mathf.Clamp(Vector3.Dot(toCocoon, right), -1f, 1f);
                panProvider.Pan = pan;

                // Proximity factor (0 = far, 1 = close)
                float proximityFactor = 1f - Mathf.Clamp01(distance / maxDistance);
                proximityFactor = proximityFactor * proximityFactor;

                // Interval: 250ms far → 30ms close
                float interval = Mathf.Lerp(0.25f, 0.03f, proximityFactor);

                float pitchMultiplier = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toCocoon);

                // Boss cocoon: higher frequency range (600-1000 Hz), regular: 400-700 Hz
                float baseFreqLow = isBossCocoon ? 600f : 400f;
                float baseFreqHigh = isBossCocoon ? 1000f : 700f;

                beepGenerator.Frequency = (baseFreqLow + proximityFactor * (baseFreqHigh - baseFreqLow)) * pitchMultiplier;
                beepGenerator.Volume = (0.30f + proximityFactor * 0.25f) * ModConfig.GetVolume(ModConfig.COCOON_BEACON);
                beepGenerator.Interval = interval;
                beepGenerator.Active = true;

                // Proximity announcements
                AnnounceProximity(distance, toCocoon, forward, right);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CocoonAudioSystem] UpdateCocoonBeacon error: {e.Message}");
            }
        }

        private void AnnounceProximity(float distance, Vector3 toCocoon, Vector3 forward, Vector3 right)
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

            // Get direction string
            string direction = GetDirectionString(toCocoon, forward, right);
            string dirSuffix = string.IsNullOrEmpty(direction) ? "" : $" {direction}";

            string prefix = isBossCocoon ? "boss_cocoon" : "cocoon";
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

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[CocoonAudioSystem] Scene changed to {currentScene} - resetting");
                    nearestCocoon = null;
                    isBossCocoon = false;
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
                    lastAnnouncedZone = ProximityZone.None;
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CocoonAudioSystem] CheckSceneChange error: {e.Message}");
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
            Plugin.Log.LogInfo("[CocoonAudioSystem] Destroyed");
        }
    }
}
