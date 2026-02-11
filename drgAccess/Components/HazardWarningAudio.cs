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
    /// Alarm sound generator that alternates between two frequencies
    /// to create a siren-like warning effect.
    /// </summary>
    public class AlarmSoundGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private double phase;
        private double targetFrequency;
        private double currentFrequency;
        private float targetVolume;
        private float currentVolume;
        private readonly int sampleRate;

        // Alarm modulation
        private double alarmPhase;
        private double alarmRate = 8.0; // Oscillations per second

        private const float FREQUENCY_SMOOTHING = 0.08f;
        private const float VOLUME_SMOOTHING = 0.05f;

        public WaveFormat WaveFormat => waveFormat;

        public double Frequency
        {
            get => targetFrequency;
            set => targetFrequency = Math.Max(20, Math.Min(20000, value));
        }

        public float Volume
        {
            get => targetVolume;
            set => targetVolume = Math.Max(0, Math.Min(1, value));
        }

        public double AlarmRate
        {
            get => alarmRate;
            set => alarmRate = Math.Max(1, Math.Min(30, value));
        }

        public AlarmSoundGenerator(double frequency = 600, float volume = 0f, int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            targetFrequency = frequency;
            currentFrequency = frequency;
            targetVolume = volume;
            currentVolume = volume;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                currentFrequency += (targetFrequency - currentFrequency) * FREQUENCY_SMOOTHING;
                currentVolume += (targetVolume - currentVolume) * VOLUME_SMOOTHING;

                // Alarm modulation: oscillate frequency by +/- 30%
                alarmPhase += alarmRate / sampleRate;
                if (alarmPhase >= 1.0) alarmPhase -= 1.0;
                double modulation = Math.Sin(2.0 * Math.PI * alarmPhase);
                double freq = currentFrequency * (1.0 + modulation * 0.3);

                // Generate harsh saw-like wave (more alarming than sine)
                double saw = 2.0 * phase - 1.0;
                double sine = Math.Sin(2.0 * Math.PI * phase);
                double waveform = saw * 0.4 + sine * 0.6;

                buffer[offset + i] = (float)(currentVolume * waveform);

                phase += freq / sampleRate;
                if (phase >= 1.0) phase -= 1.0;
            }
            return count;
        }
    }

    /// <summary>
    /// Tracked hazard info.
    /// </summary>
    public struct HazardInfo
    {
        public Vector3 Position;
        public float ExpiryTime;
        public HazardType Type;
    }

    public enum HazardType
    {
        GroundSpike,
        FallingRock,
        Exploder
    }

    /// <summary>
    /// Warning audio system for environmental hazards and exploders.
    /// Plays a directional alarm siren when hazards are near the player.
    /// - Ground Spikes (Dreadnought boss attack): registered via patch
    /// - Falling Rocks (Salt Pits biome): registered via patch
    /// - Exploders (horde enemies): detected by polling enemy names
    /// </summary>
    public class HazardWarningAudio : MonoBehaviour
    {
        public static HazardWarningAudio Instance { get; private set; }

        // Audio
        private WaveOutEvent outputDevice;
        private AlarmSoundGenerator alarmGenerator;
        private PanningSampleProvider panProvider;
        private VolumeSampleProvider volumeProvider;

        // Hazard tracking
        private List<HazardInfo> activeHazards = new List<HazardInfo>();
        private readonly object hazardLock = new object();

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // Exploder detection
        private float nextExploderCheckTime = 0f;
        private const float EXPLODER_CHECK_INTERVAL = 0.2f;
        private const float EXPLODER_WARNING_DISTANCE = 8f;

        // Configuration
        private const float MAX_WARNING_DISTANCE = 25f;
        private const float CRITICAL_DISTANCE = 5f;

        // State
        private bool isInitialized = false;
        private string lastSceneName = "";
        private IGameStateProvider gameStateProvider;

        static HazardWarningAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<HazardWarningAudio>();
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
            Plugin.Log.LogInfo("[HazardAudio] Initialized");
        }

        void Start()
        {
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            try
            {
                alarmGenerator = new AlarmSoundGenerator(700, 0f);
                panProvider = new PanningSampleProvider(alarmGenerator) { Pan = 0f };
                volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 1.0f };

                outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[HazardAudio] Audio channel created");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[HazardAudio] Failed to initialize: {e.Message}");
            }
        }

        /// <summary>
        /// Register a ground spike hazard at a position.
        /// Called from HazardPatches.
        /// </summary>
        public static void RegisterGroundSpike(Vector3 position, float lifetime)
        {
            var instance = Instance;
            if (instance == null) return;

            lock (instance.hazardLock)
            {
                instance.activeHazards.Add(new HazardInfo
                {
                    Position = position,
                    ExpiryTime = Time.time + lifetime,
                    Type = HazardType.GroundSpike
                });
            }
        }

        /// <summary>
        /// Register a falling rock hazard at a position.
        /// Called from HazardPatches.
        /// </summary>
        public static void RegisterFallingRock(Vector3 position, float lifetime = 3f)
        {
            var instance = Instance;
            if (instance == null) return;

            lock (instance.hazardLock)
            {
                instance.activeHazards.Add(new HazardInfo
                {
                    Position = position,
                    ExpiryTime = Time.time + lifetime,
                    Type = HazardType.FallingRock
                });
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
                    alarmGenerator.Volume = 0f;
                    return;
                }

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                {
                    alarmGenerator.Volume = 0f;
                    return;
                }

                // Check for exploders periodically
                if (Time.time >= nextExploderCheckTime)
                {
                    CheckExploders();
                    nextExploderCheckTime = Time.time + EXPLODER_CHECK_INTERVAL;
                }

                // Remove expired hazards
                CleanExpiredHazards();

                // Find closest hazard and play warning
                UpdateWarningAudio();
            }
            catch (Exception e)
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogError($"[HazardAudio] Update error: {e.Message}");
            }
        }

        private void CheckExploders()
        {
            try
            {
                var tracker = EnemyTracker.Instance;
                if (tracker == null) return;

                Vector3 playerPos = playerTransform.position;

                foreach (var enemy in tracker.GetActiveEnemies())
                {
                    try
                    {
                        if (enemy == null || !enemy.isAlive) continue;

                        // Check if this enemy is an Exploder by name
                        string name = enemy.name;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.Contains("Exploder", StringComparison.OrdinalIgnoreCase)) continue;

                        Vector3 enemyPos = enemy.position;
                        float distance = Vector3.Distance(playerPos, enemyPos);

                        if (distance > EXPLODER_WARNING_DISTANCE) continue;

                        // Register as a short-lived hazard (will be re-registered next check)
                        lock (hazardLock)
                        {
                            // Don't add duplicate exploders at nearly the same position
                            bool alreadyTracked = false;
                            for (int i = 0; i < activeHazards.Count; i++)
                            {
                                if (activeHazards[i].Type == HazardType.Exploder &&
                                    Vector3.Distance(activeHazards[i].Position, enemyPos) < 1f)
                                {
                                    // Update position and extend expiry
                                    activeHazards[i] = new HazardInfo
                                    {
                                        Position = enemyPos,
                                        ExpiryTime = Time.time + EXPLODER_CHECK_INTERVAL + 0.1f,
                                        Type = HazardType.Exploder
                                    };
                                    alreadyTracked = true;
                                    break;
                                }
                            }

                            if (!alreadyTracked)
                            {
                                activeHazards.Add(new HazardInfo
                                {
                                    Position = enemyPos,
                                    ExpiryTime = Time.time + EXPLODER_CHECK_INTERVAL + 0.1f,
                                    Type = HazardType.Exploder
                                });
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogDebug($"[HazardAudio] CheckExploders error: {e.Message}");
            }
        }

        private void CleanExpiredHazards()
        {
            lock (hazardLock)
            {
                float now = Time.time;
                activeHazards.RemoveAll(h => h.ExpiryTime <= now);
            }
        }

        private void UpdateWarningAudio()
        {
            lock (hazardLock)
            {
                if (activeHazards.Count == 0)
                {
                    alarmGenerator.Volume = 0f;
                    return;
                }

                Vector3 playerPos = playerTransform.position;
                Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();
                Vector3 right = new Vector3(forward.z, 0, -forward.x);

                // Find closest hazard
                float closestDistance = float.MaxValue;
                Vector3 closestPos = Vector3.zero;
                HazardType closestType = HazardType.GroundSpike;

                for (int i = 0; i < activeHazards.Count; i++)
                {
                    float dist = Vector3.Distance(playerPos, activeHazards[i].Position);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestPos = activeHazards[i].Position;
                        closestType = activeHazards[i].Type;
                    }
                }

                if (closestDistance > MAX_WARNING_DISTANCE)
                {
                    alarmGenerator.Volume = 0f;
                    return;
                }

                // Calculate stereo pan
                Vector3 toHazard = (closestPos - playerPos);
                toHazard.y = 0;
                toHazard.Normalize();
                float pan = Vector3.Dot(toHazard, right);
                pan = Mathf.Clamp(pan, -1f, 1f);

                // Proximity factor (0 = far, 1 = close)
                float proximityFactor = 1f - Mathf.Clamp01(closestDistance / MAX_WARNING_DISTANCE);
                proximityFactor = proximityFactor * proximityFactor;

                bool isCritical = closestDistance < CRITICAL_DISTANCE;

                // Audio parameters based on hazard type
                double frequency;
                float volume;
                double alarmRate;

                switch (closestType)
                {
                    case HazardType.Exploder:
                        // Exploders: fast ticking alarm, high pitch (lower volume)
                        frequency = 900 + proximityFactor * 500; // 900-1400 Hz
                        volume = 0.15f + proximityFactor * 0.15f; // 0.15-0.30
                        alarmRate = 6 + proximityFactor * 14; // 6-20 Hz (faster as closer)
                        if (isCritical)
                        {
                            volume *= 1.3f;
                            alarmRate = 25; // Very fast ticking
                        }
                        break;

                    case HazardType.FallingRock:
                        // Falling rocks: deep alarm
                        frequency = 400 + proximityFactor * 300; // 400-700 Hz
                        volume = 0.2f + proximityFactor * 0.2f; // 0.2-0.4
                        alarmRate = 4 + proximityFactor * 8; // 4-12 Hz
                        if (isCritical) volume *= 1.4f;
                        break;

                    default: // GroundSpike
                        // Ground spikes: medium alarm siren
                        frequency = 600 + proximityFactor * 400; // 600-1000 Hz
                        volume = 0.2f + proximityFactor * 0.25f; // 0.2-0.45
                        alarmRate = 5 + proximityFactor * 10; // 5-15 Hz
                        if (isCritical)
                        {
                            volume *= 1.4f;
                            alarmRate = 18;
                        }
                        break;
                }

                volume = Mathf.Clamp(volume, 0f, 0.7f);

                float pitchMultiplier = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toHazard);
                panProvider.Pan = pan;
                alarmGenerator.Frequency = frequency * pitchMultiplier;
                alarmGenerator.Volume = volume;
                alarmGenerator.AlarmRate = alarmRate;
            }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[HazardAudio] Scene changed to {currentScene} - resetting");
                    lock (hazardLock)
                    {
                        activeHazards.Clear();
                    }
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[HazardAudio] CheckSceneChange error: {e.Message}");
            }
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

        void OnDestroy()
        {
            try
            {
                outputDevice?.Stop();
                outputDevice?.Dispose();
            }
            catch { }
            Instance = null;
            Plugin.Log.LogInfo("[HazardAudio] Destroyed");
        }
    }
}
