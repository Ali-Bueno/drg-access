using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Types of boss attack telegraphs.
    /// </summary>
    public enum BossAttackType
    {
        Charge,
        Spikes,
        Fireball,
        Heal
    }

    /// <summary>
    /// Sound generator for boss attack telegraphs.
    /// Plays a pattern of 3 short beeps with attack-specific timing, frequency, and waveform.
    /// Speed between beeps is the main differentiator between attack types.
    /// </summary>
    public class ChargingSoundGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private readonly int sampleRate;
        private double phase;

        // Per-beep parameters
        private double frequency;
        private int beepDurationSamples;
        private int gapDurationSamples;
        private int totalPatternSamples;
        private WaveformType waveformType;

        private enum WaveformType { Sine, SquareSine, Deep, Gentle }

        // Playback state
        private bool isPlaying;
        private int samplesPlayed;

        // Volume control
        private float targetVolume;
        private float currentVolume;
        private const float VOLUME_SMOOTHING = 0.05f;

        private const int BEEP_COUNT = 3;

        public WaveFormat WaveFormat => waveFormat;

        public float Volume
        {
            get => targetVolume;
            set => targetVolume = Math.Max(0, Math.Min(1, value));
        }

        public float Pan { get; set; }
        public bool IsPlaying => isPlaying;

        public ChargingSoundGenerator(int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public void Play(BossAttackType type)
        {
            switch (type)
            {
                case BossAttackType.Charge:
                    // Fast 3 beeps, aggressive square+sine, high frequency
                    frequency = 600;
                    beepDurationSamples = (int)(0.08 * sampleRate);
                    gapDurationSamples = (int)(0.10 * sampleRate);
                    waveformType = WaveformType.SquareSine;
                    break;
                case BossAttackType.Spikes:
                    // Medium 3 beeps, deep rumble, low frequency
                    frequency = 350;
                    beepDurationSamples = (int)(0.10 * sampleRate);
                    gapDurationSamples = (int)(0.18 * sampleRate);
                    waveformType = WaveformType.Deep;
                    break;
                case BossAttackType.Fireball:
                    // Very fast 3 beeps, sharp sine, high frequency
                    frequency = 800;
                    beepDurationSamples = (int)(0.06 * sampleRate);
                    gapDurationSamples = (int)(0.06 * sampleRate);
                    waveformType = WaveformType.Sine;
                    break;
                case BossAttackType.Heal:
                    // Slow 3 beeps, gentle sine, low frequency
                    frequency = 250;
                    beepDurationSamples = (int)(0.12 * sampleRate);
                    gapDurationSamples = (int)(0.28 * sampleRate);
                    waveformType = WaveformType.Gentle;
                    break;
            }

            // Total = 3 beeps + 2 gaps (no trailing gap)
            totalPatternSamples = BEEP_COUNT * beepDurationSamples + (BEEP_COUNT - 1) * gapDurationSamples;
            samplesPlayed = 0;
            phase = 0;
            isPlaying = true;
        }

        public void Stop()
        {
            isPlaying = false;
            targetVolume = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                currentVolume += (targetVolume - currentVolume) * VOLUME_SMOOTHING;

                if (!isPlaying || samplesPlayed >= totalPatternSamples)
                {
                    buffer[offset + i] = 0;
                    if (isPlaying && samplesPlayed >= totalPatternSamples)
                        isPlaying = false;
                    continue;
                }

                // Determine which beep/gap we're in
                int cycleLength = beepDurationSamples + gapDurationSamples;
                int posInCycle = samplesPlayed % cycleLength;
                bool inBeep = posInCycle < beepDurationSamples;

                // Last beep has no trailing gap, check if we're past the last gap start
                int lastBeepStart = (BEEP_COUNT - 1) * cycleLength;
                if (samplesPlayed >= lastBeepStart)
                    inBeep = (samplesPlayed - lastBeepStart) < beepDurationSamples;

                if (!inBeep)
                {
                    buffer[offset + i] = 0;
                    samplesPlayed++;
                    continue;
                }

                // Beep envelope: quick attack and release for clean beep
                double beepProgress = (double)posInCycle / beepDurationSamples;
                if (samplesPlayed >= lastBeepStart)
                    beepProgress = (double)(samplesPlayed - lastBeepStart) / beepDurationSamples;

                double envelope = 1.0;
                if (beepProgress < 0.05)
                    envelope = beepProgress / 0.05; // 5% attack
                else if (beepProgress > 0.85)
                    envelope = (1.0 - beepProgress) / 0.15; // 15% release

                // Generate waveform based on type
                double waveform;
                switch (waveformType)
                {
                    case WaveformType.SquareSine:
                        double sine = Math.Sin(2.0 * Math.PI * phase);
                        double square = sine > 0 ? 1.0 : -1.0;
                        waveform = square * 0.35 + sine * 0.65;
                        break;
                    case WaveformType.Deep:
                        // Low rumble: fundamental + sub-octave
                        waveform = Math.Sin(2.0 * Math.PI * phase) * 0.6
                                 + Math.Sin(2.0 * Math.PI * phase * 0.5) * 0.4;
                        break;
                    case WaveformType.Gentle:
                        // Soft sine with slight harmonic
                        waveform = Math.Sin(2.0 * Math.PI * phase) * 0.8
                                 + Math.Sin(2.0 * Math.PI * phase * 2.0) * 0.2;
                        break;
                    default: // Sine
                        waveform = Math.Sin(2.0 * Math.PI * phase);
                        break;
                }

                buffer[offset + i] = (float)(currentVolume * waveform * envelope);

                phase += frequency / sampleRate;
                if (phase >= 1.0) phase -= 1.0;
                samplesPlayed++;
            }
            return count;
        }
    }

    /// <summary>
    /// Boss attack charging audio component.
    /// Plays directional rising-pitch alarm when boss telegraphs an attack.
    /// </summary>
    public class BossAttackAudio : MonoBehaviour
    {
        public static BossAttackAudio Instance { get; private set; }

        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;
        private ChargingSoundGenerator generator;
        private PanningSampleProvider panProvider;

        private bool isInitialized;
        private string lastSceneName = "";

        // Player/camera references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime;

        // Boss position for panning
        private Vector3 lastBossPosition;
        private bool hasBossPosition;

        private IGameStateProvider gameStateProvider;

        static BossAttackAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<BossAttackAudio>();
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
            Plugin.Log.LogInfo("[BossAttackAudio] Initialized");
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

                generator = new ChargingSoundGenerator();
                panProvider = new PanningSampleProvider(generator) { Pan = 0f };
                mixer.AddMixerInput(panProvider);

                outputDevice = new WaveOutEvent();
                outputDevice.Init(mixer);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[BossAttackAudio] Audio initialized");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BossAttackAudio] Failed to initialize: {e.Message}");
            }
        }

        void Update()
        {
            if (!isInitialized) return;

            CheckSceneChange();

            if (!IsInActiveGameplay())
            {
                if (generator.IsPlaying)
                    generator.Stop();
                return;
            }

            FindPlayerIfNeeded();
            UpdatePanning();
        }

        private void CheckSceneChange()
        {
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene != lastSceneName)
            {
                lastSceneName = currentScene;
                playerTransform = null;
                cameraTransform = null;
                gameStateProvider = null;
                hasBossPosition = false;
                if (generator != null && generator.IsPlaying)
                    generator.Stop();
            }
        }

        private bool IsInActiveGameplay()
        {
            if (Time.timeScale < 0.1f) return false;

            if (gameStateProvider != null)
            {
                try { var _ = gameStateProvider.State; }
                catch { gameStateProvider = null; }
            }

            if (gameStateProvider == null)
            {
                var gc = UnityEngine.Object.FindObjectOfType<GameController>();
                if (gc != null)
                    gameStateProvider = gc.TryCast<IGameStateProvider>();
            }

            if (gameStateProvider == null) return false;

            try
            {
                return gameStateProvider.State == GameController.EGameState.CORE;
            }
            catch { return false; }
        }

        private void FindPlayerIfNeeded()
        {
            if (Time.time < nextPlayerSearchTime) return;
            nextPlayerSearchTime = Time.time + 2f;

            if (playerTransform == null)
            {
                var player = UnityEngine.Object.FindObjectOfType<Player>();
                if (player != null)
                    playerTransform = player.transform;
            }

            if (cameraTransform == null)
            {
                var cam = Camera.main;
                if (cam != null)
                    cameraTransform = cam.transform;
            }
        }

        private void UpdatePanning()
        {
            if (!generator.IsPlaying || !hasBossPosition || playerTransform == null) return;

            float volume = ModConfig.GetVolume(ModConfig.BOSS_ATTACKS) * 0.5f;
            generator.Volume = volume;

            if (cameraTransform != null)
            {
                Vector3 toTarget = (lastBossPosition - playerTransform.position).normalized;

                // Stereo panning
                Vector3 camRight = cameraTransform.right;
                camRight.y = 0;
                camRight.Normalize();
                float pan = Vector3.Dot(camRight, toTarget);
                panProvider.Pan = Mathf.Clamp(pan, -1f, 1f);

                // Directional pitch modulation handled via frequency in generator
            }
        }

        /// <summary>
        /// Play a charging sound for a boss attack telegraph.
        /// Called from BossAttackPatches.
        /// </summary>
        public static void PlayAttackSound(BossAttackType type, Vector3 bossPosition)
        {
            var inst = Instance;
            if (inst == null || !inst.isInitialized) return;

            inst.lastBossPosition = bossPosition;
            inst.hasBossPosition = true;

            float volume = ModConfig.GetVolume(ModConfig.BOSS_ATTACKS) * 0.5f;
            inst.generator.Volume = volume;
            inst.generator.Play(type);
        }

        /// <summary>
        /// Stop all boss attack audio.
        /// </summary>
        public static void StopAll()
        {
            var inst = Instance;
            if (inst == null) return;
            inst.generator?.Stop();
            inst.hasBossPosition = false;
        }

        void OnDestroy()
        {
            try
            {
                outputDevice?.Stop();
                outputDevice?.Dispose();
            }
            catch { }

            if (Instance == this)
                Instance = null;
        }
    }
}
