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
        private HashSet<Enemy> activeCocoons = new HashSet<Enemy>();
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
            activeCocoons = new HashSet<Enemy>();
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
                    int cocoonCount = activeCocoons?.Count ?? 0;
                    activeEnemies.Clear();
                    activeCocoons?.Clear();
                    if (count > 0 || cocoonCount > 0)
                        Plugin.Log.LogInfo($"[EnemyTracker] Cleared {count} enemies and {cocoonCount} cocoons on re-enable");
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

        public void RegisterCocoon(Enemy enemy)
        {
            if (enemy == null) return;

            lock (lockObj)
            {
                if (activeCocoons.Add(enemy))
                {
                    Plugin.Log.LogDebug($"[EnemyTracker] Registered cocoon: {enemy.name}");
                }
            }
        }

        public void UnregisterCocoon(Enemy enemy)
        {
            if (enemy == null) return;

            lock (lockObj)
            {
                if (activeCocoons.Remove(enemy))
                {
                    Plugin.Log.LogDebug($"[EnemyTracker] Unregistered cocoon: {enemy.name}");
                }
            }
        }

        public IEnumerable<Enemy> GetActiveCocoons()
        {
            lock (lockObj)
            {
                activeCocoons.RemoveWhere(e =>
                {
                    try
                    {
                        if (e == null) return true;
                        var _ = e.transform;
                        return false;
                    }
                    catch
                    {
                        return true;
                    }
                });

                return new List<Enemy>(activeCocoons);
            }
        }

        public int GetCocoonCount()
        {
            lock (lockObj)
            {
                return activeCocoons.Count;
            }
        }

        void OnDestroy()
        {
            activeEnemies?.Clear();
            activeCocoons?.Clear();
            Instance = null;
            Plugin.Log.LogInfo("[EnemyTracker] Destroyed");
        }
    }
}
