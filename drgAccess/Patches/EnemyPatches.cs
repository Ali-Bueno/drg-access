using HarmonyLib;
using drgAccess.Components;

namespace drgAccess.Patches
{
    /// <summary>
    /// Patches for Enemy class to register/unregister enemies for audio system.
    /// </summary>
    [HarmonyPatch]
    public class EnemyPatches
    {
        /// <summary>
        /// Register enemy when it spawns/activates.
        /// </summary>
        [HarmonyPatch(typeof(Enemy), "OnEnable")]
        [HarmonyPostfix]
        public static void OnEnable_Postfix(Enemy __instance)
        {
            try
            {
                var tracker = EnemyTracker.Instance;
                if (tracker != null)
                {
                    tracker.RegisterEnemy(__instance);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogDebug($"[EnemyPatch] OnEnable error: {e.Message}");
            }
        }

        /// <summary>
        /// Unregister enemy when it deactivates.
        /// </summary>
        [HarmonyPatch(typeof(Enemy), "OnDisable")]
        [HarmonyPostfix]
        public static void OnDisable_Postfix(Enemy __instance)
        {
            try
            {
                var tracker = EnemyTracker.Instance;
                if (tracker != null)
                {
                    tracker.UnregisterEnemy(__instance);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogDebug($"[EnemyPatch] OnDisable error: {e.Message}");
            }
        }

        /// <summary>
        /// Unregister enemy when it dies (alternative to OnDisable).
        /// Try to patch Die() or OnDeath() method if it exists.
        /// </summary>
        [HarmonyPatch(typeof(Enemy), "Die")]
        [HarmonyPostfix]
        public static void Die_Postfix(Enemy __instance)
        {
            try
            {
                var tracker = EnemyTracker.Instance;
                if (tracker != null)
                {
                    tracker.UnregisterEnemy(__instance);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogDebug($"[EnemyPatch] Die error: {e.Message}");
            }
        }
    }
}
