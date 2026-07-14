using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using Assets.Scripts.LevelGeneration;
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
        Exploder,
        SpikyVine
    }

    /// <summary>
    /// Warning audio system for environmental hazards and exploders.
    /// Supports multiple simultaneous alarm channels (configurable via MaxHazardChannels).
    /// Each channel tracks a separate hazard with its own directional panning.
    /// </summary>
    public class HazardWarningAudio : MonoBehaviour
    {
        public static HazardWarningAudio Instance { get; private set; }

        // Audio - shared mixer with multiple alarm channels
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;
        private const int MAX_CHANNELS = 5; // Hard cap matching ModConfig max
        private HazardChannel[] channels;

        private struct HazardChannel
        {
            public AlarmSoundGenerator Generator;
            public PanningSampleProvider PanProvider;
        }

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
        private const float EXPLODER_WARNING_DISTANCE = 12f;

        // Spiky vines (Hollow Bough). Static field placed in the map, so a slower
        // cadence than enemies is plenty.
        private float nextVineCheckTime = 0f;
        private const float VINE_CHECK_INTERVAL = 0.5f;
        private float lastVineAnnounceTime = -10f;
        private const float VINE_ANNOUNCE_COOLDOWN = 4f;
        private MapGenerator cachedMapGenerator;
        private float nextMapGeneratorSearchTime = 0f;
        private int cachedVineRunGeneration = -1;

        // Configuration
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
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                mixer = new MixingSampleProvider(format) { ReadFully = true };

                channels = new HazardChannel[MAX_CHANNELS];
                for (int i = 0; i < MAX_CHANNELS; i++)
                {
                    var gen = new AlarmSoundGenerator(700, 0f);
                    var pan = new PanningSampleProvider(gen) { Pan = 0f };
                    mixer.AddMixerInput(pan);
                    channels[i] = new HazardChannel { Generator = gen, PanProvider = pan };
                }

                outputDevice = new WaveOutEvent();
                outputDevice.Init(mixer);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo($"[HazardAudio] Audio initialized with {MAX_CHANNELS} channels");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[HazardAudio] Failed to initialize: {e.Message}");
            }
        }

        /// <summary>
        /// Register a ground spike hazard at a position.
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
                    SilenceAll();
                    return;
                }

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                {
                    SilenceAll();
                    return;
                }

                // Check for exploders periodically
                if (Time.time >= nextExploderCheckTime)
                {
                    CheckExploders();
                    nextExploderCheckTime = Time.time + EXPLODER_CHECK_INTERVAL;
                }

                if (Time.time >= nextVineCheckTime)
                {
                    CheckSpikyVines();
                    nextVineCheckTime = Time.time + VINE_CHECK_INTERVAL;
                }

                CleanExpiredHazards();
                UpdateWarningAudio();
            }
            catch (Exception e)
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogError($"[HazardAudio] Update error: {e.Message}");
            }
        }

        private void SilenceAll()
        {
            if (channels == null) return;
            for (int i = 0; i < channels.Length; i++)
                channels[i].Generator.Volume = 0f;
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

                        string name = enemy.name;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.Contains("Exploder", StringComparison.OrdinalIgnoreCase)) continue;

                        Vector3 enemyPos = enemy.position;
                        float distance = Vector3.Distance(playerPos, enemyPos);

                        if (distance > EXPLODER_WARNING_DISTANCE) continue;

                        lock (hazardLock)
                        {
                            // Don't add duplicate exploders at nearly the same position
                            bool alreadyTracked = false;
                            for (int i = 0; i < activeHazards.Count; i++)
                            {
                                if (activeHazards[i].Type == HazardType.Exploder &&
                                    Vector3.Distance(activeHazards[i].Position, enemyPos) < 1f)
                                {
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

        /// <summary>
        /// Hollow Bough's spiky roots (RedVineRockBlock). The biome places dozens of
        /// them, so registering one hazard per vine would fill every alarm channel and
        /// drown out everything else — the exact "audio spam" players reported. A vine
        /// field is one danger AREA, so it gets ONE cue: the closest live vine stands in
        /// for the whole area, and the alarm turns critical once the player is inside
        /// the game's own damage radius (RedVineSystem.damageDist).
        /// </summary>
        private void CheckSpikyVines()
        {
            try
            {
                var vineSystem = GetRedVineSystem();
                if (vineSystem == null)
                {
                    RemoveHazardsOfType(HazardType.SpikyVine);
                    return;
                }

                var vines = vineSystem.RedVines;
                if (vines == null || vines.Count == 0)
                {
                    RemoveHazardsOfType(HazardType.SpikyVine);
                    return;
                }

                Vector3 playerPos = playerTransform.position;
                float maxWarningDistance = ModConfig.GetSetting(ModConfig.HAZARD_RANGE);

                float nearestDist = float.MaxValue;
                Vector3 nearestPos = Vector3.zero;

                for (int i = 0; i < vines.Count; i++)
                {
                    try
                    {
                        var vine = vines[i];
                        if (vine == null) continue;
                        if (!vine.IsRegenAlive) continue; // destroyed / regrowing: harmless

                        Vector3 pos = vine.transform.position;
                        float dist = Vector3.Distance(playerPos, pos);
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearestPos = pos;
                        }
                    }
                    catch { }
                }

                RemoveHazardsOfType(HazardType.SpikyVine);

                if (nearestDist > maxWarningDistance) return;

                lock (hazardLock)
                {
                    activeHazards.Add(new HazardInfo
                    {
                        Position = nearestPos,
                        ExpiryTime = Time.time + VINE_CHECK_INTERVAL + 0.1f,
                        Type = HazardType.SpikyVine
                    });
                }

                // Inside the vines' real damage radius: say so, the alarm alone cannot
                // tell the player they are already being hurt.
                float damageDist = RedVineSystem.damageDist;
                if (nearestDist <= damageDist && Time.time - lastVineAnnounceTime >= VINE_ANNOUNCE_COOLDOWN)
                {
                    lastVineAnnounceTime = Time.time;
                    ScreenReader.Interrupt(ModLocalization.Get("hazard_spiky_roots_on"));
                }
            }
            catch (Exception e)
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogDebug($"[HazardAudio] CheckSpikyVines error: {e.Message}");
            }
        }

        /// <summary>
        /// RedVineSystem is Zenject-injected (not a MonoBehaviour), so it can only be
        /// reached through the MapGenerator that owns it.
        /// </summary>
        private RedVineSystem GetRedVineSystem()
        {
            try
            {
                // A retry rebuilds the map without a scene change, so the generation
                // counter — not a null check — decides when this cache is stale.
                if (cachedVineRunGeneration != GameStateHelper.RunGeneration)
                {
                    cachedVineRunGeneration = GameStateHelper.RunGeneration;
                    cachedMapGenerator = null;
                    nextMapGeneratorSearchTime = 0f;
                }

                if (cachedMapGenerator == null)
                {
                    if (Time.time < nextMapGeneratorSearchTime) return null;
                    nextMapGeneratorSearchTime = Time.time + 3f;
                    cachedMapGenerator = UnityEngine.Object.FindObjectOfType<MapGenerator>();
                }

                return cachedMapGenerator != null ? cachedMapGenerator.RedVineSystem : null;
            }
            catch
            {
                cachedMapGenerator = null;
                return null;
            }
        }

        /// <summary>Copy of the currently tracked hazards, for the environment ping.</summary>
        public List<HazardInfo> GetHazardSnapshot()
        {
            lock (hazardLock)
            {
                return new List<HazardInfo>(activeHazards);
            }
        }

        private void RemoveHazardsOfType(HazardType type)
        {
            lock (hazardLock)
            {
                activeHazards.RemoveAll(h => h.Type == type);
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
                int maxChannels = ModConfig.GetSettingInt(ModConfig.MAX_HAZARD_CHANNELS);
                maxChannels = Math.Min(maxChannels, MAX_CHANNELS);
                float maxWarningDistance = ModConfig.GetSetting(ModConfig.HAZARD_RANGE);
                float configVolume = ModConfig.GetVolume(ModConfig.HAZARD_WARNING);

                if (activeHazards.Count == 0)
                {
                    SilenceAll();
                    return;
                }

                Vector3 playerPos = playerTransform.position;
                Vector3 forward = AudioDirectionHelper.GetReferenceForward(cameraTransform);
                Vector3 right = new Vector3(forward.z, 0, -forward.x);

                // Sort hazards by distance (closest first)
                activeHazards.Sort((a, b) =>
                {
                    float distA = Vector3.Distance(playerPos, a.Position);
                    float distB = Vector3.Distance(playerPos, b.Position);
                    return distA.CompareTo(distB);
                });

                // Assign up to maxChannels hazards to audio channels
                for (int ch = 0; ch < channels.Length; ch++)
                {
                    if (ch >= maxChannels || ch >= activeHazards.Count)
                    {
                        channels[ch].Generator.Volume = 0f;
                        continue;
                    }

                    var hazard = activeHazards[ch];
                    float distance = Vector3.Distance(playerPos, hazard.Position);

                    if (distance > maxWarningDistance)
                    {
                        channels[ch].Generator.Volume = 0f;
                        continue;
                    }

                    // Calculate stereo pan
                    Vector3 toHazard = (hazard.Position - playerPos);
                    toHazard.y = 0;
                    toHazard.Normalize();
                    float pan = Mathf.Clamp(Vector3.Dot(toHazard, right), -1f, 1f);

                    // Proximity factor
                    float proximityFactor = 1f - Mathf.Clamp01(distance / maxWarningDistance);
                    proximityFactor *= proximityFactor;

                    bool isCritical = distance < CRITICAL_DISTANCE;

                    // Audio parameters based on hazard type
                    double frequency;
                    float volume;
                    double alarmRate;

                    switch (hazard.Type)
                    {
                        case HazardType.Exploder:
                            frequency = 900 + proximityFactor * 500;
                            volume = 0.15f + proximityFactor * 0.15f;
                            alarmRate = 6 + proximityFactor * 14;
                            if (isCritical)
                            {
                                volume *= 1.3f;
                                alarmRate = 25;
                            }
                            break;

                        case HazardType.FallingRock:
                            frequency = 400 + proximityFactor * 300;
                            volume = 0.2f + proximityFactor * 0.2f;
                            alarmRate = 4 + proximityFactor * 8;
                            if (isCritical) volume *= 1.4f;
                            break;

                        case HazardType.SpikyVine:
                            // Lowest, slowest alarm of the set: a static field you can
                            // walk around, not something coming at you. Critical only
                            // once the player is inside the vines' real damage radius.
                            frequency = 250 + proximityFactor * 200;
                            volume = 0.16f + proximityFactor * 0.18f;
                            alarmRate = 3 + proximityFactor * 6;
                            if (distance <= RedVineSystem.damageDist)
                            {
                                volume *= 1.5f;
                                alarmRate = 14;
                            }
                            break;

                        default: // GroundSpike
                            frequency = 600 + proximityFactor * 400;
                            volume = 0.2f + proximityFactor * 0.25f;
                            alarmRate = 5 + proximityFactor * 10;
                            if (isCritical)
                            {
                                volume *= 1.4f;
                                alarmRate = 18;
                            }
                            break;
                    }

                    volume = Mathf.Clamp(volume, 0f, 0.7f);

                    // Reduce volume for secondary channels to keep the closest prominent
                    if (ch > 0)
                        volume *= 0.7f;

                    float pitchMultiplier = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toHazard);
                    channels[ch].PanProvider.Pan = pan;
                    channels[ch].Generator.Frequency = frequency * pitchMultiplier;
                    channels[ch].Generator.Volume = volume * configVolume;
                    channels[ch].Generator.AlarmRate = alarmRate;
                }
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

                return drgAccess.Helpers.GameStateHelper.IsInActiveGameplay();
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
