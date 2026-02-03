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

        // Try to read from description TextMeshProUGUI
        var descriptionText = tooltip.description;
        if (descriptionText != null)
        {
            string text = descriptionText.text;
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
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove rich text tags
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        return text.Trim();
    }
}
