using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Enemy type for audio differentiation.
    /// </summary>
    public enum EnemyAudioType
    {
        Normal,
        Elite,
        Boss
    }

    /// <summary>
    /// Generates alert sounds for enemies based on type.
    /// - Normal: short high beep
    /// - Elite: medium beep with vibrato
    /// - Boss: long low powerful tone
    /// </summary>
    public class EnemyAlertSoundGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private double phase;
        private int samplesRemaining;
        private double baseFrequency;
        private float volume;
        private int totalSamples;
        private readonly int sampleRate;
        private EnemyAudioType enemyType;
        private double vibratoPhase;

        public WaveFormat WaveFormat => waveFormat;

        public EnemyAlertSoundGenerator(int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            samplesRemaining = 0;
            volume = 0.15f;  // Reduced from 0.3 to 0.15
        }

        public void Play(double freq, float vol, EnemyAudioType type)
        {
            baseFrequency = Math.Max(50, Math.Min(2000, freq));
            volume = Math.Max(0, Math.Min(0.9f, vol));
            enemyType = type;
            vibratoPhase = 0;
            phase = 0;

            // Duration based on type
            switch (type)
            {
                case EnemyAudioType.Boss:
                    totalSamples = (int)(sampleRate * 0.25); // 250ms - long and threatening
                    break;
                case EnemyAudioType.Elite:
                    totalSamples = (int)(sampleRate * 0.12); // 120ms - medium
                    break;
                default:
                    totalSamples = (int)(sampleRate * 0.06); // 60ms - short
                    break;
            }
            samplesRemaining = totalSamples;
        }

        public bool IsPlaying => samplesRemaining > 0;

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (samplesRemaining > 0)
                {
                    float progress = 1f - (float)samplesRemaining / totalSamples;
                    float sample = GenerateSample(progress);
                    buffer[offset + i] = sample;
                    samplesRemaining--;
                }
                else
                {
                    buffer[offset + i] = 0;
                }
            }
            return count;
        }

        private float GenerateSample(float progress)
        {
            double frequency = baseFrequency;
            float envelope;
            double waveform;

            switch (enemyType)
            {
                case EnemyAudioType.Boss:
                    // Boss: deep wave with pitch descent, very powerful
                    frequency = baseFrequency * (1.0 - progress * 0.3); // Descend 30%
                    envelope = (float)(Math.Exp(-progress * 2.5) * (1.0 - progress * 0.3));

                    // Square wave with sub-bass
                    double square = phase < 0.5 ? 1.0 : -1.0;
                    double subBass = Math.Sin(2.0 * Math.PI * phase * 0.5);
                    waveform = square * 0.6 + subBass * 0.4;
                    break;

                case EnemyAudioType.Elite:
                    // Elite: distinctive vibrato
                    vibratoPhase += 12.0 / sampleRate; // 12 Hz vibrato
                    double vibrato = Math.Sin(2.0 * Math.PI * vibratoPhase) * 0.15;
                    frequency = baseFrequency * (1.0 + vibrato);
                    envelope = (float)Math.Exp(-progress * 3.5);

                    // Triangle with some square
                    double triangle = 2.0 * Math.Abs(2.0 * phase - 1.0) - 1.0;
                    double squareWave = phase < 0.5 ? 1.0 : -1.0;
                    waveform = triangle * 0.7 + squareWave * 0.3;
                    break;

                default: // Normal
                    envelope = (float)Math.Exp(-progress * 5);

                    // Simple short beep
                    double tri = 2.0 * Math.Abs(2.0 * phase - 1.0) - 1.0;
                    double sq = phase < 0.5 ? 1.0 : -1.0;
                    waveform = tri * 0.5 + sq * 0.5;
                    break;
            }

            // Advance phase
            phase += frequency / sampleRate;
            if (phase >= 1.0) phase -= 1.0;

            return (float)(volume * envelope * waveform);
        }
    }

    /// <summary>
    /// Audio channel for enemies.
    /// </summary>
    public class EnemyAudioChannel : IDisposable
    {
        public WaveOutEvent OutputDevice;
        public EnemyAlertSoundGenerator SoundGenerator;
        public PanningSampleProvider PanProvider;
        public VolumeSampleProvider VolumeProvider;
        public bool IsDisposed;

        public void Dispose()
        {
            IsDisposed = true;
            try
            {
                OutputDevice?.Stop();
                OutputDevice?.Dispose();
            }
            catch { }
        }
    }

    /// <summary>
    /// Enemy information in a direction, separated by type.
    /// </summary>
    public class DirectionalEnemyGroup
    {
        public int NormalCount;
        public int EliteCount;
        public int BossCount;

        public float ClosestNormalDistance = float.MaxValue;
        public float ClosestEliteDistance = float.MaxValue;
        public float ClosestBossDistance = float.MaxValue;

        // Pan of closest enemy of each type
        public float ClosestNormalPan;
        public float ClosestElitePan;
        public float ClosestBossPan;

        // Height of closest enemy (for pitch adjustment)
        public float ClosestNormalHeight;
        public float ClosestEliteHeight;
        public float ClosestBossHeight;

        public float NextNormalPlayTime;
        public float NextElitePlayTime;
        public float NextBossPlayTime;

        public void Reset()
        {
            NormalCount = 0;
            EliteCount = 0;
            BossCount = 0;
            ClosestNormalDistance = float.MaxValue;
            ClosestEliteDistance = float.MaxValue;
            ClosestBossDistance = float.MaxValue;
        }

        public int TotalCount => NormalCount + EliteCount + BossCount;
        public bool HasBoss => BossCount > 0;
        public bool HasElite => EliteCount > 0;
        public bool HasNormal => NormalCount > 0;
    }

    /// <summary>
    /// 3D audio system for enemies with type differentiation.
    /// - Bosses: deep, long and powerful sound
    /// - Elites: medium with vibrato
    /// - Normals: high short beep
    /// Pitch varies with distance (higher=closer) and height (above=higher, below=lower)
    /// </summary>
    public class EnemyAudioSystem : MonoBehaviour
    {
        public static EnemyAudioSystem Instance { get; private set; }

        // Configuration
        private float maxDistance = 50f;
        private float normalBaseInterval = 0.5f;
        private float normalMinInterval = 0.1f;
        private float eliteBaseInterval = 0.8f;
        private float eliteMinInterval = 0.2f;
        private float bossBaseInterval = 1.2f;
        private float bossMinInterval = 0.4f;

        // 8 directions for better precision
        private const int NUM_DIRECTIONS = 8;
        private DirectionalEnemyGroup[] directionGroups = new DirectionalEnemyGroup[NUM_DIRECTIONS];

        // Audio channels: 3 per direction (normal, elite, boss)
        private Dictionary<string, EnemyAudioChannel> audioChannels = new Dictionary<string, EnemyAudioChannel>();

        // References
        private Transform playerTransform = null;
        private Transform cameraTransform = null;
        private float nextPlayerSearchTime = 0f;
        private float nextUpdateTime = 0f;
        private float updateInterval = 0.1f;

        // Scene tracking
        private string lastSceneName = "";
        private float sceneLoadTime = 0f;

        private bool isInitialized = false;

        // Game state tracking
        private IGameStateProvider gameStateProvider;
        private AI_Manager aiManager;

        static EnemyAudioSystem()
        {
            ClassInjector.RegisterTypeInIl2Cpp<EnemyAudioSystem>();
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

            for (int i = 0; i < NUM_DIRECTIONS; i++)
            {
                directionGroups[i] = new DirectionalEnemyGroup();
            }

            Plugin.Log.LogInfo("[EnemyAudio] Initialized with 8 directions and enemy type differentiation");
        }

        void Start()
        {
            InitializeAudioChannels();
        }

        private void InitializeAudioChannels()
        {
            try
            {
                // Create channels for each direction and type
                for (int dir = 0; dir < NUM_DIRECTIONS; dir++)
                {
                    CreateAudioChannel($"normal_{dir}");
                    CreateAudioChannel($"elite_{dir}");
                    CreateAudioChannel($"boss_{dir}");
                }
                isInitialized = true;
                Plugin.Log.LogInfo($"[EnemyAudio] Created {audioChannels.Count} audio channels");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EnemyAudio] Failed to initialize: {e.Message}");
            }
        }

        private void CreateAudioChannel(string key)
        {
            try
            {
                var generator = new EnemyAlertSoundGenerator();
                var panProvider = new PanningSampleProvider(generator);
                var volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 0.15f };  // Reduced from 0.3 to 0.15

                var outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                audioChannels[key] = new EnemyAudioChannel
                {
                    OutputDevice = outputDevice,
                    SoundGenerator = generator,
                    PanProvider = panProvider,
                    VolumeProvider = volumeProvider,
                    IsDisposed = false
                };
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EnemyAudio] CreateAudioChannel error for {key}: {e.Message}");
            }
        }

        void Update()
        {
            try
            {
                CheckSceneChange();

                if (!isInitialized) return;

                // Only play during active gameplay (CORE state)
                if (!IsInActiveGameplay()) return;

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null || cameraTransform == null) return;

                if (Time.time >= nextUpdateTime)
                {
                    UpdateEnemyGroups();
                    nextUpdateTime = Time.time + updateInterval;
                }

                PlayDirectionalSounds();
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EnemyAudio] Update error: {e.Message}");
            }
        }

        private void UpdateEnemyGroups()
        {
            // Reset groups
            for (int i = 0; i < NUM_DIRECTIONS; i++)
            {
                directionGroups[i].Reset();
            }

            Vector3 playerPos = playerTransform.position;
            Vector3 forward = cameraTransform.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            // Get AI_Manager instance if not cached
            if (aiManager == null)
            {
                aiManager = AI_Manager._instance;
                if (aiManager == null) return;
            }

            // Access AI units directly from AI_Manager (static field)
            var aiUnits = AI_Manager._units;
            if (aiUnits == null) return;

            foreach (var aiUnit in aiUnits)
            {
                try
                {
                    if (aiUnit == null) continue;

                    // Get Enemy component from AI_Unit
                    var enemy = aiUnit.GetComponent<Enemy>();
                    if (enemy == null) continue;

                    // Check if enemy is dead
                    try
                    {
                        if (enemy.IsDead) continue;
                    }
                    catch
                    {
                        continue;
                    }

                    // Filter out non-combat entities
                    var enemyType = enemy.type;
                    if (enemyType == EEnemyType.COCOON || enemyType == EEnemyType.BIG_COCOON)
                        continue;

                    // Use AI_Unit's current position for better accuracy
                    Vector3 enemyPos = aiUnit._currentPosition;
                    float distance = Vector3.Distance(playerPos, enemyPos);

                    if (distance > maxDistance) continue;

                    // Calculate direction (8 directions)
                    Vector3 toEnemy = (enemyPos - playerPos);
                    float height = toEnemy.y; // Store height for pitch adjustment
                    toEnemy.y = 0;
                    toEnemy.Normalize();

                    float angle = Vector3.SignedAngle(forward, toEnemy, Vector3.up);
                    int dirIndex = GetDirectionIndex(angle);

                    // Calculate precise pan
                    float pan = Vector3.Dot(toEnemy, right);
                    pan = Mathf.Clamp(pan, -1f, 1f);

                    // Determine enemy type
                    bool isBoss = enemyType == EEnemyType.BOSS;
                    bool isElite = !isBoss && (enemyType == EEnemyType.ELITE || enemyType == EEnemyType.MINI_ELITE);

                    var group = directionGroups[dirIndex];

                    if (isBoss)
                    {
                        group.BossCount++;
                        if (distance < group.ClosestBossDistance)
                        {
                            group.ClosestBossDistance = distance;
                            group.ClosestBossPan = pan;
                            group.ClosestBossHeight = height;
                        }
                    }
                    else if (isElite)
                    {
                        group.EliteCount++;
                        if (distance < group.ClosestEliteDistance)
                        {
                            group.ClosestEliteDistance = distance;
                            group.ClosestElitePan = pan;
                            group.ClosestEliteHeight = height;
                        }
                    }
                    else
                    {
                        group.NormalCount++;
                        if (distance < group.ClosestNormalDistance)
                        {
                            group.ClosestNormalDistance = distance;
                            group.ClosestNormalPan = pan;
                            group.ClosestNormalHeight = height;
                        }
                    }
                }
                catch { }
            }
        }

        private int GetDirectionIndex(float angle)
        {
            // 8 directions of 45 degrees each
            angle += 22.5f; // Offset to center
            if (angle < 0) angle += 360f;
            if (angle >= 360f) angle -= 360f;

            return (int)(angle / 45f) % 8;
        }

        private void PlayDirectionalSounds()
        {
            float currentTime = Time.time;

            for (int dir = 0; dir < NUM_DIRECTIONS; dir++)
            {
                var group = directionGroups[dir];

                // Play boss sound (highest priority)
                if (group.HasBoss)
                {
                    PlayEnemySound(dir, EnemyAudioType.Boss, group, currentTime);
                }

                // Play elite sound
                if (group.HasElite)
                {
                    PlayEnemySound(dir, EnemyAudioType.Elite, group, currentTime);
                }

                // Play normal sound
                if (group.HasNormal)
                {
                    PlayEnemySound(dir, EnemyAudioType.Normal, group, currentTime);
                }
            }
        }

        private void PlayEnemySound(int dirIndex, EnemyAudioType type, DirectionalEnemyGroup group, float currentTime)
        {
            float distance, pan, height;
            int count;
            float baseInterval, minInterval;
            ref float nextPlayTime = ref group.NextNormalPlayTime;

            switch (type)
            {
                case EnemyAudioType.Boss:
                    distance = group.ClosestBossDistance;
                    pan = group.ClosestBossPan;
                    height = group.ClosestBossHeight;
                    count = group.BossCount;
                    baseInterval = bossBaseInterval;
                    minInterval = bossMinInterval;
                    nextPlayTime = ref group.NextBossPlayTime;
                    break;
                case EnemyAudioType.Elite:
                    distance = group.ClosestEliteDistance;
                    pan = group.ClosestElitePan;
                    height = group.ClosestEliteHeight;
                    count = group.EliteCount;
                    baseInterval = eliteBaseInterval;
                    minInterval = eliteMinInterval;
                    nextPlayTime = ref group.NextElitePlayTime;
                    break;
                default:
                    distance = group.ClosestNormalDistance;
                    pan = group.ClosestNormalPan;
                    height = group.ClosestNormalHeight;
                    count = group.NormalCount;
                    baseInterval = normalBaseInterval;
                    minInterval = normalMinInterval;
                    break;
            }

            // Calculate interval
            float proximityFactor = 1f - (distance / maxDistance);
            proximityFactor = Mathf.Clamp01(proximityFactor);
            proximityFactor = proximityFactor * proximityFactor;

            float countFactor = Mathf.Clamp01(count / 5f);
            float interval = Mathf.Lerp(baseInterval, minInterval, Mathf.Max(proximityFactor, countFactor));

            if (currentTime < nextPlayTime) return;

            string channelKey = $"{type.ToString().ToLower()}_{dirIndex}";
            if (!audioChannels.TryGetValue(channelKey, out var channel)) return;
            if (channel.IsDisposed) return;

            try
            {
                // Frequency based on type, distance, and height
                double frequency;
                float volume;

                // Height adjustment: above = higher pitch (+20%), below = lower pitch (-20%)
                float heightAdjustment = Mathf.Clamp(height / 5f, -0.2f, 0.2f);

                switch (type)
                {
                    case EnemyAudioType.Boss:
                        // Boss: very deep (60-120 Hz), reduced volume
                        frequency = 60 + (proximityFactor * 60);
                        frequency *= (1.0 + heightAdjustment);
                        volume = 0.25f + proximityFactor * 0.15f;  // Reduced from 0.6/0.3
                        break;
                    case EnemyAudioType.Elite:
                        // Elite: deep-medium (150-300 Hz), reduced volume
                        frequency = 150 + (proximityFactor * 150);
                        frequency *= (1.0 + heightAdjustment);
                        volume = 0.2f + proximityFactor * 0.12f;  // Reduced from 0.5/0.25
                        break;
                    default:
                        // Normal: high (500-1000 Hz), reduced volume
                        frequency = 500 + (proximityFactor * 500);
                        frequency *= (1.0 + heightAdjustment);
                        volume = 0.15f + proximityFactor * 0.1f;  // Reduced from 0.4/0.2
                        // Boost by count (reduced)
                        volume += Mathf.Clamp01(count / 8f) * 0.08f;  // Reduced from 0.15
                        break;
                }

                volume = Mathf.Clamp(volume, 0.1f, 0.4f);  // Reduced max from 0.85 to 0.4

                channel.PanProvider.Pan = pan;
                channel.VolumeProvider.Volume = volume;
                channel.SoundGenerator.Play(frequency, volume, type);

                nextPlayTime = currentTime + interval;
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EnemyAudio] PlayEnemySound error: {e.Message}");
            }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    sceneLoadTime = Time.time;
                    for (int i = 0; i < NUM_DIRECTIONS; i++)
                    {
                        directionGroups[i].Reset();
                        directionGroups[i].NextNormalPlayTime = 0;
                        directionGroups[i].NextElitePlayTime = 0;
                        directionGroups[i].NextBossPlayTime = 0;
                    }
                }
                lastSceneName = currentScene;
            }
            catch { }
        }

        private bool IsInActiveGameplay()
        {
            try
            {
                // Find game state provider if not cached
                if (gameStateProvider == null)
                {
                    var gameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (gameController != null)
                    {
                        gameStateProvider = gameController.Cast<IGameStateProvider>();
                    }
                }

                if (gameStateProvider != null)
                {
                    var state = gameStateProvider.State;
                    // Only play audio during CORE gameplay state
                    return state == GameController.EGameState.CORE;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[EnemyAudio] IsInActiveGameplay error: {e.Message}");
            }

            // Fallback: check time scale
            return Time.timeScale > 0.1f;
        }

        private void FindPlayer()
        {
            try
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

                var cam = Camera.main;
                if (cam != null)
                {
                    cameraTransform = cam.transform;
                    if (playerTransform == null)
                        playerTransform = cam.transform;
                }
            }
            catch { }
        }

        void OnDestroy()
        {
            foreach (var channel in audioChannels.Values)
            {
                channel?.Dispose();
            }
            audioChannels.Clear();
            Instance = null;
            Plugin.Log.LogInfo("[EnemyAudio] Destroyed");
        }
    }
}
