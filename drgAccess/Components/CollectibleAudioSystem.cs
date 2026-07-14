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
        private GameController cachedGameController; // for player HP (smart beacon)
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

        // Objective resource + lootbug scanning
        private float nextObjectiveResScanTime = 0f;
        private float nextLootbugScanTime = 0f;

        // Category config
        private static readonly CategoryConfig[] configs = new[]
        {
            new CategoryConfig(CollectibleSoundType.RedSugar, 30f, 0.22f, 0.40f, 500f, 750f, 0.5f, 0.08f),
            new CategoryConfig(CollectibleSoundType.GearDrop, 40f, 0.28f, 0.45f, 800f, 1200f, 0.6f, 0.10f),
            new CategoryConfig(CollectibleSoundType.BuffPickup, 25f, 0.18f, 0.35f, 1000f, 1500f, 0.4f, 0.06f),
            new CategoryConfig(CollectibleSoundType.CurrencyPickup, 28f, 0.16f, 0.30f, 600f, 900f, 0.45f, 0.07f),
            new CategoryConfig(CollectibleSoundType.MineralVein, 28f, 0.22f, 0.40f, 300f, 500f, 0.7f, 0.10f),
            new CategoryConfig(CollectibleSoundType.LootCrate, 40f, 0.25f, 0.42f, 1200f, 1800f, 0.5f, 0.08f),
            // XP range/volume raised in v0.10.6 — users could not hear it at all
            new CategoryConfig(CollectibleSoundType.XpNearby, 14f, 0.12f, 0.28f, 350f, 700f, 0f, 0f),
            new CategoryConfig(CollectibleSoundType.BobbyFuel, 35f, 0.22f, 0.42f, 200f, 400f, 0.5f, 0.08f),
            new CategoryConfig(CollectibleSoundType.HealingZone, 30f, 0.22f, 0.40f, 500f, 750f, 0.5f, 0.08f),
            new CategoryConfig(CollectibleSoundType.LaunchPad, 35f, 0.25f, 0.45f, 400f, 800f, 0.7f, 0.12f),
            // Resources an objective asks for (Morkite veins, Apoca Bloom, Boolo Cap):
            // longer range than plain minerals — the run cannot end without them
            new CategoryConfig(CollectibleSoundType.ObjectiveRes, 40f, 0.26f, 0.46f, 550f, 900f, 0.6f, 0.10f),
            new CategoryConfig(CollectibleSoundType.Lootbug, 25f, 0.18f, 0.34f, 700f, 1100f, 0.45f, 0.12f),
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
            public string Name; // Specific item name (e.g. "Gold", "Magnet") or null for category default
        }
        private NearestTarget[] nearestTargets;

        // Item proximity announcements (zone-based like drop pod)
        private int[] lastItemZone; // 0=none, 1=nearby, 2=closer, 3=very close
        private static readonly string[] categoryNameKeys =
        {
            "collect_red_sugar", "collect_gear", "collect_buff", "collect_currency",
            "collect_mineral_vein", "collect_loot_crate", "collect_xp",
            "collect_bobby_fuel", "collect_healing_zone", "collect_launch_pad",
            "collect_objective_resource", "collect_lootbug"
        };

        // Bobby fuel scanning
        private float nextFuelScanTime = 0f;

        // Healing zone scanning
        private float nextHealingScanTime = 0f;

        // Launch pad scanning (name-based: pads have no dedicated managed class)
        private float nextLaunchPadScanTime = 0f;
        private readonly List<Transform> cachedLaunchPads = new();

        /// <summary>
        /// Returns the effective max distance for a category, scaled by the config multiplier.
        /// </summary>
        private float GetEffectiveMaxDistance(int categoryIndex)
        {
            return configs[categoryIndex].MaxDistance * ModConfig.GetSetting(ModConfig.COLLECTIBLE_RANGE);
        }

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
            lastItemZone = new int[configs.Length];
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
                Plugin.Log.LogInfo($"[CollectibleAudio] Audio initialized ({configs.Length} channels)");
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

                if (Time.time >= nextObjectiveResScanTime)
                {
                    ScanObjectiveResources();
                    nextObjectiveResScanTime = Time.time + 1f;
                }

                // Lootbugs run away, so they need a pickup-rate scan, not a block-rate one
                if (Time.time >= nextLootbugScanTime)
                {
                    ScanLootbugs();
                    nextLootbugScanTime = Time.time + 0.3f;
                }

                if (Time.time >= nextCrateScanTime)
                {
                    ScanLootCrates();
                    nextCrateScanTime = Time.time + 1f;
                }

                if (Time.time >= nextFuelScanTime)
                {
                    ScanBobbyFuel();
                    nextFuelScanTime = Time.time + 2f;
                }

                if (Time.time >= nextHealingScanTime)
                {
                    ScanHealingZones();
                    nextHealingScanTime = Time.time + 2f;
                }

                if (Time.time >= nextLaunchPadScanTime)
                {
                    ScanLaunchPads();
                    nextLaunchPadScanTime = Time.time + 3f;
                }

                // Snapshot before UpdateAudio: the smart-beacon filter clears every
                // target except its winner, and the ping must still see them all
                if (pingSnapshot == null || pingSnapshot.Length != nearestTargets.Length)
                    pingSnapshot = new NearestTarget[nearestTargets.Length];
                Array.Copy(nearestTargets, pingSnapshot, nearestTargets.Length);

                UpdateAudio();
                AnnounceItems();
                LogScanDiagnostics();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] Update error: {e.Message}");
            }
        }

        // Periodic scan snapshot so user logs show what each category detected
        private float nextDiagTime;
        private void LogScanDiagnostics()
        {
            if (Time.time < nextDiagTime) return;
            nextDiagTime = Time.time + 10f;

            var sb = new System.Text.StringBuilder("[CollectibleAudio] targets:");
            bool any = false;
            for (int i = 0; i < configs.Length; i++)
            {
                if (!nearestTargets[i].Found) continue;
                any = true;
                sb.Append($" {configs[i].Type}={nearestTargets[i].Distance:0.0}m");
            }
            if (!any) sb.Append(" none");
            Plugin.Log.LogInfo(sb.ToString());
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
                        // Since the Unity 6 update, ground XP orbs convert into "duds"
                        // over time (XpDudConfig.DudChancePrSecond) — excluding them
                        // silenced the XP cue almost entirely. Duds are still
                        // collectible XP, so they count.
                        EPickupType.XP_DUD => 6,
                        EPickupType.XP_LARGE_DUD => 6,
                        _ => -1
                    };

                    if (categoryIndex < 0) continue;

                    Vector3 pos = pickup.transform.position;
                    float dist = Vector3.Distance(playerPos, pos);
                    float maxDist = GetEffectiveMaxDistance(categoryIndex);

                    if (dist > maxDist) continue;

                    if (!nearestTargets[categoryIndex].Found || dist < nearestTargets[categoryIndex].Distance)
                    {
                        // Get specific item name
                        string itemName = null;
                        try
                        {
                            itemName = pickupType switch
                            {
                                EPickupType.MAGNET => ModLocalization.Get("collect_magnet"),
                                EPickupType.MOVESPEED => ModLocalization.Get("collect_speed_boost"),
                                EPickupType.MININGSPEED => ModLocalization.Get("collect_mining_speed"),
                                EPickupType.BERSERK => ModLocalization.Get("collect_berserk"),
                                EPickupType.ARTIFACT_MAGNET => ModLocalization.Get("collect_artifact_magnet"),
                                EPickupType.CURRENCY => LocalizationHelper.GetCurrencyName(pickup.currencyType),
                                _ => null
                            };
                        }
                        catch { }

                        nearestTargets[categoryIndex] = new NearestTarget
                        {
                            Found = true,
                            Position = pos,
                            Distance = dist,
                            Name = itemName
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
                float maxDist = GetEffectiveMaxDistance(4);
                float nearestDist = float.MaxValue;
                Vector3 nearestPos = Vector3.zero;
                string nearestMineralName = null;

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

                    // Minerals an objective asks for (e.g. Morkite) get their own,
                    // higher-priority cue — see ScanObjectiveResources
                    try
                    {
                        if (ObjectiveHelper.IsObjectiveCurrency(block.materialType)) continue;
                    }
                    catch { }

                    float dist = Vector3.Distance(playerPos, block.transform.position);
                    if (dist <= maxDist && dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestPos = block.transform.position;
                        try { nearestMineralName = LocalizationHelper.GetCurrencyName(block.materialType); }
                        catch { nearestMineralName = null; }
                    }
                }

                if (nearestDist < float.MaxValue)
                {
                    nearestTargets[4] = new NearestTarget
                    {
                        Found = true,
                        Position = nearestPos,
                        Distance = nearestDist,
                        Name = nearestMineralName
                    };
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] ScanMaterialBlocks error: {e.Message}");
            }
        }

        /// <summary>
        /// Resources the current objectives require. Two different game classes back
        /// these: mission minerals (Morkite and friends) are ordinary MineableBlocks
        /// singled out by materialType, while Apoca Bloom / Boolo Cap are LevelPickups,
        /// a class the scanner ignored entirely — hence "not announced".
        /// </summary>
        private void ScanObjectiveResources()
        {
            nearestTargets[10] = default; // ObjectiveRes

            try
            {
                Vector3 playerPos = playerTransform.position;
                float maxDist = GetEffectiveMaxDistance(10);

                // Mission minerals — reuse the block cache ScanMaterialBlocks maintains
                foreach (var block in cachedMaterialBlocks)
                {
                    try
                    {
                        if (block == null || block.state != MineableBlock.EState.ALIVE) continue;
                        if (!ObjectiveHelper.IsObjectiveCurrency(block.materialType)) continue;

                        float dist = Vector3.Distance(playerPos, block.transform.position);
                        if (dist > maxDist) continue;

                        if (!nearestTargets[10].Found || dist < nearestTargets[10].Distance)
                        {
                            string name = null;
                            try { name = LocalizationHelper.GetCurrencyName(block.materialType); }
                            catch { }

                            nearestTargets[10] = new NearestTarget
                            {
                                Found = true,
                                Position = block.transform.position,
                                Distance = dist,
                                Name = name
                            };
                        }
                    }
                    catch { }
                }

                // Apoca Bloom / Boolo Cap
                var levelPickups = UnityEngine.Object.FindObjectsOfType<LevelPickup>();
                if (levelPickups != null)
                {
                    foreach (var pickup in levelPickups)
                    {
                        try
                        {
                            if (pickup == null) continue;
                            if (pickup.state != LevelPickup.EState.WAITING_TO_BE_CLAIMED) continue;

                            var type = pickup.type;
                            if (!ObjectiveHelper.IsObjectivePickup(type)) continue;

                            float dist = Vector3.Distance(playerPos, pickup.transform.position);
                            if (dist > maxDist) continue;

                            if (!nearestTargets[10].Found || dist < nearestTargets[10].Distance)
                            {
                                nearestTargets[10] = new NearestTarget
                                {
                                    Found = true,
                                    Position = pickup.transform.position,
                                    Distance = dist,
                                    Name = ObjectiveHelper.GetObjectivePickupName(type)
                                };
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] ScanObjectiveResources error: {e.Message}");
            }
        }

        /// <summary>
        /// Lootbugs. They are enemies, so EnemyAudioSystem filters the common ones out
        /// (they are far too numerous to beep at). Here only the NEAREST one gets a cue,
        /// like any other collectible — which is what makes them announceable at all.
        /// </summary>
        private void ScanLootbugs()
        {
            nearestTargets[11] = default; // Lootbug

            try
            {
                var tracker = EnemyTracker.Instance;
                if (tracker == null) return;

                Vector3 playerPos = playerTransform.position;
                float maxDist = GetEffectiveMaxDistance(11);

                foreach (var enemy in tracker.GetActiveEnemies())
                {
                    try
                    {
                        if (enemy == null || !enemy.isAlive) continue;
                        if (enemy.id != EEnemy.LOOTBUG) continue;

                        float dist = Vector3.Distance(playerPos, enemy.position);
                        if (dist > maxDist) continue;

                        if (!nearestTargets[11].Found || dist < nearestTargets[11].Distance)
                        {
                            nearestTargets[11] = new NearestTarget
                            {
                                Found = true,
                                Position = enemy.position,
                                Distance = dist
                            };
                        }
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[CollectibleAudio] ScanLootbugs error: {e.Message}");
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
                float maxDist = GetEffectiveMaxDistance(5);

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

                // Landed supply pods waiting for the player to collect their loot.
                // The supply ZONE beacon (ActivationZoneAudio) goes silent once the
                // zone completes, so without this the loot itself had no cue.
                var pods = UnityEngine.Object.FindObjectsOfType<SupplyPod>();
                if (pods != null)
                {
                    foreach (var pod in pods)
                    {
                        if (pod == null) continue;
                        var podState = pod.state;
                        if (podState != SupplyPod.EState.OPENING &&
                            podState != SupplyPod.EState.WAITING_FOR_PLAYER)
                            continue;

                        float dist = Vector3.Distance(playerPos, pod.transform.position);
                        if (dist > maxDist) continue;

                        if (!nearestTargets[5].Found || dist < nearestTargets[5].Distance)
                        {
                            nearestTargets[5] = new NearestTarget
                            {
                                Found = true,
                                Position = pod.transform.position,
                                Distance = dist,
                                Name = ModLocalization.Get("collect_supply_loot")
                            };
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CollectibleAudio] ScanLootCrates error: {e.Message}");
            }
        }

        private void ScanBobbyFuel()
        {
            nearestTargets[7] = default; // BobbyFuel

            try
            {
                var fuelBlocks = UnityEngine.Object.FindObjectsOfType<MaterialBlockBobbyFuel>();
                if (fuelBlocks == null || fuelBlocks.Length == 0) return;

                Vector3 playerPos = playerTransform.position;
                float maxDist = GetEffectiveMaxDistance(7);

                foreach (var block in fuelBlocks)
                {
                    if (block == null) continue;
                    // Only track alive fuel blocks
                    try
                    {
                        if (block.state != MineableBlock.EState.ALIVE) continue;
                    }
                    catch { continue; }

                    float dist = Vector3.Distance(playerPos, block.transform.position);
                    if (dist > maxDist) continue;

                    if (!nearestTargets[7].Found || dist < nearestTargets[7].Distance)
                    {
                        nearestTargets[7] = new NearestTarget
                        {
                            Found = true,
                            Position = block.transform.position,
                            Distance = dist
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[CollectibleAudio] ScanBobbyFuel error: {e.Message}");
            }
        }

        private void ScanHealingZones()
        {
            nearestTargets[8] = default; // HealingZone

            try
            {
                var pillars = UnityEngine.Object.FindObjectsOfType<AzureWealdBuffPillars>();
                if (pillars == null || pillars.Length == 0) return;

                Vector3 playerPos = playerTransform.position;
                float maxDist = GetEffectiveMaxDistance(8);

                foreach (var pillar in pillars)
                {
                    if (pillar == null) continue;
                    // Only track active zones (center block still alive)
                    try
                    {
                        var center = pillar.centerBlock;
                        if (center == null || center.state != MineableBlock.EState.ALIVE) continue;
                    }
                    catch { continue; }

                    float dist = Vector3.Distance(playerPos, pillar.transform.position);
                    if (dist > maxDist) continue;

                    if (!nearestTargets[8].Found || dist < nearestTargets[8].Distance)
                    {
                        nearestTargets[8] = new NearestTarget
                        {
                            Found = true,
                            Position = pillar.transform.position,
                            Distance = dist
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[CollectibleAudio] ScanHealingZones error: {e.Message}");
            }
        }

        // GameObject name fragments that identify launch ramps/catapults in biomes
        // that may use a different mechanism than AzureWealdMagicHole. Plain "ramp"
        // is excluded on purpose — it would match the drop pod's rampDetector.
        private static readonly string[] launchPadNameFragments =
        {
            "jumppad", "jump_pad", "jump pad",
            "launchpad", "launch_pad", "launch pad",
            "catapult", "trampoline", "springboard", "magichole", "magic_hole"
        };

        private void ScanLaunchPads()
        {
            nearestTargets[9] = default; // LaunchPad

            try
            {
                // Revalidate cache (objects die on stage end)
                for (int i = cachedLaunchPads.Count - 1; i >= 0; i--)
                {
                    var t = cachedLaunchPads[i];
                    bool alive;
                    try { alive = t != null && t.gameObject != null && t.gameObject.activeInHierarchy; }
                    catch { alive = false; }
                    if (!alive) cachedLaunchPads.RemoveAt(i);
                }

                if (cachedLaunchPads.Count == 0)
                {
                    // Primary source: the game's own jump-zone class (Azure Weald
                    // bounce zones that fling the player — required for a mission node)
                    var holes = UnityEngine.Object.FindObjectsOfType<AzureWealdMagicHole>();
                    if (holes != null)
                    {
                        foreach (var hole in holes)
                        {
                            if (hole == null) continue;
                            cachedLaunchPads.Add(hole.transform);
                        }
                        if (holes.Length > 0)
                            Plugin.Log.LogInfo($"[CollectibleAudio] {holes.Length} jump zone(s) (AzureWealdMagicHole) found");
                    }
                }

                // Fallback: name-based collider scan for launcher variants in other
                // biomes that may not use AzureWealdMagicHole
                if (cachedLaunchPads.Count == 0)
                {
                    var colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
                    if (colliders != null)
                    {
                        foreach (var col in colliders)
                        {
                            if (col == null) continue;
                            string name;
                            try { name = col.gameObject.name.ToLowerInvariant(); }
                            catch { continue; }

                            foreach (var fragment in launchPadNameFragments)
                            {
                                if (name.Contains(fragment))
                                {
                                    cachedLaunchPads.Add(col.transform);
                                    Plugin.Log.LogInfo($"[CollectibleAudio] Launch pad found by name: '{col.gameObject.name}'");
                                    break;
                                }
                            }
                        }
                    }
                }

                if (cachedLaunchPads.Count == 0) return;

                Vector3 playerPos = playerTransform.position;
                float maxDist = GetEffectiveMaxDistance(9);

                foreach (var pad in cachedLaunchPads)
                {
                    if (pad == null) continue;
                    float dist = Vector3.Distance(playerPos, pad.position);
                    if (dist > maxDist) continue;

                    if (!nearestTargets[9].Found || dist < nearestTargets[9].Distance)
                    {
                        nearestTargets[9] = new NearestTarget
                        {
                            Found = true,
                            Position = pad.position,
                            Distance = dist
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[CollectibleAudio] ScanLaunchPads error: {e.Message}");
            }
        }

        /// <summary>
        /// Called when the player gets launched by a pad (Player.TryLaunchIntoAir patch).
        /// Logs nearby trigger names once so unknown pad prefab names can be added to
        /// launchPadNameFragments from user logs.
        /// </summary>
        private float lastLaunchDiagnosticTime = -60f;
        public void OnPlayerLaunched()
        {
            try
            {
                if (playerTransform == null) return;
                if (Time.time - lastLaunchDiagnosticTime < 30f) return;
                lastLaunchDiagnosticTime = Time.time;

                var nearby = Physics.OverlapSphere(playerTransform.position, 4f);
                if (nearby == null) return;
                foreach (var col in nearby)
                {
                    if (col == null) continue;
                    try
                    {
                        if (col.isTrigger)
                            Plugin.Log.LogInfo($"[CollectibleAudio] Launch source candidate: '{col.gameObject.name}'");
                    }
                    catch { }
                }
            }
            catch { }
        }

        // --- Smart beacon (priority scoring, Hades 2 style) ---
        // Base priority per category, indexed like configs[]. Reflects gameplay
        // importance; distance only breaks ties within the score formula below.
        private static readonly float[] categoryBasePriority =
        {
            50f,  // RedSugar (boosted when hurt)
            75f,  // GearDrop
            60f,  // BuffPickup
            40f,  // CurrencyPickup
            45f,  // MineralVein
            80f,  // LootCrate
            10f,  // XpNearby
            85f,  // BobbyFuel (escort-critical)
            55f,  // HealingZone (boosted when hurt)
            30f,  // LaunchPad
            90f,  // ObjectiveRes (the run cannot be completed without these)
            35f,  // Lootbug
        };
        // Below this HP fraction, healing sources get their priority multiplied.
        private const float LOW_HP_FRACTION = 0.4f;
        private const float LOW_HP_HEAL_BOOST = 2.5f;

        /// <summary>
        /// Smart beacon mode: instead of one beacon per category playing at once,
        /// score every found target by importance x proximity and keep only the
        /// winner audible. The winner keeps its own category sound, so the player
        /// still hears WHAT it is.
        /// </summary>
        private void ApplySmartBeaconFilter()
        {
            int best = -1;
            float bestScore = 0f;
            float hpFraction = GetPlayerHealthFraction();

            for (int i = 0; i < configs.Length; i++)
            {
                if (!nearestTargets[i].Found) continue;

                float proximity = 1f - Mathf.Clamp01(nearestTargets[i].Distance / GetEffectiveMaxDistance(i));
                float basePriority = categoryBasePriority[i];

                // Health needs: when hurt, healing sources jump the queue
                var type = configs[i].Type;
                if (hpFraction < LOW_HP_FRACTION &&
                    (type == CollectibleSoundType.RedSugar || type == CollectibleSoundType.HealingZone))
                    basePriority *= LOW_HP_HEAL_BOOST;

                // Priority dominates; proximity modulates within the category
                float score = basePriority * (0.5f + 0.5f * proximity);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            for (int i = 0; i < configs.Length; i++)
            {
                if (i != best) nearestTargets[i] = default;
            }
        }

        private float GetPlayerHealthFraction()
        {
            try
            {
                var gameController = GameStateHelper.CachedGameController;
                if (gameController != null)
                {
                    var player = gameController.player;
                    if (player != null)
                        return player.CurrentHealthFraction;
                }
            }
            catch { }
            return 1f;
        }

        private void UpdateAudio()
        {
            if (ModConfig.GetBool(ModConfig.SMART_BEACON))
                ApplySmartBeaconFilter();

            Vector3 playerPos = playerTransform.position;

            Vector3 forward = AudioDirectionHelper.GetReferenceForward(cameraTransform);
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
                float proximity = 1f - Mathf.Clamp01(target.Distance / GetEffectiveMaxDistance(i));
                proximity *= proximity; // Squared for aggressive close-range emphasis

                // Frequency: increases with proximity, modulated by direction
                float freq = config.MinFreq + proximity * (config.MaxFreq - config.MinFreq);
                float pitchMult = AudioDirectionHelper.GetDirectionalPitchMultiplier(forward, toTarget);
                channel.Generator.Frequency = freq * pitchMult;

                // Volume: increases with proximity
                float volume = config.MinVolume + proximity * (config.MaxVolume - config.MinVolume);
                channel.Generator.Volume = volume * ModConfig.GetVolume(ModConfig.COLLECTIBLES);

                // Interval (beacon types only, MineralVein is continuous)
                if (config.MaxInterval > 0)
                {
                    float interval = Mathf.Lerp(config.MaxInterval, config.MinInterval, proximity);
                    channel.Generator.Interval = interval;
                }

                channel.Generator.Active = true;
            }
        }

        private void AnnounceItems()
        {
            try
            {
                Vector3 playerPos = playerTransform.position;
                Vector3 forward = AudioDirectionHelper.GetReferenceForward(cameraTransform);

                for (int i = 0; i < configs.Length; i++)
                {
                    // Skip XP — too common, not worth announcing
                    if (configs[i].Type == CollectibleSoundType.XpNearby)
                    {
                        lastItemZone[i] = 0;
                        continue;
                    }

                    int zone = 0; // none
                    if (nearestTargets[i].Found)
                    {
                        float maxDist = GetEffectiveMaxDistance(i);
                        float dist = nearestTargets[i].Distance;
                        float ratio = dist / maxDist;

                        if (ratio <= 0.25f)
                            zone = 3; // very close
                        else if (ratio <= 0.55f)
                            zone = 2; // closer
                        else
                            zone = 1; // nearby
                    }

                    // Announce only on zone transitions (getting closer or first detection)
                    if (zone > lastItemZone[i])
                    {
                        string direction = "";
                        if (nearestTargets[i].Found)
                        {
                            Vector3 toTarget = nearestTargets[i].Position - playerPos;
                            toTarget.y = 0;
                            if (toTarget.sqrMagnitude > 0.01f)
                            {
                                toTarget.Normalize();
                                direction = " " + GetDirectionLabel(forward, toTarget);
                            }
                        }

                        string name = nearestTargets[i].Name ?? ModLocalization.Get(categoryNameKeys[i]);
                        string label = zone switch
                        {
                            1 => ModLocalization.Get("collect_nearby", name, direction),
                            2 => ModLocalization.Get("collect_closer", name, direction),
                            3 => ModLocalization.Get("collect_very_close", name, direction),
                            _ => null
                        };
                        if (label != null)
                            ScreenReader.Say(label);
                    }

                    lastItemZone[i] = zone;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"[CollectibleAudio] AnnounceItems error: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns a screen-relative direction label (up/down/left/right/diagonals)
        /// adapted for top-down perspective (up=W, down=S, left=A, right=D).
        /// </summary>
        private string GetDirectionLabel(Vector3 forward, Vector3 toTarget)
            => AudioDirectionHelper.GetDirectionLabel(forward, toTarget);

        /// <summary>
        /// What the environment ping reports: the nearest item of every category found
        /// by the scans, taken BEFORE the smart-beacon filter narrows the audio down to
        /// a single winner — the ping is meant to list everything, not just what is
        /// currently audible.
        /// </summary>
        public struct PingTarget
        {
            public string Name;
            public Vector3 Position;
            public float Distance;
            public float Priority;
        }

        private NearestTarget[] pingSnapshot;

        public List<PingTarget> GetPingTargets()
        {
            var result = new List<PingTarget>();
            var snapshot = pingSnapshot;
            if (snapshot == null) return result;

            for (int i = 0; i < configs.Length && i < snapshot.Length; i++)
            {
                if (!snapshot[i].Found) continue;
                if (configs[i].Type == CollectibleSoundType.XpNearby) continue; // everywhere, never news

                string name = snapshot[i].Name;
                if (string.IsNullOrEmpty(name))
                {
                    try { name = ModLocalization.Get(categoryNameKeys[i]); }
                    catch { continue; }
                }

                result.Add(new PingTarget
                {
                    Name = name,
                    Position = snapshot[i].Position,
                    Distance = snapshot[i].Distance,
                    Priority = categoryBasePriority[i]
                });
            }

            return result;
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
            nextFuelScanTime = 0f;
            nextHealingScanTime = 0f;
            sceneLoadTime = Time.time;
            for (int i = 0; i < nearestTargets.Length; i++)
                nearestTargets[i] = default;
            if (lastItemZone != null)
                Array.Clear(lastItemZone, 0, lastItemZone.Length);
            SilenceAll();
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

        private bool IsInActiveGameplay()
        {
            return drgAccess.Helpers.GameStateHelper.IsInActiveGameplay();
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
