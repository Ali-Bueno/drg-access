using System;
using System.Collections.Generic;
using System.Text;
using DavyKager;
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
/// Follows the same pattern as ModSettingsMenu: blocks EventSystem, suppresses
/// ScreenReader, handles ALL input (including Escape), uses SpeakDirect for output.
/// Fully self-contained lifecycle — no reliance on external patches for state management.
/// </summary>
public class PauseReaderComponent : MonoBehaviour
{
    public static PauseReaderComponent Instance { get; private set; }

    private bool isOpen;
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

    /// <summary>
    /// True when the pause reader is involved (open, collecting, or suspended).
    /// Used by UIButtonPatch to suppress stray focus announcements.
    /// </summary>
    public bool IsHandlingPause => isOpen || suspended || collectDelay >= 0;

    private struct PauseItem
    {
        public string Text;
        public Action OnActivate;
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

    /// <summary>
    /// Called from PauseFormShowPatch when pause form opens.
    /// Starts the collection delay, then opens the reader.
    /// </summary>
    public void Activate(UICorePauseForm form)
    {
        pauseForm = form;
        isOpen = false;
        suspended = false;
        items = null;
        selectedIndex = 0;
        collectDelay = 10;
        ScreenReader.Suppressed = true;
    }

    /// <summary>
    /// Closes the reader and restores everything.
    /// Safe to call multiple times.
    /// </summary>
    private void Close()
    {
        if (!isOpen && collectDelay < 0 && !suspended) return;
        isOpen = false;
        suspended = false;
        collectDelay = -1;
        pauseForm = null;
        items = null;
        RestoreEventSystem();
        ScreenReader.Suppressed = false;
    }

    void Update()
    {
        try
        {
            // Restoration state machine (for after Close)
            if (restoreStep > 0)
            {
                restoreStep--;
            }
            else if (restoreStep == 0)
            {
                restoreStep = -1;
                FinishRestore();
            }

            // Safety: if pause form disappeared, clean up
            if ((isOpen || collectDelay >= 0) && pauseForm != null)
            {
                try
                {
                    if (!pauseForm.gameObject.activeInHierarchy)
                    {
                        Close();
                        return;
                    }
                }
                catch { Close(); return; }
            }

            // Suspended: poll for settings return
            if (suspended)
            {
                if (pauseForm == null)
                {
                    Close();
                    return;
                }

                // Check if pause form disappeared while suspended
                try
                {
                    if (!pauseForm.gameObject.activeInHierarchy)
                    {
                        Close();
                        return;
                    }
                }
                catch { Close(); return; }

                suspendPollCounter++;
                if (suspendPollCounter % 10 == 0)
                {
                    try
                    {
                        var sf = UnityEngine.Object.FindObjectOfType<UISettingsForm>();
                        bool settingsOpen = sf != null && sf.gameObject.activeInHierarchy;
                        if (!settingsOpen)
                            ResumeFromSettings();
                    }
                    catch { Close(); }
                }
                return;
            }

            // Collection delay
            if (collectDelay > 0)
            {
                collectDelay--;
                return;
            }
            if (collectDelay == 0)
            {
                collectDelay = -1;
                CollectAndOpen();
                return;
            }

            if (!isOpen || items == null || items.Count == 0) return;

            // Handle ALL input (self-contained, like ModSettingsMenu)
            if (InputHelper.Cancel())
            {
                DoResume();
                return;
            }

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

        SpeakDirect(items[selectedIndex].Text);
    }

    private void ActivateCurrent()
    {
        var item = items[selectedIndex];
        if (item.OnActivate != null)
            item.OnActivate();
    }

    // ===== Actions =====

    private void DoResume()
    {
        var form = pauseForm;
        Close();
        try { form?.SetVisibility(false, false); }
        catch { }
    }

    private void DoMenu()
    {
        var form = pauseForm;
        Close();
        try { form?.OnMenuButton(); }
        catch { }
    }

    private void DoSettings()
    {
        var form = pauseForm;

        // Suspend: keep state, restore EventSystem so settings can work
        isOpen = false;
        suspended = true;
        suspendPollCounter = 0;
        RestoreEventSystem();
        // Keep ScreenReader.Suppressed = true to avoid stray announcements

        try { form?.OnSettingsButton(); }
        catch { }
    }

    private void ResumeFromSettings()
    {
        suspended = false;
        BlockEventSystem();
        isOpen = true;
        SpeakDirect($"Game Paused. {items[selectedIndex].Text}");
    }

    // ===== Data collection =====

    private void CollectAndOpen()
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
        isOpen = true;
        selectedIndex = 0;

        SpeakDirect(
            $"Game Paused. Up and down to navigate, Enter to select, Escape to resume. {items[0].Text}");
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

                var data = handler.Data;
                if (data != null)
                {
                    string title = data.Title;
                    if (!string.IsNullOrEmpty(title))
                        sb.Append(TextHelper.CleanText(title));
                }

                int level = handler.weaponLevel;
                if (level > 0)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"Level {level}");
                }

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
        AddItem("Resume", DoResume);
        AddItem("Menu", DoMenu);
        AddItem("Settings", DoSettings);
    }

    private void AddItem(string text, Action onActivate = null)
    {
        if (!string.IsNullOrEmpty(text))
            items.Add(new PauseItem { Text = text, OnActivate = onActivate });
    }

    // ===== Speech =====

    /// <summary>
    /// Speaks directly via Tolk, bypassing ScreenReader.Suppressed.
    /// Same pattern as ModSettingsMenu.SpeakDirect.
    /// </summary>
    private void SpeakDirect(string text)
    {
        try { Tolk.Speak(text, true); }
        catch { }
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
        if (isOpen || collectDelay >= 0 || suspended)
        {
            RestoreEventSystem();
            ScreenReader.Suppressed = false;
        }
        Instance = null;
    }
}
