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
