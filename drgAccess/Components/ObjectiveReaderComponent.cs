using System;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;
using DRS.UI;

namespace drgAccess.Components;

/// <summary>
/// Reads active objectives on O key press during gameplay.
/// Finds UIObjectiveTracker in scene and reads all visible UIObjective items.
/// </summary>
public class ObjectiveReaderComponent : MonoBehaviour
{
    public static ObjectiveReaderComponent Instance { get; private set; }

    private IGameStateProvider gameStateProvider;
    private float nextSearchTime = 0f;

    static ObjectiveReaderComponent()
    {
        ClassInjector.RegisterTypeInIl2Cpp<ObjectiveReaderComponent>();
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
            if (!IsInActiveGameplay()) return;
            if (!InputHelper.ReadObjectives()) return;

            ReadObjectives();
        }
        catch { }
    }

    private void ReadObjectives()
    {
        try
        {
            var tracker = UnityEngine.Object.FindObjectOfType<UIObjectiveTracker>();
            if (tracker == null)
            {
                ScreenReader.Interrupt(ModLocalization.Get("obj_none"));
                return;
            }

            var objectives = tracker.uiObjectives;
            if (objectives == null || objectives.Length == 0)
            {
                ScreenReader.Interrupt(ModLocalization.Get("obj_none"));
                return;
            }

            // Collect all visible objectives
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < objectives.Length; i++)
            {
                var obj = objectives[i];
                if (obj == null) continue;
                if (!obj.gameObject.activeSelf) continue;

                var desc = obj.description;
                var prog = obj.progress;

                if (desc == null || string.IsNullOrEmpty(desc.text)) continue;

                string text = TextHelper.CleanText(desc.text);
                if (prog != null && !string.IsNullOrEmpty(prog.text))
                    text += ": " + TextHelper.CleanText(prog.text);

                parts.Add(text);
            }

            if (parts.Count == 0)
            {
                ScreenReader.Interrupt(ModLocalization.Get("obj_no_active"));
                return;
            }

            string result = parts.Count == 1
                ? ModLocalization.Get("obj_single", parts[0])
                : ModLocalization.Get("obj_multiple", string.Join(". ", parts));

            ScreenReader.Interrupt(result);
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"[ObjectiveReader] ReadObjectives error: {e.Message}");
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
                    var gameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (gameController != null)
                        gameStateProvider = gameController.Cast<IGameStateProvider>();
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
