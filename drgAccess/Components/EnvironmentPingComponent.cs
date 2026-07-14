using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components;

/// <summary>
/// On-demand sweep of the surroundings (P key / R3), so the player can ask "what is
/// around me?" instead of relying on the continuous cues alone. Reads the scans the
/// audio systems already run — it never scans the scene itself.
/// </summary>
public class EnvironmentPingComponent : MonoBehaviour
{
    // Danger first, then valuables. Collectible priorities come from the audio system.
    private const float PRIORITY_HAZARD = 100f;
    private const float PRIORITY_BOSS = 95f;
    private const float PRIORITY_EXPLODER = 92f;
    private const float PRIORITY_ELITE = 88f;

    private const int MAX_ENTRIES = 6;

    private struct PingEntry
    {
        public string Name;
        public Vector3 Position;
        public float Distance;
        public float Priority;
    }

    static EnvironmentPingComponent()
    {
        ClassInjector.RegisterTypeInIl2Cpp<EnvironmentPingComponent>();
    }

    void Update()
    {
        try
        {
            if (!InputHelper.PingEnvironment()) return;
            if (!GameStateHelper.IsInActiveGameplay()) return;

            Ping();
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"[Ping] Update error: {e.Message}");
        }
    }

    private void Ping()
    {
        var playerTransform = PlayerLocator.FindPlayerTransform();
        if (playerTransform == null) return;

        Vector3 playerPos = playerTransform.position;
        var cameraTransform = Camera.main != null ? Camera.main.transform : null;
        Vector3 forward = AudioDirectionHelper.GetReferenceForward(cameraTransform);

        var entries = new List<PingEntry>();
        int enemyCount = CollectEnemies(playerPos, entries);
        CollectHazards(playerPos, entries);
        CollectCollectibles(entries);

        if (entries.Count == 0 && enemyCount == 0)
        {
            ScreenReader.Interrupt(ModLocalization.Get("ping_nothing"));
            return;
        }

        entries.Sort((a, b) =>
        {
            int byPriority = b.Priority.CompareTo(a.Priority);
            return byPriority != 0 ? byPriority : a.Distance.CompareTo(b.Distance);
        });

        var sb = new StringBuilder();
        int count = Math.Min(entries.Count, MAX_ENTRIES);
        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];

            Vector3 toTarget = entry.Position - playerPos;
            toTarget.y = 0;
            string direction = toTarget.sqrMagnitude > 0.01f
                ? AudioDirectionHelper.GetDirectionLabel(forward, toTarget.normalized)
                : ModLocalization.Get("dir_ahead");

            if (sb.Length > 0) sb.Append(". ");
            sb.Append(ModLocalization.Get("ping_entry", entry.Name, direction,
                Mathf.RoundToInt(entry.Distance)));
        }

        if (enemyCount > 0)
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(enemyCount == 1
                ? ModLocalization.Get("enemy_nearby_singular")
                : ModLocalization.Get("enemy_nearby_plural", enemyCount));
        }

        ScreenReader.Interrupt(sb.ToString());
    }

    /// <summary>
    /// Nearest boss / exploder / elite get their own entry; everything else only adds
    /// to the head count, which is what keeps the ping short in a busy fight.
    /// </summary>
    private int CollectEnemies(Vector3 playerPos, List<PingEntry> entries)
    {
        int count = 0;

        try
        {
            var tracker = EnemyTracker.Instance;
            if (tracker == null) return 0;

            float maxDist = ModConfig.GetSetting(ModConfig.ENEMY_RANGE);

            bool bossFound = false, exploderFound = false, eliteFound = false;
            PingEntry boss = default, exploder = default, elite = default;

            foreach (var enemy in tracker.GetActiveEnemies())
            {
                try
                {
                    if (enemy == null || !enemy.isAlive) continue;

                    var type = enemy.type;
                    if (type == EEnemyType.COCOON || type == EEnemyType.BIG_COCOON) continue;

                    var id = enemy.id;
                    if (id == EEnemy.LOOTBUG) continue; // reported as a collectible instead

                    Vector3 pos = enemy.position;
                    float dist = Vector3.Distance(playerPos, pos);
                    if (dist > maxDist) continue;

                    count++;

                    bool isExploder = id == EEnemy.EXPLODER || id == EEnemy.EXPLODER_FAST;
                    bool isBoss = !isExploder && type == EEnemyType.BOSS;
                    bool isElite = !isExploder && !isBoss && type == EEnemyType.ELITE;

                    if (isBoss && (!bossFound || dist < boss.Distance))
                    {
                        bossFound = true;
                        boss = MakeEntry(ModLocalization.Get("ping_boss"), pos, dist, PRIORITY_BOSS);
                    }
                    else if (isExploder && (!exploderFound || dist < exploder.Distance))
                    {
                        exploderFound = true;
                        exploder = MakeEntry(ModLocalization.Get("ping_exploder"), pos, dist, PRIORITY_EXPLODER);
                    }
                    else if (isElite && (!eliteFound || dist < elite.Distance))
                    {
                        eliteFound = true;
                        elite = MakeEntry(ModLocalization.Get("ping_elite"), pos, dist, PRIORITY_ELITE);
                    }
                }
                catch { }
            }

            if (bossFound) entries.Add(boss);
            if (exploderFound) entries.Add(exploder);
            if (eliteFound) entries.Add(elite);
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"[Ping] CollectEnemies error: {e.Message}");
        }

        return count;
    }

    private void CollectHazards(Vector3 playerPos, List<PingEntry> entries)
    {
        try
        {
            var hazardAudio = HazardWarningAudio.Instance;
            if (hazardAudio == null) return;

            // One entry per hazard TYPE (the nearest of each): a vine field or a spike
            // volley is many objects but a single thing to avoid.
            var nearestByType = new Dictionary<HazardType, PingEntry>();

            foreach (var hazard in hazardAudio.GetHazardSnapshot())
            {
                float dist = Vector3.Distance(playerPos, hazard.Position);

                if (nearestByType.TryGetValue(hazard.Type, out var existing) &&
                    existing.Distance <= dist)
                    continue;

                nearestByType[hazard.Type] = MakeEntry(
                    GetHazardName(hazard.Type), hazard.Position, dist, PRIORITY_HAZARD);
            }

            foreach (var entry in nearestByType.Values)
                entries.Add(entry);
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"[Ping] CollectHazards error: {e.Message}");
        }
    }

    private void CollectCollectibles(List<PingEntry> entries)
    {
        try
        {
            var collectibles = CollectibleAudioSystem.Instance;
            if (collectibles == null) return;

            foreach (var target in collectibles.GetPingTargets())
            {
                entries.Add(new PingEntry
                {
                    Name = target.Name,
                    Position = target.Position,
                    Distance = target.Distance,
                    Priority = target.Priority
                });
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"[Ping] CollectCollectibles error: {e.Message}");
        }
    }

    private static string GetHazardName(HazardType type) => type switch
    {
        HazardType.SpikyVine => ModLocalization.Get("hazard_spiky_roots"),
        HazardType.GroundSpike => ModLocalization.Get("hazard_ground_spikes"),
        HazardType.FallingRock => ModLocalization.Get("hazard_falling_rock"),
        _ => ModLocalization.Get("ping_exploder")
    };

    private static PingEntry MakeEntry(string name, Vector3 pos, float dist, float priority)
        => new PingEntry { Name = name, Position = pos, Distance = dist, Priority = priority };
}
