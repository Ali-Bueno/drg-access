using System;
using System.Collections.Generic;
using System.Text;
using DRS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components;

/// <summary>
/// Arrow-key navigable reader for the pause menu.
/// Collects weapons (with full stats), artifacts, player stats, and action buttons.
/// Blocks EventSystem to prevent game UI conflicts (fixes first-level navigation bug).
/// Up/Down navigates, Enter activates, Escape resumes.
/// </summary>
public class PauseReaderComponent : MonoBehaviour
{
    public static PauseReaderComponent Instance { get; private set; }

    private bool isActive;
    private UICorePauseForm pauseForm;
    private List<PauseItem> items;
    private int selectedIndex;
    private int collectDelay = -1;

    // Suspended state (settings open, waiting to return)
    private bool suspended;
    private int suspendPollCounter;

    // EventSystem blocking
    private GameObject eventSystemObject;
    private int restoreStep = -1;

    private struct PauseItem
    {
        public string Text;
        public Action OnActivate;
        public bool SuspendOnActivate; // true = suspend reader instead of deactivate
    }

    static PauseReaderComponent()
    {
        ClassInjector.RegisterTypeInIl2Cpp<PauseReaderComponent>();
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

    public void Activate(UICorePauseForm form)
    {
        pauseForm = form;
        isActive = false;
        suspended = false;
        items = null;
        selectedIndex = 0;
        collectDelay = 10;
    }

    public void Deactivate()
    {
        if (!isActive && collectDelay < 0 && !suspended) return;
        isActive = false;
        suspended = false;
        collectDelay = -1;
        pauseForm = null;
        items = null;
        RestoreEventSystem();
    }

    /// <summary>
    /// Suspends the reader (restores EventSystem) but keeps state intact.
    /// Used when opening settings from pause — reader resumes when settings closes.
    /// </summary>
    private void Suspend()
    {
        if (!isActive) return;
        isActive = false;
        suspended = true;
        suspendPollCounter = 0;
        RestoreEventSystem();
    }

    void Update()
    {
        try
        {
            // Restoration state machine
            if (restoreStep > 0)
            {
                restoreStep--;
            }
            else if (restoreStep == 0)
            {
                restoreStep = -1;
                FinishRestore();
            }

            // Poll for settings return (check every ~30 frames / 0.5s)
            if (suspended && pauseForm != null)
            {
                suspendPollCounter++;
                if (suspendPollCounter % 30 == 0)
                {
                    try
                    {
                        var sf = UnityEngine.Object.FindObjectOfType<UISettingsForm>();
                        bool settingsOpen = sf != null && sf.gameObject.activeInHierarchy;
                        if (!settingsOpen)
                        {
                            suspended = false;
                            BlockEventSystem();
                            isActive = true;
                            ScreenReader.Interrupt($"Game Paused. {items[selectedIndex].Text}");
                        }
                    }
                    catch { suspended = false; }
                }
                return;
            }

            // Wait for UI to populate
            if (collectDelay > 0)
            {
                collectDelay--;
                return;
            }
            if (collectDelay == 0)
            {
                collectDelay = -1;
                CollectItems();
                return;
            }

            if (!isActive || items == null || items.Count == 0) return;

            // Escape/B to resume
            if (InputHelper.Cancel())
            {
                ResumeGame();
                return;
            }

            // Navigation
            if (InputHelper.NavigateUp())
                Navigate(-1);
            else if (InputHelper.NavigateDown())
                Navigate(1);
            else if (InputHelper.Confirm())
                ActivateCurrent();
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"PauseReader Update error: {e.Message}");
        }
    }

    private void Navigate(int direction)
    {
        selectedIndex += direction;
        if (selectedIndex < 0) selectedIndex = items.Count - 1;
        if (selectedIndex >= items.Count) selectedIndex = 0;

        ScreenReader.Interrupt(items[selectedIndex].Text);
    }

    private void ActivateCurrent()
    {
        var item = items[selectedIndex];
        if (item.OnActivate != null)
        {
            var action = item.OnActivate;
            if (item.SuspendOnActivate)
                Suspend();
            else
                Deactivate();
            action();
        }
    }

    private void ResumeGame()
    {
        var form = pauseForm;
        Deactivate();
        try
        {
            form?.SetVisibility(false, false);
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"PauseReader ResumeGame error: {e.Message}");
        }
    }

    // ===== Data collection =====

    private void CollectItems()
    {
        if (pauseForm == null) return;

        items = new List<PauseItem>();

        try
        {
            CollectWeapons();
            CollectArtifacts();
            CollectStats();
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"PauseReader CollectItems error: {e.Message}");
        }

        CollectButtons();

        if (items.Count == 0)
            items.Add(new PauseItem { Text = "No information available" });

        BlockEventSystem();

        isActive = true;
        selectedIndex = 0;

        ScreenReader.Interrupt(
            $"Game Paused. Up and down to navigate, Enter to select. {items[0].Text}");
    }

    private void CollectWeapons()
    {
        var weapons = pauseForm.weapons;
        if (weapons == null || weapons.Count == 0) return;

        var details = pauseForm.weaponDetails;

        for (int i = 0; i < weapons.Count; i++)
        {
            try
            {
                var uiWeapon = weapons[i];
                if (uiWeapon == null) continue;

                var handler = uiWeapon.weaponHandler;
                if (handler == null) continue;

                var sb = new StringBuilder();

                // Weapon name
                var data = handler.Data;
                if (data != null)
                {
                    string title = data.Title;
                    if (!string.IsNullOrEmpty(title))
                        sb.Append(TextHelper.CleanText(title));
                }

                // Level
                int level = handler.weaponLevel;
                if (level > 0)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"Level {level}");
                }

                // Populate weapon details panel to read stats
                if (details != null)
                {
                    try
                    {
                        details.Show(handler);

                        var statsText = details.statsText;
                        if (statsText != null && !string.IsNullOrEmpty(statsText.text))
                        {
                            if (sb.Length > 0) sb.Append(". ");
                            sb.Append(TextHelper.CleanText(statsText.text));
                        }

                        var tagText = details.tagText;
                        if (tagText != null && !string.IsNullOrEmpty(tagText.text))
                        {
                            if (sb.Length > 0) sb.Append(". ");
                            sb.Append(TextHelper.CleanText(tagText.text));
                        }

                        var upgradesText = details.upgradesText;
                        if (upgradesText != null && !string.IsNullOrEmpty(upgradesText.text))
                        {
                            if (sb.Length > 0) sb.Append(". ");
                            sb.Append(TextHelper.CleanText(upgradesText.text));
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Log?.LogDebug($"PauseReader weapon details error: {e.Message}");
                    }
                }

                if (sb.Length > 0)
                    AddItem(i == 0 ? $"Weapons. {sb}" : sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug($"PauseReader weapon {i} error: {e.Message}");
            }
        }
    }

    private void CollectArtifacts()
    {
        var artifacts = pauseForm.artifacts;
        if (artifacts == null || artifacts.Count == 0) return;

        bool first = true;

        for (int i = 0; i < artifacts.Count; i++)
        {
            try
            {
                var uiArtifact = artifacts[i];
                if (uiArtifact == null) continue;

                var data = uiArtifact.artifactData;
                if (data == null) continue;

                var sb = new StringBuilder();

                string title = data.Title;
                if (!string.IsNullOrEmpty(title))
                    sb.Append(TextHelper.CleanText(title));

                // Get description
                try
                {
                    string desc = data.GetDescription();
                    if (!string.IsNullOrEmpty(desc))
                    {
                        if (sb.Length > 0) sb.Append(". ");
                        sb.Append(TextHelper.CleanText(desc));
                    }
                }
                catch { }

                if (sb.Length > 0)
                {
                    string text = first ? $"Artifacts. {sb}" : sb.ToString();
                    first = false;
                    AddItem(text);
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug($"PauseReader artifact {i} error: {e.Message}");
            }
        }
    }

    private void CollectStats()
    {
        var uiStats = pauseForm.uiStats;
        if (uiStats == null || uiStats.Count == 0) return;

        bool first = true;

        for (int i = 0; i < uiStats.Count; i++)
        {
            try
            {
                var stat = uiStats[i];
                if (stat == null) continue;

                string name = null;
                string value = null;

                var nameText = stat.nameText;
                if (nameText != null)
                    name = TextHelper.CleanText(nameText.text);

                var valueText = stat.valueText;
                if (valueText != null)
                    value = TextHelper.CleanText(valueText.text);

                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(value))
                    continue;

                string text;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    text = $"{name}: {value}";
                else
                    text = name ?? value;

                if (first)
                {
                    text = $"Stats. {text}";
                    first = false;
                }

                AddItem(text);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug($"PauseReader stat {i} error: {e.Message}");
            }
        }
    }

    private void CollectButtons()
    {
        var form = pauseForm;

        AddItem("Resume", () =>
        {
            try { form?.SetVisibility(false, false); }
            catch { }
        });

        AddItem("Menu", () =>
        {
            try { form?.OnMenuButton(); }
            catch { }
        });

        items.Add(new PauseItem
        {
            Text = "Settings",
            OnActivate = () => { try { form?.OnSettingsButton(); } catch { } },
            SuspendOnActivate = true
        });
    }

    private void AddItem(string text, Action onActivate = null)
    {
        if (!string.IsNullOrEmpty(text))
            items.Add(new PauseItem { Text = text, OnActivate = onActivate });
    }

    // ===== EventSystem management =====

    private void BlockEventSystem()
    {
        try
        {
            var es = EventSystem.current;
            if (es != null)
            {
                eventSystemObject = es.gameObject;
                eventSystemObject.SetActive(false);
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"PauseReader BlockEventSystem error: {e.Message}");
        }
    }

    private void RestoreEventSystem()
    {
        try
        {
            if (eventSystemObject != null)
            {
                eventSystemObject.SetActive(true);
                eventSystemObject = null;
                restoreStep = 5;
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"PauseReader RestoreEventSystem error: {e.Message}");
            eventSystemObject = null;
        }
    }

    private void FinishRestore()
    {
        try
        {
            var es = EventSystem.current;
            if (es == null) return;

            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module != null)
            {
                module.enabled = false;
                module.enabled = true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"PauseReader FinishRestore error: {e.Message}");
        }
    }

    void OnDestroy()
    {
        if (isActive || collectDelay >= 0 || suspended)
            RestoreEventSystem();
        Instance = null;
    }
}
