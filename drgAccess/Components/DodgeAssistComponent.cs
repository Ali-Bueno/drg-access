using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Dodge assistance for Dreadnought boss attacks. The telegraph announcements
    /// (BossAttackPatches) say WHAT is coming; this component says whether the player
    /// is actually IN DANGER and which way to move, using the real attack geometry:
    /// - Charge: corridor check against the boss's charge line (width from its collider)
    /// - Ground spikes: per-spike damage radius from GroundSpike.OnSpawn
    /// - Fireball (burp): "move now" nudge if the player hasn't moved after the telegraph
    /// Directions are screen-relative (up/down/left/right), matching WASD movement.
    /// </summary>
    public class DodgeAssistComponent : MonoBehaviour
    {
        public static DodgeAssistComponent Instance { get; private set; }

        // --- Charge tracking ---
        private Transform chargingBoss;
        private Vector3 chargeOrigin;
        private Vector3 chargeDir;
        private bool chargeLocked;          // true once BeginCharge fixed the direction
        private float chargeEndTime;
        private float chargeHalfWidth;
        private bool wasInChargePath;
        private float nextChargeWarnTime;
        // How long a charge is monitored after the telegraph. Covers telegraph
        // (~1s) + charge run; cleared early when the boss reference dies.
        private const float CHARGE_MONITOR_SECONDS = 4f;
        // Fallback half-width when no collider is found on the boss; generous
        // to err on the side of warning.
        private const float FALLBACK_CHARGE_HALF_WIDTH = 3f;
        // Extra safety margin added around attack shapes.
        private const float DODGE_MARGIN = 1f;
        private const float CHARGE_WARN_INTERVAL = 0.9f;

        // --- Spike tracking ---
        private struct SpikeInfo
        {
            public Vector3 Pos;
            public float Radius;
            public float ExpireTime;
        }
        private readonly List<SpikeInfo> activeSpikes = new List<SpikeInfo>();
        private float nextSpikeWarnTime;
        private const float SPIKE_WARN_INTERVAL = 1.2f;

        // --- Fireball nudge ---
        private Vector3 fireballPlayerPos;
        private float fireballCheckTime;
        private bool fireballPending;
        // If the player has moved less than this since the telegraph, they are
        // probably standing in the blast — nudge them.
        private const float FIREBALL_SAFE_MOVE_DISTANCE = 2.5f;
        private const float FIREBALL_CHECK_DELAY = 1.2f;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime;

        static DodgeAssistComponent()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DodgeAssistComponent>();
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
            Plugin.Log.LogInfo("[DodgeAssist] Initialized");
        }

        // === Public API (called from patches) ===

        /// <summary>Charge telegraph began: predict the charge line (boss → player).</summary>
        public void OnChargeTelegraph(Transform boss)
        {
            if (boss == null) return;
            chargingBoss = boss;
            chargeOrigin = boss.position;
            chargeLocked = false;
            chargeEndTime = Time.time + CHARGE_MONITOR_SECONDS;
            chargeHalfWidth = MeasureBossHalfWidth(boss) + DODGE_MARGIN;
            wasInChargePath = false;
            nextChargeWarnTime = 0f;

            // Direction: the dreadnought charges at the player's current position
            if (playerTransform != null)
            {
                chargeDir = playerTransform.position - chargeOrigin;
                chargeDir.y = 0;
                chargeDir.Normalize();
            }
        }

        /// <summary>Charge actually started: lock direction to the boss's facing.</summary>
        public void OnChargeStarted(Transform boss)
        {
            if (boss == null) return;
            chargingBoss = boss;
            chargeOrigin = boss.position;
            var dir = boss.forward;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
            {
                chargeDir = dir.normalized;
                chargeLocked = true;
            }
            chargeEndTime = Time.time + CHARGE_MONITOR_SECONDS;
        }

        /// <summary>A ground spike spawned with its real damage radius.</summary>
        public void OnSpikeSpawned(Vector3 pos, float life, float damageRadius)
        {
            if (damageRadius <= 0f) return;
            activeSpikes.Add(new SpikeInfo
            {
                Pos = pos,
                Radius = damageRadius,
                ExpireTime = Time.time + Mathf.Max(life, 0.5f)
            });
        }

        /// <summary>Fireball telegraph: nudge the player if they stand still.</summary>
        public void OnFireballTelegraph()
        {
            if (playerTransform == null) return;
            fireballPlayerPos = playerTransform.position;
            fireballCheckTime = Time.time + FIREBALL_CHECK_DELAY;
            fireballPending = true;
        }

        // === Update ===

        void Update()
        {
            try
            {
                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }
                if (playerTransform == null) return;

                UpdateCharge();
                UpdateSpikes();
                UpdateFireball();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DodgeAssist] Update error: {e.Message}");
            }
        }

        private void UpdateCharge()
        {
            if (chargingBoss == null) return;

            if (Time.time > chargeEndTime)
            {
                chargingBoss = null;
                return;
            }

            // Boss may die/despawn mid-charge
            Vector3 bossPos;
            try { bossPos = chargingBoss.position; }
            catch { chargingBoss = null; return; }

            // While the telegraph is still aiming (not locked), keep updating the
            // predicted line from the boss's current position toward the player.
            if (!chargeLocked)
            {
                chargeOrigin = bossPos;
                var dir = playerTransform.position - bossPos;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.01f) chargeDir = dir.normalized;
            }

            if (chargeDir.sqrMagnitude < 0.01f) return;

            // Perpendicular distance of the player from the charge line (only ahead
            // of the boss — behind it is safe).
            Vector3 toPlayer = playerTransform.position - chargeOrigin;
            toPlayer.y = 0;
            float along = Vector3.Dot(toPlayer, chargeDir);
            bool inPath = false;
            float side = 0f;

            if (along > -1f)
            {
                Vector3 closest = chargeDir * Mathf.Max(along, 0f);
                Vector3 offset = toPlayer - closest;
                float perpDist = offset.magnitude;
                inPath = perpDist < chargeHalfWidth;
                // Which side of the line the player is on (sign of cross product Y)
                side = chargeDir.x * toPlayer.z - chargeDir.z * toPlayer.x;
            }

            if (inPath)
            {
                if (Time.time >= nextChargeWarnTime)
                {
                    nextChargeWarnTime = Time.time + CHARGE_WARN_INTERVAL;
                    wasInChargePath = true;

                    // Escape direction: perpendicular to the charge line, on the side
                    // the player is already closer to (shortest way out).
                    Vector3 escape = new Vector3(-chargeDir.z, 0, chargeDir.x); // left of dir
                    if (side < 0) escape = -escape;                             // right of dir
                    ScreenReader.Interrupt(ModLocalization.Get("dodge_charge_path", GetScreenDirection(escape)));
                }
            }
            else if (wasInChargePath)
            {
                wasInChargePath = false;
                ScreenReader.Say(ModLocalization.Get("dodge_safe"));
            }
        }

        private void UpdateSpikes()
        {
            if (activeSpikes.Count == 0) return;

            // Prune expired
            for (int i = activeSpikes.Count - 1; i >= 0; i--)
            {
                if (Time.time > activeSpikes[i].ExpireTime)
                    activeSpikes.RemoveAt(i);
            }
            if (activeSpikes.Count == 0 || Time.time < nextSpikeWarnTime) return;

            Vector3 playerPos = playerTransform.position;
            int worst = -1;
            float worstOverlap = 0f;

            for (int i = 0; i < activeSpikes.Count; i++)
            {
                var s = activeSpikes[i];
                Vector3 d = playerPos - s.Pos;
                d.y = 0;
                float overlap = (s.Radius + DODGE_MARGIN) - d.magnitude;
                if (overlap > worstOverlap)
                {
                    worstOverlap = overlap;
                    worst = i;
                }
            }

            if (worst >= 0)
            {
                nextSpikeWarnTime = Time.time + SPIKE_WARN_INTERVAL;

                // Escape direction: straight away from the spike center
                Vector3 escape = playerPos - activeSpikes[worst].Pos;
                escape.y = 0;
                if (escape.sqrMagnitude < 0.01f)
                    escape = Vector3.right; // dead center: any direction works
                ScreenReader.Interrupt(ModLocalization.Get("dodge_spike_under", GetScreenDirection(escape)));
            }
        }

        private void UpdateFireball()
        {
            if (!fireballPending || Time.time < fireballCheckTime) return;
            fireballPending = false;

            Vector3 moved = playerTransform.position - fireballPlayerPos;
            moved.y = 0;
            if (moved.magnitude < FIREBALL_SAFE_MOVE_DISTANCE)
                ScreenReader.Interrupt(ModLocalization.Get("dodge_move_now"));
        }

        // === Helpers ===

        private float MeasureBossHalfWidth(Transform boss)
        {
            try
            {
                var collider = boss.GetComponentInChildren<Collider>();
                if (collider != null)
                {
                    var ext = collider.bounds.extents;
                    float halfWidth = Mathf.Max(ext.x, ext.z);
                    if (halfWidth > 0.1f) return halfWidth;
                }
            }
            catch { }
            return FALLBACK_CHARGE_HALF_WIDTH;
        }

        /// <summary>World direction → screen-relative word (up/down/left/right + diagonals).</summary>
        private string GetScreenDirection(Vector3 worldDir)
        {
            worldDir.y = 0;
            worldDir.Normalize();

            Vector3 screenUp = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            screenUp.y = 0;
            screenUp.Normalize();
            Vector3 screenRight = new Vector3(screenUp.z, 0, -screenUp.x);

            float upDot = Vector3.Dot(worldDir, screenUp);
            float rightDot = Vector3.Dot(worldDir, screenRight);

            bool isUp = upDot > 0.38f;
            bool isDown = upDot < -0.38f;
            bool isRight = rightDot > 0.38f;
            bool isLeft = rightDot < -0.38f;

            if (isUp && isRight) return ModLocalization.Get("dir_up_right");
            if (isUp && isLeft) return ModLocalization.Get("dir_up_left");
            if (isDown && isRight) return ModLocalization.Get("dir_down_right");
            if (isDown && isLeft) return ModLocalization.Get("dir_down_left");
            if (isUp) return ModLocalization.Get("dir_up");
            if (isDown) return ModLocalization.Get("dir_down");
            if (isRight) return ModLocalization.Get("dir_right");
            return ModLocalization.Get("dir_left");
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
                else
                {
                    // Validate (player object is destroyed between runs)
                    try { var _ = playerTransform.position; }
                    catch { playerTransform = null; }
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
            Instance = null;
            Plugin.Log.LogInfo("[DodgeAssist] Destroyed");
        }
    }
}
