using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace drgAccess.Components
{
    /// <summary>
    /// Tracks active enemies in the scene.
    /// Enemies are registered via patches and removed when destroyed.
    /// </summary>
    public class EnemyTracker : MonoBehaviour
    {
        public static EnemyTracker Instance { get; private set; }

        private HashSet<Enemy> activeEnemies = new HashSet<Enemy>();
        private readonly object lockObj = new object();

        static EnemyTracker()
        {
            ClassInjector.RegisterTypeInIl2Cpp<EnemyTracker>();
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
            activeEnemies = new HashSet<Enemy>();
            Plugin.Log.LogInfo("[EnemyTracker] Initialized");
        }

        void OnEnable()
        {
            // Clear enemies when re-enabled (scene change/retry)
            if (activeEnemies != null)
            {
                lock (lockObj)
                {
                    int count = activeEnemies.Count;
                    activeEnemies.Clear();
                    if (count > 0)
                        Plugin.Log.LogInfo($"[EnemyTracker] Cleared {count} enemies on re-enable");
                }
            }
        }

        public void RegisterEnemy(Enemy enemy)
        {
            if (enemy == null) return;

            lock (lockObj)
            {
                if (activeEnemies.Add(enemy))
                {
                    Plugin.Log.LogDebug($"[EnemyTracker] Registered enemy: {enemy.name}");
                }
            }
        }

        public void UnregisterEnemy(Enemy enemy)
        {
            if (enemy == null) return;

            lock (lockObj)
            {
                if (activeEnemies.Remove(enemy))
                {
                    Plugin.Log.LogDebug($"[EnemyTracker] Unregistered enemy: {enemy.name}");
                }
            }
        }

        public IEnumerable<Enemy> GetActiveEnemies()
        {
            lock (lockObj)
            {
                // Remove null/destroyed enemies
                activeEnemies.RemoveWhere(e =>
                {
                    try
                    {
                        if (e == null) return true;
                        var _ = e.transform; // Test if destroyed
                        return false;
                    }
                    catch
                    {
                        return true;
                    }
                });

                return new List<Enemy>(activeEnemies);
            }
        }

        public int GetEnemyCount()
        {
            lock (lockObj)
            {
                return activeEnemies.Count;
            }
        }

        void OnDestroy()
        {
            activeEnemies?.Clear();
            Instance = null;
            Plugin.Log.LogInfo("[EnemyTracker] Destroyed");
        }
    }
}
