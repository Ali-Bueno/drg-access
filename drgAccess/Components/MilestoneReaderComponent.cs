using System;
using System.Collections.Generic;
using System.Text;
using DRS.UI;
using TMPro;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components;

/// <summary>
/// W/S navigable reader for milestone form.
/// Collects visible milestones and lets user browse with W/S keys.
/// Does NOT block EventSystem so tab navigation still works.
/// </summary>
public class MilestoneReaderComponent : MonoBehaviour
{
    public static MilestoneReaderComponent Instance { get; private set; }

    private bool isActive;
    private UIMilestoneForm milestoneForm;
    private List<string> items;
    private int selectedIndex;
    private int collectDelay = -1;

    static MilestoneReaderComponent()
    {
        ClassInjector.RegisterTypeInIl2Cpp<MilestoneReaderComponent>();
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

    public void Activate(UIMilestoneForm form)
    {
        milestoneForm = form;
        isActive = false;
        items = null;
        selectedIndex = 0;
        collectDelay = 5; // wait frames for UI to populate
    }

    /// <summary>
    /// Called when milestones are re-setup (tab change). Re-collects items.
    /// </summary>
    public void Refresh()
    {
        if (milestoneForm == null) return;
        collectDelay = 3;
        isActive = false;
        items = null;
        selectedIndex = 0;
    }

    public void Deactivate()
    {
        isActive = false;
        collectDelay = -1;
        milestoneForm = null;
        items = null;
    }

    void Update()
    {
        try
        {
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

            // Navigation with W/S or D-Pad
            if (InputHelper.NavigateUp())
                Navigate(-1);
            else if (InputHelper.NavigateDown())
                Navigate(1);
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"MilestoneReader Update error: {e.Message}");
        }
    }


    private void Navigate(int direction)
    {
        selectedIndex += direction;
        if (selectedIndex < 0) selectedIndex = items.Count - 1;
        if (selectedIndex >= items.Count) selectedIndex = 0;

        ScreenReader.Interrupt(items[selectedIndex]);
    }

    private void CollectItems()
    {
        if (milestoneForm == null) return;

        items = new List<string>();

        try
        {
            var uiMilestones = milestoneForm.uiMilestones;
            if (uiMilestones == null || uiMilestones.Count == 0)
            {
                items.Add("No milestones");
                isActive = true;
                selectedIndex = 0;
                ScreenReader.Interrupt("Milestones. W and S to browse. No milestones in this category.");
                return;
            }

            for (int i = 0; i < uiMilestones.Count; i++)
            {
                try
                {
                    var milestone = uiMilestones[i];
                    if (milestone == null) continue;

                    // Skip hidden milestones
                    if (!IsVisible(milestone)) continue;

                    string text = BuildMilestoneText(milestone);
                    if (!string.IsNullOrEmpty(text))
                        items.Add(text);
                }
                catch (Exception e)
                {
                    Plugin.Log?.LogDebug($"MilestoneReader: Error reading milestone {i}: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"MilestoneReader CollectItems error: {e.Message}");
        }

        if (items.Count == 0)
            items.Add("No milestones available");

        isActive = true;
        selectedIndex = 0;

        string announcement = $"Milestones. {items.Count} items. W and S to browse. {items[0]}";
        ScreenReader.Interrupt(announcement);
    }

    private string BuildMilestoneText(UIMilestoneProgress milestone)
    {
        var sb = new StringBuilder();

        // Check completion state
        bool isComplete = false;
        try
        {
            var completeGOs = milestone.completeGOs;
            if (completeGOs != null && completeGOs.Length > 0)
                isComplete = completeGOs[0] != null && completeGOs[0].activeInHierarchy;
        }
        catch { }

        // Description
        string desc = GetText(milestone.description);
        if (!string.IsNullOrEmpty(desc))
            sb.Append(desc);

        // Progress
        string progress = GetText(milestone.barText1);
        if (!string.IsNullOrEmpty(progress))
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append($"Progress: {progress}");
        }

        // Completion status
        if (isComplete)
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append("Complete");
        }

        // Reward
        string reward = GetText(milestone.rewardDescription);
        if (!string.IsNullOrEmpty(reward))
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append($"Reward: {reward}");
        }

        // Requirements
        try
        {
            var requirements = milestone.requirements;
            if (requirements != null && requirements.Length > 0)
            {
                for (int r = 0; r < requirements.Length; r++)
                {
                    var req = requirements[r];
                    if (req == null) continue;
                    if (!IsVisible(req)) continue;

                    string reqText = GetText(req.reqText);
                    if (!string.IsNullOrEmpty(reqText))
                    {
                        if (sb.Length > 0) sb.Append(". ");
                        sb.Append($"Requires: {reqText}");
                    }
                }
            }
        }
        catch { }

        return sb.ToString();
    }

    private string GetText(TMP_Text field)
    {
        if (field == null) return null;
        try
        {
            string text = field.text;
            if (string.IsNullOrEmpty(text)) return null;
            return TextHelper.CleanText(text);
        }
        catch { return null; }
    }

    private bool IsVisible(Component comp)
    {
        try { return comp != null && comp.gameObject.activeInHierarchy; }
        catch { return false; }
    }

    void OnDestroy()
    {
        Instance = null;
    }
}
