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
        /// Register enemy when it spawns.
        /// Only register actual enemies, not cocoons or destructibles.
        /// </summary>
        [HarmonyPatch(typeof(Enemy), "OnSpawn")]
        [HarmonyPostfix]
        public static void OnSpawn_Postfix(Enemy __instance)
        {
            try
            {
                // Filter out cocoons and non-combat entities
                var enemyType = __instance.type;
                if (enemyType == EEnemyType.COCOON || enemyType == EEnemyType.BIG_COCOON)
                {
                    return; // Don't track cocoons/destructibles
                }

                var tracker = EnemyTracker.Instance;
                if (tracker != null)
                {
                    tracker.RegisterEnemy(__instance);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogDebug($"[EnemyPatch] OnSpawn error: {e.Message}");
            }
        }

        /// <summary>
        /// Unregister enemy when it despawns.
        /// </summary>
        [HarmonyPatch(typeof(Enemy), "DeSpawn")]
        [HarmonyPostfix]
        public static void DeSpawn_Postfix(Enemy __instance)
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
                Plugin.Log.LogDebug($"[EnemyPatch] DeSpawn error: {e.Message}");
            }
        }

        /// <summary>
        /// Unregister enemy when it dies.
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
