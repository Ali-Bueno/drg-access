using HarmonyLib;
using DRS.UI;

namespace drgAccess.Patches;

/// <summary>
/// Patches for UITooltip to announce tooltip content when shown.
/// </summary>
public static class UITooltipPatch
{
    private static string _lastTooltipText = "";

    // Patch ShowTooltip - this is called when the tooltip is actually displayed
    [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.ShowTooltip))]
    public static class ShowTooltip_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(UITooltip __instance)
        {
            try
            {
                ReadTooltipContent(__instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UITooltipPatch.ShowTooltip error: {ex.Message}");
            }
        }
    }

    // Patch ShowImmediately for direct text tooltips (takes string and UITooltipAnchor)
    [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.ShowImmediately))]
    public static class ShowImmediately_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(UITooltip __instance, string text, UITooltipAnchor anchor)
        {
            try
            {
                if (!string.IsNullOrEmpty(text))
                {
                    string cleanText = CleanText(text);
                    if (!string.IsNullOrEmpty(cleanText) && cleanText != _lastTooltipText)
                    {
                        _lastTooltipText = cleanText;
                        ScreenReader.Say(cleanText);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UITooltipPatch.ShowImmediately error: {ex.Message}");
            }
        }
    }

    private static void ReadTooltipContent(UITooltip tooltip)
    {
        if (tooltip == null)
            return;

        // Try description TextMeshProUGUI first, then buildString as fallback
        string text = null;
        var descriptionText = tooltip.description;
        if (descriptionText != null && !string.IsNullOrEmpty(descriptionText.text))
            text = descriptionText.text;
        else if (!string.IsNullOrEmpty(tooltip.buildString))
            text = tooltip.buildString;

        if (!string.IsNullOrEmpty(text))
        {
            string cleanText = CleanText(text);
            if (!string.IsNullOrEmpty(cleanText) && cleanText != _lastTooltipText)
            {
                _lastTooltipText = cleanText;
                ScreenReader.Say(cleanText);
            }
        }
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove rich text tags
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        // Remove serial number patterns like "nº cm-718-689" or "n° XX-XXX-XXX"
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[Nn][º°]\s*\S+", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }
}
