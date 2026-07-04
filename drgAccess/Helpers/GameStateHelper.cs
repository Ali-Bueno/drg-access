using UnityEngine;

namespace drgAccess.Helpers
{
    /// <summary>
    /// Single source of truth for "are we in active gameplay" across all components.
    ///
    /// History of the retry bug: on Retry the scene name does not change and the old
    /// GameController is Unity-destroyed but its il2cpp object stays alive (our own
    /// reference roots it), so BOTH earlier validation attempts fail silently:
    /// TryCast succeeds, and reading .State does NOT throw — it returns stale data.
    /// The only reliable approach is to re-find the GameController periodically and
    /// compare native pointers: a new run means a new instance.
    /// </summary>
    public static class GameStateHelper
    {
        private static GameController cached;
        private static IGameStateProvider provider;
        private static float nextSearchTime;
        private const float SEARCH_INTERVAL = 2f;

        /// <summary>
        /// Increments every time a new GameController instance is detected
        /// (new run, retry, or next escort stage). Components compare this to
        /// reset their per-run state.
        /// </summary>
        public static int RunGeneration { get; private set; }

        /// <summary>Current run's GameController (may be null outside a run).</summary>
        public static GameController CachedGameController => cached;

        public static bool IsInActiveGameplay()
        {
            var state = CurrentState();
            return state == GameController.EGameState.CORE;
        }

        /// <summary>Like IsInActiveGameplay but also true during the extraction outro.</summary>
        public static bool IsInGameplayOrOutro()
        {
            var state = CurrentState();
            return state == GameController.EGameState.CORE ||
                   state == GameController.EGameState.CORE_OUTRO;
        }

        private static GameController.EGameState? CurrentState()
        {
            if (Time.timeScale <= 0.1f) return null;

            if (Time.time >= nextSearchTime)
            {
                nextSearchTime = Time.time + SEARCH_INTERVAL;
                Refresh();
            }

            if (provider == null) return null;

            try
            {
                return provider.State;
            }
            catch
            {
                provider = null;
                cached = null;
                return null;
            }
        }

        private static void Refresh()
        {
            try
            {
                var gc = Object.FindObjectOfType<GameController>();
                if (gc == null)
                {
                    if (cached != null)
                        Plugin.Log.LogInfo("[GameState] GameController gone (run ended)");
                    cached = null;
                    provider = null;
                    return;
                }

                if (cached == null || gc.Pointer != cached.Pointer)
                {
                    cached = gc;
                    provider = gc.Cast<IGameStateProvider>();
                    RunGeneration++;
                    Plugin.Log.LogInfo($"[GameState] GameController acquired (run generation {RunGeneration})");
                }
            }
            catch
            {
                cached = null;
                provider = null;
            }
        }
    }
}
