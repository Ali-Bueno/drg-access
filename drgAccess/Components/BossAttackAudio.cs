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
    /// Sound generator for boss attack charging effects.
    /// Produces a rising-pitch alarm that accelerates to signal an incoming attack.
    /// </summary>
    public class ChargingSoundGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private readonly int sampleRate;
        private double phase;
        private double alarmPhase;

        // Attack parameters (set per attack type)
        private double baseFreq;
        private double peakFreq;
        private double baseAlarmRate;
        private double peakAlarmRate;
        private float duration;
        private bool isHeal;

        // Playback state
        private bool isPlaying;
        private int samplesPlayed;
        private int totalSamples;

        // Volume control
        private float targetVolume;
        private float currentVolume;
        private const float VOLUME_SMOOTHING = 0.05f;

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
                    baseFreq = 400; peakFreq = 1000;
                    baseAlarmRate = 4; peakAlarmRate = 18;
                    duration = 1.2f; isHeal = false;
                    break;
                case BossAttackType.Spikes:
                    baseFreq = 300; peakFreq = 800;
                    baseAlarmRate = 3; peakAlarmRate = 15;
                    duration = 1.4f; isHeal = false;
                    break;
                case BossAttackType.Fireball:
                    baseFreq = 500; peakFreq = 1200;
                    baseAlarmRate = 5; peakAlarmRate = 20;
                    duration = 1.0f; isHeal = false;
                    break;
                case BossAttackType.Heal:
                    baseFreq = 200; peakFreq = 400;
                    baseAlarmRate = 6; peakAlarmRate = 12;
                    duration = 2.0f; isHeal = true;
                    break;
            }

            samplesPlayed = 0;
            totalSamples = (int)(duration * sampleRate);
            phase = 0;
            alarmPhase = 0;
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

                if (!isPlaying || samplesPlayed >= totalSamples)
                {
                    buffer[offset + i] = 0;
                    if (isPlaying && samplesPlayed >= totalSamples)
                        isPlaying = false;
                    continue;
                }

                double progress = (double)samplesPlayed / totalSamples;

                // Frequency rises from base to peak over duration
                double freq = baseFreq + (peakFreq - baseFreq) * progress;

                // Alarm rate accelerates
                double rate = baseAlarmRate + (peakAlarmRate - baseAlarmRate) * progress;

                // Alarm modulation
                alarmPhase += rate / sampleRate;
                if (alarmPhase >= 1.0) alarmPhase -= 1.0;
                double modulation = Math.Sin(2.0 * Math.PI * alarmPhase);

                double modulatedFreq;
                double waveform;

                if (isHeal)
                {
                    // Heal: gentle warble with sine wave
                    modulatedFreq = freq * (1.0 + modulation * 0.2);
                    double sine = Math.Sin(2.0 * Math.PI * phase);
                    double sine2 = Math.Sin(2.0 * Math.PI * phase * 1.5);
                    waveform = sine * 0.7 + sine2 * 0.3;
                }
                else
                {
                    // Attack: aggressive siren with square+sine mix
                    modulatedFreq = freq * (1.0 + modulation * 0.3);
                    double sine = Math.Sin(2.0 * Math.PI * phase);
                    double square = sine > 0 ? 1.0 : -1.0;
                    waveform = square * 0.35 + sine * 0.65;
                }

                // Envelope: sustain with quick fade at end
                double envelope = 1.0;
                if (progress > 0.85)
                    envelope = (1.0 - progress) / 0.15;

                buffer[offset + i] = (float)(currentVolume * waveform * envelope);

                phase += modulatedFreq / sampleRate;
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
