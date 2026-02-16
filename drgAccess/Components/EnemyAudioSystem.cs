using System;
using System.Collections.Generic;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Enemy type for audio differentiation.
    /// </summary>
    public enum EnemyAudioType
    {
        Normal,
        Elite,
        Boss,
        Loot
    }

    /// <summary>
    /// Generates alert sounds for enemies based on type.
    /// - Normal: short high beep
    /// - Elite: medium beep with vibrato
    /// - Boss: long low powerful tone
    /// </summary>
    public class EnemyAlertSoundGenerator : ISampleProvider
    {
        private static readonly System.Random rng = new System.Random();

        private readonly WaveFormat waveFormat;
        private double phase;
        private int samplesRemaining;
        private int delaySamples; // Pre-beep silence for staggering across directions
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
            phase = rng.NextDouble(); // Random initial phase so overlapping beeps sound distinct
            volume = 0.15f;
        }

        /// <summary>
        /// Trigger a beep. delaySamples offsets the start within the audio buffer
        /// so beeps from different directions don't merge into one sound.
        /// </summary>
        public void Play(double freq, float vol, EnemyAudioType type, int delaySamples = 0)
        {
            baseFrequency = Math.Max(50, Math.Min(2000, freq));
            volume = Math.Max(0, Math.Min(0.9f, vol));
            enemyType = type;
            vibratoPhase = 0;

            switch (type)
            {
                case EnemyAudioType.Boss:
                    totalSamples = (int)(sampleRate * 0.35); // 350ms
                    break;
                case EnemyAudioType.Elite:
                    totalSamples = (int)(sampleRate * 0.18); // 180ms
                    break;
                case EnemyAudioType.Loot:
                    totalSamples = (int)(sampleRate * 0.12); // 120ms bright chime
                    break;
                default:
                    totalSamples = (int)(sampleRate * 0.05); // 50ms
                    break;
            }
            // Set samplesRemaining BEFORE delaySamples for thread safety
            // (audio thread checks delaySamples first, then samplesRemaining)
            samplesRemaining = totalSamples;
            this.delaySamples = delaySamples;
        }

        public bool IsPlaying => samplesRemaining > 0;

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (delaySamples > 0)
                {
                    // Silence before the beep starts (stagger offset)
                    buffer[offset + i] = 0;
                    delaySamples--;
                }
                else if (samplesRemaining > 0)
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
                    // Boss: VERY deep rumbling with dramatic pitch descent
                    frequency = baseFrequency * (1.0 - progress * 0.5); // Descend 50% (more dramatic)
                    envelope = (float)(Math.Exp(-progress * 2.0) * (1.0 - progress * 0.2));

                    // Heavy square wave with strong sub-bass (menacing)
                    double square = phase < 0.5 ? 1.0 : -1.0;
                    double subBass = Math.Sin(2.0 * Math.PI * phase * 0.5);
                    double deepSine = Math.Sin(2.0 * Math.PI * phase * 0.25); // Extra low component
                    waveform = square * 0.5 + subBass * 0.3 + deepSine * 0.2;
                    break;

                case EnemyAudioType.Elite:
                    // Elite: Strong distinctive vibrato (more pronounced)
                    vibratoPhase += 15.0 / sampleRate; // 15 Hz vibrato (faster, more noticeable)
                    double vibrato = Math.Sin(2.0 * Math.PI * vibratoPhase) * 0.25; // Increased depth
                    frequency = baseFrequency * (1.0 + vibrato);
                    envelope = (float)Math.Exp(-progress * 3.0);

                    // Triangle dominant with sharp harmonics
                    double triangle = 2.0 * Math.Abs(2.0 * phase - 1.0) - 1.0;
                    double harmonic = Math.Sin(2.0 * Math.PI * phase * 2.0); // 2nd harmonic
                    waveform = triangle * 0.6 + harmonic * 0.4;
                    break;

                case EnemyAudioType.Loot:
                    // Loot: bright ascending chime (pleasant, clearly not a threat)
                    frequency = baseFrequency * (1.0 + progress * 0.3); // Ascend 30% (opposite of boss)
                    envelope = (float)(Math.Exp(-progress * 4.0) * (progress < 0.05 ? progress / 0.05 : 1.0));

                    // Pure sine + octave harmonic for sparkle/chime quality
                    double chime = Math.Sin(2.0 * Math.PI * phase);
                    double octave = Math.Sin(2.0 * Math.PI * phase * 2.0) * 0.3;
                    double fifthHarm = Math.Sin(2.0 * Math.PI * phase * 3.0) * 0.1;
                    waveform = chime * 0.6 + octave + fifthHarm;
                    break;

                default: // Normal
                    envelope = (float)Math.Exp(-progress * 8); // Very sharp attack/decay

                    // Clean pure beep (distinct from others)
                    double cleanSine = Math.Sin(2.0 * Math.PI * phase);
                    waveform = cleanSine; // Pure sine wave - clear and simple
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
    public class EnemyAudioChannel
    {
        public EnemyAlertSoundGenerator SoundGenerator;
        public PanningSampleProvider PanProvider;
        public VolumeSampleProvider VolumeProvider;
        public bool IsDisposed;
    }

    /// <summary>
    /// Enemy information in a direction, separated by type.
    /// </summary>
    public class DirectionalEnemyGroup
    {
        public int NormalCount;
        public int EliteCount;
        public int BossCount;
        public int LootCount;

        public float ClosestNormalDistance = float.MaxValue;
        public float ClosestEliteDistance = float.MaxValue;
        public float ClosestBossDistance = float.MaxValue;
        public float ClosestLootDistance = float.MaxValue;

        // Pan of closest enemy of each type
        public float ClosestNormalPan;
        public float ClosestElitePan;
        public float ClosestBossPan;
        public float ClosestLootPan;

        // Height of closest enemy (for pitch adjustment)
        public float ClosestNormalHeight;
        public float ClosestEliteHeight;
        public float ClosestBossHeight;
        public float ClosestLootHeight;

        public float NextNormalPlayTime;
        public float NextElitePlayTime;
        public float NextBossPlayTime;
        public float NextLootPlayTime;

        public void Reset()
        {
            NormalCount = 0;
            EliteCount = 0;
            BossCount = 0;
            LootCount = 0;
            ClosestNormalDistance = float.MaxValue;
            ClosestEliteDistance = float.MaxValue;
            ClosestBossDistance = float.MaxValue;
            ClosestLootDistance = float.MaxValue;
        }

        public int TotalCount => NormalCount + EliteCount + BossCount + LootCount;
        public bool HasBoss => BossCount > 0;
        public bool HasElite => EliteCount > 0;
        public bool HasNormal => NormalCount > 0;
        public bool HasLoot => LootCount > 0;
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
        private float MaxDistance => ModConfig.GetSetting(ModConfig.ENEMY_RANGE);
        private float criticalProximityDistance = 3.5f; // Very close - imminent danger!
        // Normal: Fast frequent beeps
        private float normalBaseInterval = 0.4f;
        private float normalMinInterval = 0.08f;
        private float normalCriticalInterval = 0.04f; // VERY fast when critical
        // Elite: Medium distinctive beeps
        private float eliteBaseInterval = 0.6f;
        private float eliteMinInterval = 0.15f;
        private float eliteCriticalInterval = 0.08f;
        // Boss: Slow powerful pulses
        private float bossBaseInterval = 1.0f;
        private float bossMinInterval = 0.3f;
        private float bossCriticalInterval = 0.15f;
        // Loot: Distinctive chime beeps
        private float lootBaseInterval = 0.7f;
        private float lootMinInterval = 0.2f;
        private float lootCriticalInterval = 0.1f;

        // 8 directions for better precision (supports diagonal movement)
        private const int NUM_DIRECTIONS = 8;
        private DirectionalEnemyGroup[] directionGroups = new DirectionalEnemyGroup[NUM_DIRECTIONS];

        // Shared audio output (single device for all enemy sounds)
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;

        // Audio channels: 3 per direction (normal, elite, boss), all share one output
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

        // Enemy name announcements
        private HashSet<EKillScoreFamily> announcedEnemyTypes = new HashSet<EKillScoreFamily>();
        private HashSet<EKillScoreFamily> currentNearbyTypes = new HashSet<EKillScoreFamily>();
        private float nextNameAnnouncementTime = 0f;
        private const float NAME_ANNOUNCEMENT_INTERVAL = 3f;
        private LocalizedEnemyResources localizedEnemyResources;
        private bool searchedEnemyResources = false;

        // Proximity announcements
        private const float NEARBY_THRESHOLD = 20f;
        private const float VERY_CLOSE_THRESHOLD = 8f;
        private const float GENERAL_ANNOUNCE_COOLDOWN = 8f;
        private const float SPECIAL_ANNOUNCE_COOLDOWN = 5f;
        private const float VERY_CLOSE_ANNOUNCE_COOLDOWN = 3f;
        private float nextGeneralAnnounceTime = 0f;
        private float nextSpecialAnnounceTime = 0f;
        private float nextVeryCloseAnnounceTime = 0f;
        private bool wasEnemyNearby = false;
        // Per-update proximity tracking (populated in UpdateEnemyGroups)
        private int nearbyEnemyCount = 0;
        private float closestExploderDistance = float.MaxValue;
        private float closestEliteDistance = float.MaxValue;
        private float closestBossDistance = float.MaxValue;

        static EnemyAudioSystem()
        {
            ClassInjector.RegisterTypeInIl2Cpp<EnemyAudioSystem>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Plugin.Log.LogWarning("[EnemyAudio] Duplicate instance - destroying");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            for (int i = 0; i < NUM_DIRECTIONS; i++)
            {
                directionGroups[i] = new DirectionalEnemyGroup();
            }

            Plugin.Log.LogInfo($"[EnemyAudio] Awake - Initialized (GameObject: {gameObject.name})");
        }

        void OnEnable()
        {
            Plugin.Log.LogInfo("[EnemyAudio] OnEnable called");
        }

        void OnDisable()
        {
            Plugin.Log.LogWarning("[EnemyAudio] OnDisable called - audio will stop!");
        }

        void Start()
        {
            InitializeAudioChannels();
        }

        private void InitializeAudioChannels()
        {
            try
            {
                // Single shared mixer for all enemy sounds
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                mixer = new MixingSampleProvider(format) { ReadFully = true };

                // Create channels for each direction and type
                for (int dir = 0; dir < NUM_DIRECTIONS; dir++)
                {
                    CreateAudioChannel($"normal_{dir}");
                    CreateAudioChannel($"elite_{dir}");
                    CreateAudioChannel($"boss_{dir}");
                    CreateAudioChannel($"loot_{dir}");
                }

                // Single output device for all enemy audio
                outputDevice = new WaveOutEvent();
                outputDevice.Init(mixer);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo($"[EnemyAudio] Created {audioChannels.Count} channels (1 shared device)");
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
                var volumeProvider = new VolumeSampleProvider(panProvider) { Volume = 0.15f };
                mixer.AddMixerInput(volumeProvider);

                audioChannels[key] = new EnemyAudioChannel
                {
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

                if (!isInitialized)
                {
                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogDebug("[EnemyAudio] Not initialized");
                    return;
                }

                // Only play during active gameplay (CORE state)
                if (!IsInActiveGameplay())
                {
                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogDebug("[EnemyAudio] Not in active gameplay");
                    return;
                }

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null || cameraTransform == null)
                {
                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogDebug($"[EnemyAudio] Player/camera missing: player={playerTransform != null}, camera={cameraTransform != null}");
                    return;
                }

                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogDebug($"[EnemyAudio] Ready to update (player+camera OK, in gameplay)");

                if (Time.time >= nextUpdateTime)
                {
                    UpdateEnemyGroups();
                    AnnounceEnemyNames();
                    AnnounceEnemyProximity();
                    nextUpdateTime = Time.time + updateInterval;
                }

                PlayDirectionalSounds();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EnemyAudio] Update error: {e.Message}");
            }
        }

        private void UpdateEnemyGroups()
        {
            try
            {
                if (Time.frameCount % 60 == 0)
                    Plugin.Log.LogInfo("[EnemyAudio] UpdateEnemyGroups() called");

                // Reset groups
                for (int i = 0; i < NUM_DIRECTIONS; i++)
                {
                    directionGroups[i].Reset();
                }
                currentNearbyTypes.Clear();
                nearbyEnemyCount = 0;
                closestExploderDistance = float.MaxValue;
                closestEliteDistance = float.MaxValue;
                closestBossDistance = float.MaxValue;

                if (Time.frameCount % 60 == 0)
                    Plugin.Log.LogInfo("[EnemyAudio] Groups reset, getting player position");

                Vector3 playerPos = playerTransform.position;
                Vector3 forward = cameraTransform.forward;
                forward.y = 0;
                forward.Normalize();

                // Correct cross product order for right vector
                Vector3 right = new Vector3(forward.z, 0, -forward.x); // Perpendicular to forward on XZ plane

                if (Time.frameCount % 60 == 0)
                    Plugin.Log.LogInfo($"[EnemyAudio] Player at {playerPos}, checking EnemyTracker");

                // Get enemies from EnemyTracker (which is maintained by patches)
                var tracker = EnemyTracker.Instance;
                if (tracker == null)
                {
                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogWarning("[EnemyAudio] EnemyTracker.Instance is null");
                    return;
                }

                var enemies = tracker.GetActiveEnemies();
                if (enemies == null)
                {
                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogWarning("[EnemyAudio] GetActiveEnemies() returned null");
                    return;
                }

                int processedCount = 0;
                int validEnemyCount = 0;

                    foreach (var enemy in enemies)
                {
                    try
                    {
                        processedCount++;
                        if (enemy == null) continue;

                        // Check if enemy is alive using proper property
                        if (!enemy.isAlive) continue;

                    // Filter out non-combat entities
                    var enemyType = enemy.type;
                    if (enemyType == EEnemyType.COCOON || enemyType == EEnemyType.BIG_COCOON)
                        continue;

                    // Skip common lootbugs (too many, causes audio spam)
                    // Golden Lootbugs and Huuli Hoarders are rare and valuable - keep them
                    if (enemy.id == EEnemy.LOOTBUG)
                        continue;

                    validEnemyCount++;

                    // Track enemy type for name announcements
                    try { currentNearbyTypes.Add(enemy.killScoreFamily); } catch { }

                    // Use Enemy's position property
                    Vector3 enemyPos = enemy.position;
                    float distance = Vector3.Distance(playerPos, enemyPos);

                    if (distance > MaxDistance) continue;

                    // Track proximity for announcements
                    nearbyEnemyCount++;

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
                    // Loot enemies: LOOTBUG, LOOTBUG_GOLDEN, HUUL_HOARDER - special chime
                    // BOSS: Big threatening bosses
                    // ELITE: True elites with distinctive vibrato
                    // MINI_ELITE: Treat as normal (common enemies)
                    var enemyId = enemy.id;
                    bool isLoot = enemyId == EEnemy.LOOTBUG ||
                                  enemyId == EEnemy.LOOTBUG_GOLDEN ||
                                  enemyId == EEnemy.HUUL_HOARDER;
                    bool isBoss = !isLoot && enemyType == EEnemyType.BOSS;
                    bool isElite = !isLoot && !isBoss && enemyType == EEnemyType.ELITE;
                    bool isExploder = enemyId == EEnemy.EXPLODER || enemyId == EEnemy.EXPLODER_FAST;

                    // Track closest special enemies for proximity announcements
                    if (isExploder && distance < closestExploderDistance)
                        closestExploderDistance = distance;
                    if (isBoss && distance < closestBossDistance)
                        closestBossDistance = distance;
                    if (isElite && distance < closestEliteDistance)
                        closestEliteDistance = distance;

                    var group = directionGroups[dirIndex];

                    if (isLoot)
                    {
                        group.LootCount++;
                        if (distance < group.ClosestLootDistance)
                        {
                            group.ClosestLootDistance = distance;
                            group.ClosestLootPan = pan;
                            group.ClosestLootHeight = height;
                        }
                    }
                    else if (isBoss)
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
                    catch (Exception ex)
                    {
                        if (Time.frameCount % 300 == 0)
                            Plugin.Log.LogDebug($"[EnemyAudio] Error processing enemy: {ex.Message}");
                    }
                }

                if (Time.frameCount % 60 == 0)
                {
                    Plugin.Log.LogInfo($"[EnemyAudio] UpdateEnemyGroups: processed={processedCount}, valid={validEnemyCount}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[EnemyAudio] UpdateEnemyGroups error: {ex.Message}\nStack: {ex.StackTrace}");
            }
        }

        private void AnnounceEnemyNames()
        {
            try
            {
                if (currentNearbyTypes.Count == 0) return;
                if (Time.time < nextNameAnnouncementTime) return;

                // Find types not yet announced
                var newTypes = new List<EKillScoreFamily>();
                foreach (var type in currentNearbyTypes)
                {
                    if (!announcedEnemyTypes.Contains(type))
                        newTypes.Add(type);
                }

                if (newTypes.Count == 0) return;

                // Look up localized names
                if (!searchedEnemyResources)
                {
                    searchedEnemyResources = true;
                    try
                    {
                        var resources = UnityEngine.Resources.FindObjectsOfTypeAll<LocalizedEnemyResources>();
                        if (resources != null && resources.Length > 0)
                            localizedEnemyResources = resources[0];
                    }
                    catch { }
                }

                var sb = new StringBuilder();
                foreach (var type in newTypes)
                {
                    announcedEnemyTypes.Add(type);

                    string name = null;
                    try
                    {
                        if (localizedEnemyResources != null)
                            name = localizedEnemyResources.GetKillScoreFamilyName(type);
                    }
                    catch { }

                    if (string.IsNullOrEmpty(name))
                        name = type.ToString();

                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(name);
                }

                if (sb.Length > 0)
                {
                    ScreenReader.Say(sb.ToString());
                    nextNameAnnouncementTime = Time.time + NAME_ANNOUNCEMENT_INTERVAL;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"[EnemyAudio] AnnounceEnemyNames error: {ex.Message}");
            }
        }

        private void AnnounceEnemyProximity()
        {
            try
            {
                float currentTime = Time.time;

                // Priority 1: Explosive very close (interrupt)
                if (closestExploderDistance < VERY_CLOSE_THRESHOLD && currentTime >= nextVeryCloseAnnounceTime)
                {
                    ScreenReader.Interrupt("Explosive very close!");
                    nextVeryCloseAnnounceTime = currentTime + VERY_CLOSE_ANNOUNCE_COOLDOWN;
                    nextSpecialAnnounceTime = currentTime + SPECIAL_ANNOUNCE_COOLDOWN;
                    return;
                }

                // Priority 2: Boss very close (interrupt)
                if (closestBossDistance < VERY_CLOSE_THRESHOLD && currentTime >= nextVeryCloseAnnounceTime)
                {
                    ScreenReader.Interrupt("Boss very close!");
                    nextVeryCloseAnnounceTime = currentTime + VERY_CLOSE_ANNOUNCE_COOLDOWN;
                    nextSpecialAnnounceTime = currentTime + SPECIAL_ANNOUNCE_COOLDOWN;
                    return;
                }

                // Priority 3: Elite very close (interrupt)
                if (closestEliteDistance < VERY_CLOSE_THRESHOLD && currentTime >= nextVeryCloseAnnounceTime)
                {
                    ScreenReader.Interrupt("Elite very close!");
                    nextVeryCloseAnnounceTime = currentTime + VERY_CLOSE_ANNOUNCE_COOLDOWN;
                    nextSpecialAnnounceTime = currentTime + SPECIAL_ANNOUNCE_COOLDOWN;
                    return;
                }

                // Priority 4: Special enemies nearby
                if (currentTime >= nextSpecialAnnounceTime)
                {
                    if (closestExploderDistance < NEARBY_THRESHOLD)
                    {
                        ScreenReader.Say("Explosive nearby");
                        nextSpecialAnnounceTime = currentTime + SPECIAL_ANNOUNCE_COOLDOWN;
                        return;
                    }
                    if (closestBossDistance < NEARBY_THRESHOLD)
                    {
                        ScreenReader.Say("Boss nearby");
                        nextSpecialAnnounceTime = currentTime + SPECIAL_ANNOUNCE_COOLDOWN;
                        return;
                    }
                    if (closestEliteDistance < NEARBY_THRESHOLD)
                    {
                        ScreenReader.Say("Elite nearby");
                        nextSpecialAnnounceTime = currentTime + SPECIAL_ANNOUNCE_COOLDOWN;
                        return;
                    }
                }

                // Priority 5: General enemy count (only on transition from 0 → some, or significant changes)
                if (currentTime >= nextGeneralAnnounceTime)
                {
                    if (nearbyEnemyCount > 0 && !wasEnemyNearby)
                    {
                        if (nearbyEnemyCount == 1)
                            ScreenReader.Say("Enemy nearby");
                        else
                            ScreenReader.Say($"{nearbyEnemyCount} enemies nearby");
                        nextGeneralAnnounceTime = currentTime + GENERAL_ANNOUNCE_COOLDOWN;
                    }
                }

                wasEnemyNearby = nearbyEnemyCount > 0;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"[EnemyAudio] AnnounceEnemyProximity error: {ex.Message}");
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

                // Play loot sound (highest priority - rare and valuable)
                if (group.HasLoot)
                {
                    PlayEnemySound(dir, EnemyAudioType.Loot, group, currentTime);
                }

                // Play boss sound
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
                case EnemyAudioType.Loot:
                    distance = group.ClosestLootDistance;
                    pan = group.ClosestLootPan;
                    height = group.ClosestLootHeight;
                    count = group.LootCount;
                    baseInterval = lootBaseInterval;
                    minInterval = lootMinInterval;
                    nextPlayTime = ref group.NextLootPlayTime;
                    break;
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

            // Check if in critical proximity (IMMINENT DANGER!)
            bool isCritical = distance < criticalProximityDistance;

            // Calculate interval based on proximity
            float proximityFactor = 1f - (distance / MaxDistance);
            proximityFactor = Mathf.Clamp01(proximityFactor);
            proximityFactor = proximityFactor * proximityFactor;

            float countFactor = Mathf.Clamp01(count / 5f);

            // Critical proximity: use MUCH faster interval for urgent warning
            float targetInterval = isCritical ?
                (type == EnemyAudioType.Boss ? bossCriticalInterval :
                 type == EnemyAudioType.Elite ? eliteCriticalInterval :
                 type == EnemyAudioType.Loot ? lootCriticalInterval :
                 normalCriticalInterval) :
                minInterval;

            float interval = Mathf.Lerp(baseInterval, targetInterval, Mathf.Max(proximityFactor, countFactor));

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
                    case EnemyAudioType.Loot:
                        // Loot: Very high bright chime (1500-2200 Hz) - clearly not a threat
                        frequency = 1500 + (proximityFactor * 700);
                        frequency *= (1.0 + heightAdjustment);
                        volume = 0.25f + proximityFactor * 0.15f;
                        if (isCritical) volume *= 1.3f;
                        break;
                    case EnemyAudioType.Boss:
                        // Boss: VERY deep (40-100 Hz) - unmistakable rumble
                        frequency = 40 + (proximityFactor * 60);
                        frequency *= (1.0 + heightAdjustment);
                        volume = 0.3f + proximityFactor * 0.2f;
                        // CRITICAL PROXIMITY: boost volume significantly
                        if (isCritical) volume *= 1.5f;
                        break;
                    case EnemyAudioType.Elite:
                        // Elite: Medium-low (200-400 Hz) - clear separation from normal
                        frequency = 200 + (proximityFactor * 200);
                        frequency *= (1.0 + heightAdjustment);
                        volume = 0.25f + proximityFactor * 0.15f;
                        // CRITICAL PROXIMITY: boost volume
                        if (isCritical) volume *= 1.4f;
                        break;
                    default:
                        // Normal: High (700-1400 Hz) - bright and distinct
                        frequency = 700 + (proximityFactor * 700);
                        frequency *= (1.0 + heightAdjustment);
                        volume = 0.2f + proximityFactor * 0.12f;
                        // Boost by count
                        volume += Mathf.Clamp01(count / 8f) * 0.08f;
                        // CRITICAL PROXIMITY: boost volume and add urgency
                        if (isCritical)
                        {
                            volume *= 1.3f;
                            frequency *= 1.2; // Slightly higher pitch for urgency
                        }
                        break;
                }

                // Forward/behind pitch modulation (on top of height adjustment)
                frequency *= AudioDirectionHelper.GetDirectionalPitchMultiplier(dirIndex);

                volume = Mathf.Clamp(volume, 0.15f, 0.7f);  // Allow louder for critical warnings

                channel.PanProvider.Pan = pan;
                channel.VolumeProvider.Volume = volume * ModConfig.GetVolume(ModConfig.ENEMY_DETECTION);

                // Stagger beeps across directions at the audio buffer level.
                // Without this, all directions fire in the same mixer Read() call
                // and merge into one indistinguishable beep.
                // ~10ms per direction = 70ms total spread across 8 directions.
                int staggerSamples = dirIndex * 441; // 441 samples ≈ 10ms at 44100Hz
                channel.SoundGenerator.Play(frequency, volume, type, staggerSamples);

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
                    Plugin.Log.LogInfo($"[EnemyAudio] Scene changed to {currentScene} - resetting");
                    sceneLoadTime = Time.time;

                    // Reset all direction groups
                    for (int i = 0; i < NUM_DIRECTIONS; i++)
                    {
                        directionGroups[i].Reset();
                        directionGroups[i].NextNormalPlayTime = 0;
                        directionGroups[i].NextElitePlayTime = 0;
                        directionGroups[i].NextBossPlayTime = 0;
                        directionGroups[i].NextLootPlayTime = 0;
                    }

                    // Reset player/camera references (will be found again)
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;

                    // Reset enemy name announcements
                    announcedEnemyTypes.Clear();
                    currentNearbyTypes.Clear();
                    nextNameAnnouncementTime = 0f;
                    searchedEnemyResources = false;
                    localizedEnemyResources = null;

                    // Reset proximity announcements
                    wasEnemyNearby = false;
                    nextGeneralAnnounceTime = 0f;
                    nextSpecialAnnounceTime = 0f;
                    nextVeryCloseAnnounceTime = 0f;

                    // Reset game state provider
                    gameStateProvider = null;
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EnemyAudio] CheckSceneChange error: {e.Message}");
            }
        }

        private bool IsInActiveGameplay()
        {
            try
            {
                // Primary check: time scale (catches pause)
                if (Time.timeScale <= 0.1f)
                {
                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogDebug($"[EnemyAudio] timeScale too low: {Time.timeScale}");
                    return false;
                }

                // Validate gameStateProvider using Unity's null check (IL2CPP-safe)
                if (gameStateProvider != null)
                {
                    // Try to cast back to GameController to test if destroyed
                    var gc = gameStateProvider.TryCast<GameController>();
                    if (gc == null) // Unity's overloaded null check detects destroyed objects
                    {
                        if (Time.frameCount % 300 == 0)
                            Plugin.Log.LogInfo("[EnemyAudio] GameController was destroyed, searching for new one");
                        gameStateProvider = null;
                    }
                }

                // Find game state provider if not cached (search every frame until found!)
                if (gameStateProvider == null)
                {
                    var gameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (gameController != null)
                    {
                        gameStateProvider = gameController.Cast<IGameStateProvider>();
                        Plugin.Log.LogInfo("[EnemyAudio] Found new GameController");
                    }
                    else
                    {
                        // Not found yet, keep searching next frame
                        if (Time.frameCount % 300 == 0)
                            Plugin.Log.LogDebug("[EnemyAudio] GameController not found, will retry next frame");
                        return false;
                    }
                }

                if (gameStateProvider != null)
                {
                    var state = gameStateProvider.State;
                    bool isCore = state == GameController.EGameState.CORE;

                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogDebug($"[EnemyAudio] GameState: {state}, CORE={isCore}");

                    // Only play audio during CORE gameplay state
                    return isCore;
                }
                else
                {
                    if (Time.frameCount % 300 == 0)
                        Plugin.Log.LogDebug("[EnemyAudio] gameStateProvider is null after search");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[EnemyAudio] IsInActiveGameplay error: {e.Message}");
            }

            // Fallback: already checked time scale above
            return false;
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
            audioChannels.Clear();

            try
            {
                outputDevice?.Stop();
                outputDevice?.Dispose();
            }
            catch { }

            Instance = null;
            Plugin.Log.LogInfo("[EnemyAudio] Destroyed");
        }
    }
}
