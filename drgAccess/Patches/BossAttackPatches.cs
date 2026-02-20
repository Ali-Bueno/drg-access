using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DRS.UI;
using drgAccess.Components;

namespace drgAccess.Patches
{
    /// <summary>
    /// Patches for boss attack telegraphs and HP tracking.
    /// DreadnoughtAnimator telegraph methods are declared directly on the class (safe to patch).
    /// UIBossTopBar methods are declared directly on the class (safe to patch).
    /// </summary>
    [HarmonyPatch]
    public class BossAttackPatches
    {
        // HP threshold tracking
        private static readonly int[] HpThresholds = { 75, 50, 25, 15, 10, 5 };
        private static int lastAnnouncedThreshold = 100;
        private static float lastHealAnnounceTime;
        private const float HEAL_ANNOUNCE_COOLDOWN = 3f;

        // --- Dreadnought Attack Telegraph Patches ---

        [HarmonyPatch(typeof(DreadnoughtAnimator), "BeginTelegraphingCharge")]
        [HarmonyPostfix]
        public static void Telegraph_Charge_Postfix(DreadnoughtAnimator __instance)
        {
            try
            {
                ScreenReader.Interrupt("Charge!");
                var pos = GetBossPosition(__instance);
                BossAttackAudio.PlayAttackSound(BossAttackType.Charge, pos);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[BossAttack] Charge telegraph error: {e.Message}");
            }
        }

        [HarmonyPatch(typeof(DreadnoughtAnimator), "BeginTelegraphingSpikes")]
        [HarmonyPostfix]
        public static void Telegraph_Spikes_Postfix(DreadnoughtAnimator __instance)
        {
            try
            {
                ScreenReader.Interrupt("Spikes!");
                var pos = GetBossPosition(__instance);
                BossAttackAudio.PlayAttackSound(BossAttackType.Spikes, pos);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[BossAttack] Spikes telegraph error: {e.Message}");
            }
        }

        // Note: the game code has a typo "Being" instead of "Begin"
        [HarmonyPatch(typeof(DreadnoughtAnimator), "BeingTelegraphingBurp")]
        [HarmonyPostfix]
        public static void Telegraph_Fireball_Postfix(DreadnoughtAnimator __instance)
        {
            try
            {
                ScreenReader.Interrupt("Fireball!");
                var pos = GetBossPosition(__instance);
                BossAttackAudio.PlayAttackSound(BossAttackType.Fireball, pos);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[BossAttack] Fireball telegraph error: {e.Message}");
            }
        }

        [HarmonyPatch(typeof(DreadnoughtAnimator), "TelegraphHealCast")]
        [HarmonyPostfix]
        public static void Telegraph_Heal_Postfix(DreadnoughtAnimator __instance, bool state)
        {
            try
            {
                if (state)
                {
                    ScreenReader.Interrupt("Healing!");
                    var pos = GetBossPosition(__instance);
                    BossAttackAudio.PlayAttackSound(BossAttackType.Heal, pos);
                }
                else
                {
                    BossAttackAudio.StopAll();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[BossAttack] Heal telegraph error: {e.Message}");
            }
        }

        private static Vector3 GetBossPosition(DreadnoughtAnimator animator)
        {
            try
            {
                var component = animator.TryCast<Component>();
                if (component != null)
                    return component.transform.position;
            }
            catch { }
            return Vector3.zero;
        }

        // --- Boss HP Tracking Patches ---

        /// <summary>
        /// Disambiguate UIBossTopBar.Show overloads — target Show(Enemy, int).
        /// </summary>
        [HarmonyPatch]
        public class BossShowPatch
        {
            static MethodBase TargetMethod()
            {
                return typeof(UIBossTopBar).GetMethods()
                    .First(m => m.Name == "Show" &&
                                m.GetParameters().Length == 2 &&
                                m.GetParameters()[0].ParameterType == typeof(Enemy));
            }

            [HarmonyPostfix]
            public static void Postfix(UIBossTopBar __instance)
            {
                try
                {
                    lastAnnouncedThreshold = 100;
                    ScreenReader.Interrupt("Boss!");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogDebug($"[BossAttack] Show error: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(UIBossTopBar), "UpdateFill")]
        [HarmonyPostfix]
        public static void UpdateFill_Postfix(UIBossTopBar __instance, float normLife)
        {
            try
            {
                int hpPercent = Mathf.RoundToInt(normLife * 100f);

                foreach (int threshold in HpThresholds)
                {
                    if (hpPercent <= threshold && lastAnnouncedThreshold > threshold)
                    {
                        lastAnnouncedThreshold = threshold;
                        ScreenReader.Interrupt($"Boss {threshold}%");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[BossAttack] UpdateFill error: {e.Message}");
            }
        }

        [HarmonyPatch(typeof(UIBossTopBar), "OnOwnerDeath")]
        [HarmonyPostfix]
        public static void OnOwnerDeath_Postfix()
        {
            try
            {
                BossAttackAudio.StopAll();
                ScreenReader.Interrupt("Boss defeated!");
                lastAnnouncedThreshold = 100;
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[BossAttack] OnOwnerDeath error: {e.Message}");
            }
        }

        [HarmonyPatch(typeof(UIBossTopBar), "OnHealed")]
        [HarmonyPostfix]
        public static void OnHealed_Postfix()
        {
            try
            {
                if (Time.time - lastHealAnnounceTime < HEAL_ANNOUNCE_COOLDOWN) return;
                lastHealAnnounceTime = Time.time;
                ScreenReader.Say("Boss healed");
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[BossAttack] OnHealed error: {e.Message}");
            }
        }
    }
}
