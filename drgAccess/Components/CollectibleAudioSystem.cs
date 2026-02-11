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
    /// 3D positional audio for collectible items (pickups, mineral veins, loot crates).
    /// Uses 1 WaveOutEvent + 1 MixingSampleProvider with 7 channels (one per category).
    /// Only the nearest item per category gets audio to avoid overwhelming the player.
    /// </summary>
    public class CollectibleAudioSystem : MonoBehaviour
    {
        public static CollectibleAudioSystem Instance { get; private set; }

        // Audio
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;
        private CollectibleChannel[] channels;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";
        private bool isInitialized = false;
        private float sceneLoadTime = 0f;

        // Pickup scanning
        private float nextPickupScanTime = 0f;

        // Material block scanning
        private List<MineableBlock> cachedMaterialBlocks = new();
        private float nextMaterialScanTime = 0f;

        // Loot crate scanning
        private float nextCrateScanTime = 0f;

        // Category config
        private static readonly CategoryConfig[] configs = new[]
        {
            new CategoryConfig(CollectibleSoundType.RedSugar, 15f, 0.22f, 0.40f, 500f, 750f, 0.5f, 0.08f),
            new CategoryConfig(CollectibleSoundType.GearDrop, 18f, 0.28f, 0.45f, 800f, 1200f, 0.6f, 0.10f),
            new CategoryConfig(CollectibleSoundType.BuffPickup, 12f, 0.18f, 0.35f, 1000f, 1500f, 0.4f, 0.06f),
            new CategoryConfig(CollectibleSoundType.CurrencyPickup, 13f, 0.16f, 0.30f, 600f, 900f, 0.45f, 0.07f),
            new CategoryConfig(CollectibleSoundType.MineralVein, 13f, 0.22f, 0.40f, 300f, 500f, 0.7f, 0.10f),
            new CategoryConfig(CollectibleSoundType.LootCrate, 18f, 0.25f, 0.42f, 1200f, 1800f, 0.5f, 0.08f),
            new CategoryConfig(CollectibleSoundType.XpNearby, 8f, 0.05f, 0.14f, 350f, 700f, 0f, 0f),
        };

        private struct CategoryConfig
        {
            public CollectibleSoundType Type;
            public float MaxDistance;
            public float MinVolume, MaxVolume;
            public float MinFreq, MaxFreq;
            public float MaxInterval, MinInterval; // beacon: far interval, close interval

            public CategoryConfig(CollectibleSoundType type, float maxDist,
                float minVol, float maxVol, float minFreq, float maxFreq,
                float maxInterval, float minInterval)
            {
                Type = type; MaxDistance = maxDist;
                MinVolume = minVol; MaxVolume = maxVol;
                MinFreq = minFreq; MaxFreq = maxFreq;
                MaxInterval = maxInterval; MinInterval = minInterval;
            }
        }

        private class CollectibleChannel
        {
            public CollectibleSoundGenerator Generator;
            public PanningSampleProvider Pan;
            public VolumeSampleProvider Volume;
        }

        // Per-category nearest target tracking
        private struct NearestTarget
        {
            public bool Found;
            public Vector3 Position;
            public float Distance;
        }
        private NearestTarget[] nearestTargets;

        static CollectibleAudioSystem()
        {
            ClassInjector.RegisterTypeInIl2Cpp<CollectibleAudioSystem>();
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

            nearestTargets = new NearestTarget[configs.Length];
            Plugin.Log.LogInfo("[CollectibleAudio] Initialized");
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

                channels = new CollectibleChannel[configs.Length];
                for (int i = 0; i < configs.Length; i++)
                {
                    var gen = new CollectibleSoundGenerator();
                    gen.SoundType = configs[i].Type;
                    var pan = new PanningSampleProvider(gen) { Pan = 0f };
                    var vol = new VolumeSampleProvider(pan) { Volume = 1.0f };
                    mixer.AddMixerInput(vol);
                    channels[i] = new CollectibleChannel { Generator = gen, Pan = pan, Volume = vol };
                }

                outputDevice = new WaveOutEvent { DesiredLatency = 100 };
                outputDevice.Init(mixer);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[CollectibleAudio] Audio initialized (7 channels)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] Init error: {e.Message}");
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

                // Delay audio start after scene load to avoid spam during transitions
                if (Time.time - sceneLoadTime < 3f)
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

                // Scan at different intervals
                if (Time.time >= nextPickupScanTime)
                {
                    ScanPickups();
                    nextPickupScanTime = Time.time + 0.3f;
                }

                if (Time.time >= nextMaterialScanTime)
                {
                    ScanMaterialBlocks();
                    nextMaterialScanTime = Time.time + 3f;
                }

                if (Time.time >= nextCrateScanTime)
                {
                    ScanLootCrates();
                    nextCrateScanTime = Time.time + 1f;
                }

                UpdateAudio();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] Update error: {e.Message}");
            }
        }

        private void ScanPickups()
        {
            // Reset pickup categories
            nearestTargets[0] = default; // RedSugar
            nearestTargets[1] = default; // GearDrop
            nearestTargets[2] = default; // BuffPickup
            nearestTargets[3] = default; // CurrencyPickup
            nearestTargets[6] = default; // XpNearby

            try
            {
                var pickups = UnityEngine.Object.FindObjectsOfType<Pickup>();
                if (pickups == null) return;

                Vector3 playerPos = playerTransform.position;

                foreach (var pickup in pickups)
                {
                    if (pickup == null) continue;
                    if (pickup.state != Pickup.EState.WAITING_TO_BE_CLAIMED) continue;

                    var pickupType = pickup.type;

                    // Map pickup type to category index
                    int categoryIndex = pickupType switch
                    {
                        EPickupType.RED_SUGAR => 0,
                        EPickupType.GEAR => 1,
                        EPickupType.MAGNET => 2,
                        EPickupType.MOVESPEED => 2,
                        EPickupType.MININGSPEED => 2,
                        EPickupType.BERSERK => 2,
                        EPickupType.ARTIFACT_MAGNET => 2,
                        EPickupType.CURRENCY => 3,
                        EPickupType.XP => 6,
                        EPickupType.XP_LARGE => 6,
                        _ => -1 // Duds excluded
                    };

                    if (categoryIndex < 0) continue;

                    Vector3 pos = pickup.transform.position;
                    float dist = Vector3.Distance(playerPos, pos);
                    float maxDist = configs[categoryIndex].MaxDistance;

                    if (dist > maxDist) continue;

                    if (!nearestTargets[categoryIndex].Found || dist < nearestTargets[categoryIndex].Distance)
                    {
                        nearestTargets[categoryIndex] = new NearestTarget
                        {
                            Found = true,
                            Position = pos,
                            Distance = dist
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] ScanPickups error: {e.Message}");
            }
        }

        private void ScanMaterialBlocks()
        {
            nearestTargets[4] = default; // MineralVein

            try
            {
                // Refresh cache periodically
                try
                {
                    var blocks = UnityEngine.Object.FindObjectsOfType<MineableBlock>();
                    cachedMaterialBlocks.Clear();
                    foreach (var block in blocks)
                    {
                        if (block == null) continue;
                        if (!block.hasMaterial) continue;
                        if (block.state != MineableBlock.EState.ALIVE) continue;
                        cachedMaterialBlocks.Add(block);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[CollectibleAudio] FindMaterialBlocks error: {e.Message}");
                    return;
                }

                if (cachedMaterialBlocks.Count == 0) return;

                Vector3 playerPos = playerTransform.position;
                float maxDist = configs[4].MaxDistance;
                float nearestDist = float.MaxValue;
                Vector3 nearestPos = Vector3.zero;

                for (int i = cachedMaterialBlocks.Count - 1; i >= 0; i--)
                {
                    var block = cachedMaterialBlocks[i];
                    try
                    {
                        if (block == null || block.state != MineableBlock.EState.ALIVE)
                        {
                            cachedMaterialBlocks.RemoveAt(i);
                            continue;
                        }
                    }
                    catch
                    {
                        cachedMaterialBlocks.RemoveAt(i);
                        continue;
                    }

                    float dist = Vector3.Distance(playerPos, block.transform.position);
                    if (dist <= maxDist && dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestPos = block.transform.position;
                    }
                }

                if (nearestDist < float.MaxValue)
                {
                    nearestTargets[4] = new NearestTarget
                    {
                        Found = true,
                        Position = nearestPos,
                        Distance = nearestDist
                    };
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] ScanMaterialBlocks error: {e.Message}");
            }
        }

        private void ScanLootCrates()
        {
            nearestTargets[5] = default; // LootCrate

            try
            {
                var crates = UnityEngine.Object.FindObjectsOfType<LootCrate>();
                if (crates == null) return;

                Vector3 playerPos = playerTransform.position;
                float maxDist = configs[5].MaxDistance;

                foreach (var crate in crates)
                {
                    if (crate == null) continue;
                    var crateState = crate.state;
                    if (crateState != LootCrate.EState.CLOSED &&
                        crateState != LootCrate.EState.WAITING_FOR_PLAYER)
                        continue;

                    float dist = Vector3.Distance(playerPos, crate.transform.position);
                    if (dist > maxDist) continue;

                    if (!nearestTargets[5].Found || dist < nearestTargets[5].Distance)
                    {
                        nearestTargets[5] = new NearestTarget
                        {
                            Found = true,
                            Position = crate.transform.position,
                            Distance = dist
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] ScanLootCrates error: {e.Message}");
            }
        }

        private void UpdateAudio()
        {
            Vector3 playerPos = playerTransform.position;

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0, -forward.x);

            for (int i = 0; i < configs.Length; i++)
            {
                var config = configs[i];
                var target = nearestTargets[i];
                var channel = channels[i];

                if (!target.Found)
                {
                    channel.Generator.Active = false;
                    continue;
                }

                // Calculate pan
                Vector3 toTarget = target.Position - playerPos;
                toTarget.y = 0;
                if (toTarget.sqrMagnitude > 0.01f)
                    toTarget.Normalize();
                float pan = Mathf.Clamp(Vector3.Dot(toTarget, right), -1f, 1f);
                channel.Pan.Pan = pan;

                // Proximity factor (0 = far, 1 = close)
                float proximity = 1f - Mathf.Clamp01(target.Distance / config.MaxDistance);
                proximity *= proximity; // Squared for aggressive close-range emphasis

                // Frequency: increases with proximity, modulated by direction
                float freq = config.MinFreq + proximity * (config.MaxFreq - config.MinFreq);
                float pitchMult = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toTarget);
                channel.Generator.Frequency = freq * pitchMult;

                // Volume: increases with proximity
                float volume = config.MinVolume + proximity * (config.MaxVolume - config.MinVolume);
                channel.Generator.Volume = volume;

                // Interval (beacon types only, MineralVein is continuous)
                if (config.MaxInterval > 0)
                {
                    float interval = Mathf.Lerp(config.MaxInterval, config.MinInterval, proximity);
                    channel.Generator.Interval = interval;
                }

                channel.Generator.Active = true;
            }
        }

        private void SilenceAll()
        {
            if (channels == null) return;
            for (int i = 0; i < channels.Length; i++)
                channels[i].Generator.Active = false;
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[CollectibleAudio] Scene changed to {currentScene} - resetting");
                    ResetState();
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] CheckSceneChange error: {e.Message}");
            }
        }

        private void ResetState()
        {
            playerTransform = null;
            cameraTransform = null;
            nextPlayerSearchTime = 0f;
            gameStateProvider = null;
            cachedMaterialBlocks.Clear();
            sceneLoadTime = Time.time;
            for (int i = 0; i < nearestTargets.Length; i++)
                nearestTargets[i] = default;
            SilenceAll();
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
            Plugin.Log.LogInfo("[CollectibleAudio] Destroyed");
        }
    }
}
