using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Plays one-shot audio from a preloaded float buffer.
    /// Used for footstep sounds that don't overlap.
    /// </summary>
    public class FootstepSampleProvider : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private float[] currentBuffer;
        private int position;
        private volatile float volume = 1.0f;

        public WaveFormat WaveFormat => waveFormat;
        public float Volume { set => volume = value; }

        public FootstepSampleProvider()
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        }

        public void Play(float[] samples)
        {
            currentBuffer = samples;
            position = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var buf = currentBuffer;
            var vol = volume;
            for (int i = 0; i < count; i++)
            {
                if (buf != null && position < buf.Length)
                {
                    buffer[offset + i] = buf[position] * vol;
                    position++;
                }
                else
                {
                    buffer[offset + i] = 0f;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Footstep audio system. Plays stone or metal footstep sounds based on
    /// player movement speed and proximity to the drop pod ramp.
    /// Stops when the player is stationary or colliding with walls.
    /// </summary>
    public class FootstepAudio : MonoBehaviour
    {
        public static FootstepAudio Instance { get; private set; }

        // Preloaded sound buffers (stereo 44100 Hz float arrays)
        private List<float[]> stoneSounds = new();
        private List<float[]> metalSounds = new();

        // Audio output
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;
        private FootstepSampleProvider footstepProvider;

        // Player tracking
        private Transform playerTransform;
        private Vector3 lastPosition;
        private bool hasLastPosition = false;
        private float nextPlayerSearchTime = 0f;

        // Footstep timing
        private float footstepTimer = 0f;
        private float smoothedSpeed = 0f;
        private int lastSoundIndex = -1;
        private System.Random rng = new();

        // Speed thresholds and interval mapping
        private const float MIN_SPEED = 0.5f;
        private const float STEP_INTERVAL = 0.34f;
        private const float SPEED_SMOOTHING = 0.04f;
        private const float BASE_VOLUME_BOOST = 1.8f;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";
        private bool isInitialized = false;

        static FootstepAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<FootstepAudio>();
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
            Plugin.Log.LogInfo("[FootstepAudio] Initialized");
        }

        void Start()
        {
            LoadSounds();
            InitializeAudio();
        }

        private void LoadSounds()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string stoneDir = Path.Combine(dllDir, "sounds", "footsteps", "stone");
            string metalDir = Path.Combine(dllDir, "sounds", "footsteps", "metal");

            stoneSounds = LoadSoundsFromDirectory(stoneDir);
            metalSounds = LoadSoundsFromDirectory(metalDir);
            Plugin.Log.LogInfo($"[FootstepAudio] Loaded {stoneSounds.Count} stone, {metalSounds.Count} metal sounds");
        }

        private List<float[]> LoadSoundsFromDirectory(string dir)
        {
            var result = new List<float[]>();
            if (!Directory.Exists(dir))
            {
                Plugin.Log.LogWarning($"[FootstepAudio] Directory not found: {dir}");
                return result;
            }

            foreach (var file in Directory.GetFiles(dir, "*.mp3"))
            {
                try
                {
                    using var reader = new AudioFileReader(file);
                    ISampleProvider provider = reader;

                    // Resample to 44100 Hz if needed
                    if (provider.WaveFormat.SampleRate != 44100)
                        provider = new WdlResamplingSampleProvider(provider, 44100);

                    // Convert mono to stereo if needed
                    if (provider.WaveFormat.Channels == 1)
                        provider = new MonoToStereoSampleProvider(provider);

                    // Read all samples into a float array
                    var samples = new List<float>();
                    var buf = new float[4096];
                    int read;
                    while ((read = provider.Read(buf, 0, buf.Length)) > 0)
                    {
                        for (int i = 0; i < read; i++)
                            samples.Add(buf[i]);
                    }

                    if (samples.Count > 0)
                        result.Add(samples.ToArray());
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[FootstepAudio] Failed to load {Path.GetFileName(file)}: {e.Message}");
                }
            }
            return result;
        }

        private void InitializeAudio()
        {
            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                mixer = new MixingSampleProvider(format) { ReadFully = true };

                footstepProvider = new FootstepSampleProvider();
                mixer.AddMixerInput(footstepProvider);

                outputDevice = new WaveOutEvent();
                outputDevice.Init(mixer);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[FootstepAudio] Audio initialized");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[FootstepAudio] Failed to initialize audio: {e.Message}");
            }
        }

        void Update()
        {
            if (!isInitialized) return;

            try
            {
                CheckSceneChange();

                if (!IsInActiveGameplay() || !ModConfig.GetBool(ModConfig.FOOTSTEPS_ENABLED))
                    return;

                // Find player periodically
                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null) return;

                Vector3 currentPos = playerTransform.position;

                // First frame with player: record position, don't play yet
                if (!hasLastPosition)
                {
                    lastPosition = currentPos;
                    hasLastPosition = true;
                    return;
                }

                float distance = Vector3.Distance(currentPos, lastPosition);
                float rawSpeed = distance / Mathf.Max(Time.deltaTime, 0.001f);
                smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed, SPEED_SMOOTHING);

                if (smoothedSpeed > MIN_SPEED)
                {
                    footstepTimer -= Time.deltaTime;
                    if (footstepTimer <= 0f)
                    {
                        PlayFootstep();
                        footstepTimer = STEP_INTERVAL;
                    }
                }
                else
                {
                    // Not moving — pause timer but don't reset
                    // (prevents rapid-fire steps from speed fluctuations)
                    if (footstepTimer <= 0f)
                        footstepTimer = 0.1f;
                }

                lastPosition = currentPos;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[FootstepAudio] Update error: {e.Message}");
            }
        }

        private void PlayFootstep()
        {
            bool useMetal = DropPodAudio.Instance != null && DropPodAudio.Instance.IsPlayerNearPod;

            var sounds = useMetal ? metalSounds : stoneSounds;
            if (sounds.Count == 0) return;

            int index = rng.Next(sounds.Count);
            if (sounds.Count > 1 && index == lastSoundIndex)
                index = (index + 1) % sounds.Count;
            lastSoundIndex = index;

            footstepProvider.Volume = ModConfig.GetVolume(ModConfig.FOOTSTEPS) * BASE_VOLUME_BOOST;
            footstepProvider.Play(sounds[index]);
        }

        /// <summary>
        /// Plays a preview footstep sound at the given volume.
        /// Called from ModSettingsMenu for audio preview.
        /// </summary>
        public void PreviewFootstep(float volume)
        {
            if (stoneSounds.Count == 0) return;
            footstepProvider.Volume = volume * BASE_VOLUME_BOOST;
            footstepProvider.Play(stoneSounds[rng.Next(stoneSounds.Count)]);
        }

        private void FindPlayer()
        {
            try
            {
                if (playerTransform != null) return;

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
            catch { }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    playerTransform = null;
                    hasLastPosition = false;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
                    footstepTimer = 0f;
                    smoothedSpeed = 0f;
                }
                lastSceneName = currentScene;
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
                {
                    var state = gameStateProvider.State;
                    return state == GameController.EGameState.CORE ||
                           state == GameController.EGameState.CORE_OUTRO;
                }
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
            Plugin.Log.LogInfo("[FootstepAudio] Destroyed");
        }
    }
}
