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
            if (cachedGameController == null) return;

            var player = cachedGameController.player;
            if (player == null)
            {
                ScreenReader.Interrupt("No player found");
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
                        maxHp = maxHpStat.Value;
                }
            }
            catch { }

            if (maxHp > 0)
            {
                int current = Mathf.RoundToInt(fraction * maxHp);
                int max = Mathf.RoundToInt(maxHp);
                ScreenReader.Interrupt($"HP: {current} / {max}");
            }
            else
            {
                int percent = Mathf.RoundToInt(fraction * 100f);
                ScreenReader.Interrupt($"HP: {percent}%");
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"[HPReader] ReadHP error: {e.Message}");
        }
    }

    private bool IsInActiveGameplay()
    {
        try
        {
            if (Time.timeScale <= 0.1f) return false;

            if (Time.time >= nextSearchTime)
            {
                nextSearchTime = Time.time + 2f;

                if (gameStateProvider != null)
                {
                    var gc = gameStateProvider.TryCast<GameController>();
                    if (gc == null) gameStateProvider = null;
                }

                if (gameStateProvider == null)
                {
                    cachedGameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (cachedGameController != null)
                        gameStateProvider = cachedGameController.Cast<IGameStateProvider>();
                    else
                        return false;
                }
            }

            if (gameStateProvider != null)
            {
                var state = gameStateProvider.State;
                return state == GameController.EGameState.CORE ||
                       state == GameController.EGameState.CORE_OUTRO;
            }
        }
        catch { }
        return false;
    }

    void OnDestroy()
    {
        Instance = null;
    }
}
