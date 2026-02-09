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
    /// Smooth waveform generator for wall detection tones.
    /// </summary>
    public class SineWaveGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private double phase;
        private double targetFrequency;
        private double currentFrequency;
        private float targetVolume;
        private float currentVolume;

        private const float FREQUENCY_SMOOTHING = 0.05f;
        private const float VOLUME_SMOOTHING = 0.02f;

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

        public SineWaveGenerator(double frequency = 440, float volume = 0.5f, int sampleRate = 44100)
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1); // Mono
            targetFrequency = frequency;
            currentFrequency = frequency;
            targetVolume = volume;
            currentVolume = volume;
            phase = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            double sampleRate = waveFormat.SampleRate;

            for (int i = 0; i < count; i++)
            {
                // Smooth frequency and volume transitions
                currentFrequency += (targetFrequency - currentFrequency) * FREQUENCY_SMOOTHING;
                currentVolume += (targetVolume - currentVolume) * VOLUME_SMOOTHING;

                // Generate smooth triangular wave
                double triangleWave = 2.0 * Math.Abs(2.0 * phase - 1.0) - 1.0;
                double sineWave = Math.Sin(2 * Math.PI * phase);
                double mixedWave = triangleWave * 0.7 + sineWave * 0.3;

                buffer[offset + i] = (float)(currentVolume * mixedWave);

                // Advance phase
                phase += currentFrequency / sampleRate;
                if (phase >= 1.0)
                    phase -= 1.0;
            }

            return count;
        }
    }

    /// <summary>
    /// Audio channel for a wall direction.
    /// </summary>
    public class WallAudioChannel : IDisposable
    {
        public WaveOutEvent OutputDevice;
        public SineWaveGenerator SineGenerator;
        public PanningSampleProvider PanProvider;
        public VolumeSampleProvider VolumeProvider;
        public bool IsPlaying;
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
    /// Wall detection directions.
    /// </summary>
    public enum WallDirection
    {
        Forward,
        Back,
        Left,
        Right
    }

    /// <summary>
    /// Wall navigation audio system - generates continuous tones to indicate wall proximity.
    /// - Forward: medium-high tone
    /// - Back: low tone
    /// - Left/Right: medium tone (with stereo panning)
    /// - Volume increases as walls get closer
    /// </summary>
    public class WallNavigationAudio : MonoBehaviour
    {
        public static WallNavigationAudio Instance { get; private set; }

        // Frequency configuration (Hz)
        private const double FREQ_FORWARD = 500;   // Medium - wall ahead
        private const double FREQ_BACK = 180;      // Low - wall behind
        private const double FREQ_SIDES = 300;     // Medium-low - walls at sides

        // Detection configuration
        private float maxWallDistance = 12f;
        private float minVolumeDistance = 0.5f;
        private float baseVolume = 0.05f;  // Reduced from 0.15 to 0.05 (much quieter)

        // Audio channels (one per direction)
        private Dictionary<WallDirection, WallAudioChannel> channels = new Dictionary<WallDirection, WallAudioChannel>();
        private readonly object channelLock = new object();

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // Layer mask for collisions
        private int wallLayerMask = -1;
        private LayerMaskLibrary layerMaskLibrary;

        // State
        private bool isInitialized = false;
        private bool isEnabled = true;

        // Game state tracking
        private IGameStateProvider gameStateProvider;

        // Scene tracking
        private string lastSceneName = "";
        private float sceneLoadTime = 0f;
        private float sceneStartDelay = 2f;  // Wait 2 seconds after scene load

        private int debugCounter = 0;

        static WallNavigationAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<WallNavigationAudio>();
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

            Plugin.Log.LogInfo("[WallNav] Wall Navigation Audio initialized");
        }

        void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // Try to get LayerMaskLibrary from the game
                try
                {
                    layerMaskLibrary = UnityEngine.Object.FindObjectOfType<LayerMaskLibrary>();
                    if (layerMaskLibrary != null)
                    {
                        // Use the game's obstacle mask for more accurate wall detection
                        wallLayerMask = layerMaskLibrary.obstacleMask;
                        Plugin.Log.LogInfo($"[WallNav] Using game's obstacleMask layer");
                    }
                    else
                    {
                        wallLayerMask = -1;
                        Plugin.Log.LogInfo($"[WallNav] LayerMaskLibrary not found, using all layers");
                    }
                }
                catch
                {
                    wallLayerMask = -1;
                    Plugin.Log.LogInfo($"[WallNav] Using all layers (fallback)");
                }

                // Create audio channels for each direction
                CreateAudioChannels();

                isInitialized = true;
                Plugin.Log.LogInfo($"[WallNav] Initialized with maxDistance={maxWallDistance}, baseVolume={baseVolume}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[WallNav] Initialize error: {e.Message}");
            }
        }

        private void CreateAudioChannels()
        {
            lock (channelLock)
            {
                // Create a channel for each direction
                CreateChannel(WallDirection.Forward, FREQ_FORWARD, 0f);   // Center
                CreateChannel(WallDirection.Back, FREQ_BACK, 0f);         // Center
                CreateChannel(WallDirection.Left, FREQ_SIDES, -1f);       // Left
                CreateChannel(WallDirection.Right, FREQ_SIDES, 1f);       // Right

                Plugin.Log.LogInfo("[WallNav] Audio channels created");
            }
        }

        private void CreateChannel(WallDirection direction, double frequency, float pan)
        {
            try
            {
                // Create sine wave generator (mono)
                var sineGen = new SineWaveGenerator(frequency, 0f); // Start silenced

                // Add panning
                var panProvider = new PanningSampleProvider(sineGen)
                {
                    Pan = pan
                };

                // Add volume control
                var volumeProvider = new VolumeSampleProvider(panProvider)
                {
                    Volume = 1.0f
                };

                // Create output device
                var outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);

                // Create channel
                var channel = new WallAudioChannel
                {
                    OutputDevice = outputDevice,
                    SineGenerator = sineGen,
                    PanProvider = panProvider,
                    VolumeProvider = volumeProvider,
                    IsPlaying = false,
                    IsDisposed = false
                };

                channels[direction] = channel;

                // Start playback (but silenced)
                outputDevice.Play();
                channel.IsPlaying = true;

                Plugin.Log.LogInfo($"[WallNav] Channel created: {direction} at {frequency}Hz, pan={pan}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[WallNav] CreateChannel error for {direction}: {e.Message}");
            }
        }

        void Update()
        {
            if (!isInitialized || !isEnabled) return;

            try
            {
                debugCounter++;

                // Search for player periodically
                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                {
                    if (debugCounter % 300 == 0)
                        Plugin.Log.LogWarning("[WallNav] No player found!");
                    return;
                }

                // Check game state - only play during active gameplay (CORE state)
                if (!IsInActiveGameplay())
                {
                    if (debugCounter % 300 == 0)
                        Plugin.Log.LogDebug("[WallNav] Not in active gameplay");
                    SilenceAllChannels();
                    return;
                }

                // Check scene delay
                bool shouldPause = Time.time - sceneLoadTime < sceneStartDelay;
                if (shouldPause)
                {
                    if (debugCounter % 300 == 0)
                        Plugin.Log.LogDebug("[WallNav] Paused (scene delay)");
                    SilenceAllChannels();
                    return;
                }

                // Update wall detection every frame
                UpdateWallDetection();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[WallNav] Update error: {e.Message}");
            }
        }

        private void FindPlayer()
        {
            try
            {
                // Search for player
                string[] playerNames = { "Player", "PlayerCharacter", "Hero", "Character" };

                foreach (var name in playerNames)
                {
                    var obj = GameObject.Find(name);
                    if (obj != null)
                    {
                        playerTransform = obj.transform;
                        Plugin.Log.LogInfo($"[WallNav] Found player: {name} at {obj.transform.position}");
                        break;
                    }
                }

                // Search for camera
                var cam = Camera.main;
                if (cam != null)
                {
                    cameraTransform = cam.transform;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[WallNav] FindPlayer error: {e.Message}");
            }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[WallNav] Scene changed to {currentScene}");
                    sceneLoadTime = Time.time;
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
                    // Don't play during: MENU, SPLASH, SHOP, LOADING, CORE_INTRO, CORE_OUTRO, etc.
                    return state == GameController.EGameState.CORE;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[WallNav] IsInActiveGameplay error: {e.Message}");
            }

            // Fallback: check time scale
            return Time.timeScale > 0.1f;
        }

        private void UpdateWallDetection()
        {
            if (playerTransform == null) return;

            try
            {
                Vector3 playerPos = playerTransform.position;

                // Use forward from camera or player
                Vector3 forward = Vector3.forward;
                Vector3 right = Vector3.right;

                if (cameraTransform != null)
                {
                    forward = cameraTransform.forward;
                    forward.y = 0;
                    if (forward.sqrMagnitude > 0.001f)
                    {
                        forward = forward.normalized;
                        right = new Vector3(forward.z, 0, -forward.x); // Perpendicular
                    }
                }

                Vector3 back = -forward;
                Vector3 left = -right;

                // Detect walls in each direction
                DetectWall(WallDirection.Forward, playerPos, forward);
                DetectWall(WallDirection.Back, playerPos, back);
                DetectWall(WallDirection.Left, playerPos, left);
                DetectWall(WallDirection.Right, playerPos, right);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[WallNav] UpdateWallDetection error: {e.Message}");
            }
        }

        private void DetectWall(WallDirection direction, Vector3 playerPos, Vector3 dir)
        {
            if (!channels.TryGetValue(direction, out var channel))
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogWarning($"[WallNav] No channel for {direction}");
                return;
            }

            if (channel == null || channel.IsDisposed || channel.SineGenerator == null)
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogWarning($"[WallNav] Channel {direction} is null/disposed");
                return;
            }

            try
            {
                // Raycast from player's waist height
                Vector3 origin = playerPos + Vector3.up * 1.0f;

                bool hasWall = Physics.Raycast(origin, dir, out RaycastHit hit, maxWallDistance, wallLayerMask);

                if (hasWall)
                {
                    float distance = hit.distance;
                    float normalizedDist = Mathf.Clamp01((distance - minVolumeDistance) / (maxWallDistance - minVolumeDistance));
                    float volumeMultiplier = 1f - (normalizedDist * normalizedDist);
                    float finalVolume = baseVolume * volumeMultiplier;

                    channel.SineGenerator.Volume = finalVolume;

                    if (Time.frameCount % 120 == 0)
                    {
                        Plugin.Log.LogInfo($"[WallNav] {direction}: WALL at {distance:F1}m, vol={finalVolume:F2}");
                    }
                }
                else
                {
                    channel.SineGenerator.Volume = 0f;
                }
            }
            catch (Exception e)
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogError($"[WallNav] DetectWall {direction} error: {e.Message}");
            }
        }

        private void SilenceAllChannels()
        {
            lock (channelLock)
            {
                foreach (var channel in channels.Values)
                {
                    if (channel != null && !channel.IsDisposed && channel.SineGenerator != null)
                    {
                        channel.SineGenerator.Volume = 0f;
                    }
                }
            }
        }

        public void SetEnabled(bool enabled)
        {
            isEnabled = enabled;
            if (!enabled)
            {
                SilenceAllChannels();
            }
            Plugin.Log.LogInfo($"[WallNav] System {(enabled ? "enabled" : "disabled")}");
        }

        public void SetMaxDistance(float distance)
        {
            maxWallDistance = Mathf.Max(1f, distance);
            Plugin.Log.LogInfo($"[WallNav] Max distance set to {maxWallDistance}");
        }

        public void SetBaseVolume(float volume)
        {
            baseVolume = Mathf.Clamp01(volume);
            Plugin.Log.LogInfo($"[WallNav] Base volume set to {baseVolume}");
        }

        void OnDestroy()
        {
            lock (channelLock)
            {
                foreach (var channel in channels.Values)
                {
                    channel?.Dispose();
                }
                channels.Clear();
            }

            Instance = null;
            Plugin.Log.LogInfo("[WallNav] Destroyed");
        }
    }
}
