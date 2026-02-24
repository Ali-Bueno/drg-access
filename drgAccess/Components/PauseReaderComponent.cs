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
using DRS;

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

    // Suspended state (overlay open — settings, abort popup, etc. — waiting to return)
    private bool suspended;
    private bool suspendedForSettings;
    private int suspendPollCounter;
    private int suspendEscapeDelay;

    // Resume delay (frames to wait before re-opening with existing items)
    private int resumeDelay = -1;

    // Frame tracking (to skip Cancel on the same frame as Activate)
    private int activateFrame = -1;

    // EventSystem blocking
    private GameObject eventSystemObject;
    private int restoreStep = -1;

    /// <summary>
    /// True when the pause reader is involved (open, collecting, or suspended).
    /// Used by UIButtonPatch to suppress stray focus announcements.
    /// </summary>
    public bool IsHandlingPause => isOpen || suspended || collectDelay >= 0 || resumeDelay >= 0;

    /// <summary>
    /// True when suspended specifically because user opened Settings from pause menu.
    /// Used by UIFormPatches to detect when settings closes and resume the reader.
    /// </summary>
    public bool IsSuspendedForSettings => suspended && suspendedForSettings;

    /// <summary>
    /// True when suspended because user opened Menu (abort popup) from pause menu.
    /// Used by AbortPopupHidePatch to detect when popup closes and resume the reader.
    /// </summary>
    public bool IsSuspendedForMenu => suspended && !suspendedForSettings;

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
    /// If returning from settings (suspended with existing items), resumes immediately.
    /// Otherwise starts a fresh collection delay.
    /// </summary>
    public void Activate(UICorePauseForm form)
    {
        // Returning from settings — resume immediately with existing items
        if ((suspended || resumeDelay >= 0) && items != null && items.Count > 0)
        {
            pauseForm = form;
            suspended = false;
            resumeDelay = -1;
            ResumeFromSuspend();
            return;
        }

        pauseForm = form;
        isOpen = false;
        suspended = false;
        resumeDelay = -1;
        items = null;
        selectedIndex = 0;
        collectDelay = 10;
        activateFrame = Time.frameCount;
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
        resumeDelay = -1;
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

            // Safety: if pause form disappeared or game unpaused, clean up
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

            // Suspended: waiting for user to return from overlay (settings, popup, etc.)
            if (suspended)
            {
                // Safety: if pause form destroyed, clean up
                if (pauseForm == null) { Close(); return; }
                try { var _ = pauseForm.gameObject; }
                catch { Close(); return; }

                suspendPollCounter++;

                // Let overlay open before processing input
                if (suspendPollCounter <= 20) return;

                // Settings: resume is handled by UIFormPatches when UISettingsForm closes,
                // or by Show() patch if it fires. Don't listen for input here.
                if (suspendedForSettings) return;

                // Non-Settings overlays (popups): resume on user input.

                // Escape-based resume: user pressed Escape to close popup,
                // wait a few frames for the game to process it, then resume
                if (suspendEscapeDelay > 0)
                {
                    suspendEscapeDelay--;
                    if (suspendEscapeDelay == 0)
                    {
                        ResumeFromSuspend();
                        return;
                    }
                }

                if (InputHelper.Cancel())
                    suspendEscapeDelay = 8;

                // Navigation-based resume: Up/Down means popup is gone
                // and user wants the reader back
                if (InputHelper.NavigateUp() || InputHelper.NavigateDown())
                {
                    ResumeFromSuspend();
                    return;
                }

                return;
            }

            // Resume delay: re-open with existing items after returning from settings
            if (resumeDelay > 0)
            {
                resumeDelay--;
                return;
            }
            if (resumeDelay == 0)
            {
                resumeDelay = -1;
                if (items != null && items.Count > 0)
                {
                    ResumeFromSuspend();
                }
                return;
            }

            // Handle Cancel during collectDelay (quick Escape before reader opens).
            // Skip the activation frame — the same Escape that paused the game
            // would immediately abort the reader via wasPressedThisFrame.
            if (collectDelay >= 0 && Time.frameCount > activateFrame && InputHelper.Cancel())
            {
                var form = pauseForm;
                Close();
                ResumeGame(form);
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
        ResumeGame(form);
    }

    /// <summary>
    /// Properly resumes the game by calling OnBack (which hides the form AND calls UnpauseCore).
    /// SetVisibility alone only hides the UI without unpausing.
    /// </summary>
    private void ResumeGame(UICorePauseForm form)
    {
        try
        {
            if (form != null)
                form.OnBack(new UIManager.BackEvent());
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"PauseReader ResumeGame OnBack failed: {e.Message}");
            // Fallback: hide form and unpause manually
            try { form?.SetVisibility(false, false); } catch { }
            try
            {
                var gc = UnityEngine.Object.FindObjectOfType<GameController>();
                gc?.UnpauseCore();
            }
            catch { }
        }
    }

    private void DoMenu()
    {
        SuspendForOverlay(forSettings: false);
        try { pauseForm?.OnMenuButton(); }
        catch { }
    }

    private void DoSettings()
    {
        SuspendForOverlay(forSettings: true);
        try { pauseForm?.OnSettingsButton(); }
        catch { }
    }

    /// <summary>
    /// Suspends the reader while an overlay (settings, abort popup, etc.) is active.
    /// Keeps items and selected index intact so we can resume when the overlay closes.
    /// </summary>
    private void SuspendForOverlay(bool forSettings)
    {
        isOpen = false;
        suspended = true;
        suspendedForSettings = forSettings;
        suspendPollCounter = 0;
        suspendEscapeDelay = 0;
        RestoreEventSystem();
        // Allow overlay to use screen reader (settings patches, popup announcements).
        // IsHandlingPause still suppresses pause-form-specific button announcements.
        ScreenReader.Suppressed = false;
    }

    private void ResumeFromSuspend()
    {
        suspended = false;
        BlockEventSystem();
        isOpen = true;
        ScreenReader.Suppressed = true;
        SpeakDirect($"Game Paused. {items[selectedIndex].Text}");
    }

    /// <summary>
    /// Called from UIFormPatches when UISettingsForm closes (SetVisibility false).
    /// Resumes the reader with existing items after a short delay.
    /// </summary>
    public void ResumeFromSettingsClose()
    {
        if (!suspended || !suspendedForSettings) return;
        // Use resumeDelay to let the pause form re-appear before we block EventSystem
        suspendedForSettings = false;
        suspended = false;
        resumeDelay = 5;
        ScreenReader.Suppressed = true;
    }

    /// <summary>
    /// Called from AbortPopupHidePatch when the abort popup closes (Continue/Escape).
    /// Resumes the reader with existing items after a short delay.
    /// </summary>
    public void ResumeFromOverlayClose()
    {
        if (!suspended) return;
        suspended = false;
        resumeDelay = 5;
        ScreenReader.Suppressed = true;
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
