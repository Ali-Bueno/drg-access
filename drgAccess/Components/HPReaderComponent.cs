using System;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components;

/// <summary>
/// Listens for H key to announce player HP during active gameplay.
/// </summary>
public class HPReaderComponent : MonoBehaviour
{
    public static HPReaderComponent Instance { get; private set; }

    private IGameStateProvider gameStateProvider;
    private GameController cachedGameController;
    private float nextSearchTime = 0f;

    static HPReaderComponent()
    {
        ClassInjector.RegisterTypeInIl2Cpp<HPReaderComponent>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        try
        {
            if (!IsInActiveGameplay() && !Patches.WalletReader.ShopFormOpen) return;
            if (!InputHelper.ReadHP()) return;

            ReadHP();
        }
        catch { }
    }

    private void ReadHP()
    {
        try
        {
            var gameController = Helpers.GameStateHelper.CachedGameController;
            if (gameController == null) return;

            var player = gameController.player;
            if (player == null)
            {
                ScreenReader.Interrupt(ModLocalization.Get("hp_no_player"));
                return;
            }

            float fraction = player.CurrentHealthFraction;
            float maxHp = 0f;

            try
            {
                var stats = player.stats;
                if (stats != null)
                {
                    var maxHpStat = stats.GetStat(EStatType.MAX_HP);
                    if (maxHpStat != null)
                        maxHp = (float)maxHpStat.Value;
                }
            }
            catch { }

            if (maxHp > 0)
            {
                int current = Mathf.RoundToInt(fraction * maxHp);
                int max = Mathf.RoundToInt(maxHp);
                ScreenReader.Interrupt(ModLocalization.Get("hp_format", current, max));
            }
            else
            {
                int percent = Mathf.RoundToInt(fraction * 100f);
                ScreenReader.Interrupt(ModLocalization.Get("hp_format_percent", percent));
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"[HPReader] ReadHP error: {e.Message}");
        }
    }

    private bool IsInActiveGameplay()
    {
        return drgAccess.Helpers.GameStateHelper.IsInGameplayOrOutro();
    }

    void OnDestroy()
    {
        Instance = null;
    }
}
