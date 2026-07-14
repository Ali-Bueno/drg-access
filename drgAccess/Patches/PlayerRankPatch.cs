using System;
using System.Text;
using HarmonyLib;
using DRS.UI;
using Assets.Scripts.RankSystem;
using drgAccess.Helpers;

namespace drgAccess.Patches;

/// <summary>
/// Player rank was only reachable on the save-slot screen at launch. PlayerRankManager
/// is Zenject-injected (not a MonoBehaviour), so it cannot be found with
/// FindObjectOfType — instead we grab the reference the game itself injects into the
/// rank widget, which every menu that shows the rank receives.
/// </summary>
[HarmonyPatch(typeof(UIPlayerRankWidget), nameof(UIPlayerRankWidget.Inject))]
public static class PlayerRankWidgetPatch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerRankManager playerRankManager)
    {
        try
        {
            if (playerRankManager != null)
                PlayerRankReader.CachedManager = playerRankManager;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug($"PlayerRankWidgetPatch error: {ex.Message}");
        }
    }
}

public static class PlayerRankReader
{
    internal static PlayerRankManager CachedManager;

    public static bool IsAvailable
    {
        get
        {
            try { return CachedManager != null && CachedManager.DataLoaded; }
            catch { return false; }
        }
    }

    /// <summary>
    /// "Player rank 7. 2 of 3 class levels to the next rank."
    /// Rank progress is measured in class levels: the game fills one pip per class
    /// level and needs PlayerRankSettings.ClassRanksPerPlayerRank of them per rank.
    /// </summary>
    public static string GetRankText()
    {
        try
        {
            var manager = CachedManager;
            if (manager == null || !manager.DataLoaded) return null;

            var sb = new StringBuilder(ModLocalization.Get("player_rank", manager.PlayerRank));

            try
            {
                var settings = manager.settingsPlayer;
                if (settings != null && settings.ClassRanksPerPlayerRank > 0)
                {
                    int filled = manager.GetPlayerRankSegments();
                    sb.Append(". ");
                    sb.Append(ModLocalization.Get("player_rank_progress",
                        filled, settings.ClassRanksPerPlayerRank));
                }
            }
            catch { }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug($"PlayerRankReader error: {ex.Message}");
            return null;
        }
    }

    public static void ReadRank()
    {
        string text = GetRankText();
        ScreenReader.Interrupt(text ?? ModLocalization.Get("player_rank_unavailable"));
    }
}
