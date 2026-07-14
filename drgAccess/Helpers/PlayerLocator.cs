using UnityEngine;

namespace drgAccess.Helpers
{
    /// <summary>
    /// Shared player lookup for all positional audio components.
    /// Primary: the game's own Player component — robust across game updates
    /// (the Unity 6 update broke the old GameObject-name search, which killed
    /// every positional audio cue while screen reader output kept working).
    /// Fallback: the well-known GameObject names from before.
    /// Callers cache the returned Transform and re-ask when it dies.
    /// </summary>
    public static class PlayerLocator
    {
        private static Player cachedPlayer;
        private static float nextPlayerSearchTime;
        private static int cachedRunGeneration = -1;

        /// <summary>
        /// The Player component itself, cached — callers that need more than a position
        /// (facing, movement behaviour) go through here instead of searching every frame.
        /// The cache is dropped whenever a new run starts: a retry replaces the player
        /// without changing the scene, and the dead one keeps answering.
        /// </summary>
        public static Player FindPlayer()
        {
            try
            {
                if (cachedRunGeneration != GameStateHelper.RunGeneration)
                {
                    cachedRunGeneration = GameStateHelper.RunGeneration;
                    cachedPlayer = null;
                    nextPlayerSearchTime = 0f;
                }

                if (cachedPlayer != null) return cachedPlayer;

                if (Time.time < nextPlayerSearchTime) return null;
                nextPlayerSearchTime = Time.time + 1f;

                cachedPlayer = Object.FindObjectOfType<Player>();
                return cachedPlayer;
            }
            catch
            {
                cachedPlayer = null;
                return null;
            }
        }

        public static Transform FindPlayerTransform()
        {
            try
            {
                var player = Object.FindObjectOfType<Player>();
                if (player != null) return player.transform;
            }
            catch { }

            try
            {
                string[] playerNames = { "Player", "PlayerCharacter", "Hero", "Character" };
                foreach (var name in playerNames)
                {
                    var obj = GameObject.Find(name);
                    if (obj != null && !obj.name.Contains("Camera"))
                        return obj.transform;
                }
            }
            catch { }

            return null;
        }
    }
}
