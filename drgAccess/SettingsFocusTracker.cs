using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DRS.UI;
using TMPro;

namespace drgAccess;

/// <summary>
/// Polls EventSystem for focus changes and announces control labels.
/// This is safer than patching Selectable.OnSelect which crashes during IL2CPP init.
/// Only active when the settings form is open.
/// </summary>
public class SettingsFocusTracker : MonoBehaviour
{
    private GameObject _lastSelected;

    /// <summary>
    /// Frame number when a slider received focus. Used by SetValueText patch
    /// to avoid interrupting the focus announcement with just the value.
    /// </summary>
    internal static int LastSliderFocusFrame = -1;

    /// <summary>
    /// Suppress all focus announcements until this frame. Used by screens
    /// that announce their own context on open (e.g. GammaAdjuster).
    /// </summary>
    internal static int SuppressUntilFrame = -1;

    void Update()
    {
        if (!Patches.UISettingsPatch.SettingsOpen && !Patches.UIFormPatches.GammaAdjusterOpen)
            return;

        var es = EventSystem.current;
        if (es == null)
            return;

        var current = es.currentSelectedGameObject;
        if (current == _lastSelected)
            return;

        _lastSelected = current;
        if (current == null)
            return;

        // Suppress announcements for a few frames after a screen opens with its own announcement
        if (Time.frameCount <= SuppressUntilFrame)
            return;

        try
        {
            // Skip UIButton — UIButtonPatch handles those
            if (current.GetComponent<UIButton>() != null)
                return;

            // Try to identify the control and announce it
            var slider = current.GetComponent<UISettingsSlider>();
            if (slider != null)
            {
                LastSliderFocusFrame = Time.frameCount;

                string label = Patches.UISettingsPatch.GetSliderLabel(slider);
                string value = Patches.UISettingsPatch.GetSliderValue(slider);

                string msg = !string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value)
                    ? $"{label}: {value}"
                    : !string.IsNullOrEmpty(label) ? label
                    : !string.IsNullOrEmpty(value) ? value
                    : "";
                if (!string.IsNullOrEmpty(msg))
                    ScreenReader.Interrupt(msg);
                return;
            }

            var toggle = current.GetComponent<Toggle>();
            if (toggle != null)
            {
                string label = Patches.UISettingsPatch.GetControlLabel(toggle.transform);
                string state = toggle.isOn ? "On" : "Off";
                string msg = !string.IsNullOrEmpty(label)
                    ? $"{label}: {state}"
                    : state;
                ScreenReader.Interrupt(msg);
                return;
            }

            // Generic selectable — try to read label
            string genericLabel = Patches.UISettingsPatch.GetControlLabel(current.transform);
            if (!string.IsNullOrEmpty(genericLabel))
                ScreenReader.Interrupt(genericLabel);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"FocusTracker error: {ex.Message}");
        }
    }

}
