using HarmonyLib;
using DRS.UI;
using TMPro;
using System.Text;

namespace drgAccess.Patches;

/// <summary>
/// Patches for page description panels that update when buttons are selected.
/// These read the info panel beside the button list (title + description text).
/// </summary>
public static class UIPageDescriptionPatches
{
    // === UIChallengeSetSelectPage: main play menu mode selection ===

    [HarmonyPatch(typeof(UIChallengeSetSelectPage), nameof(UIChallengeSetSelectPage.OnMissionRoadSelect))]
    public static class ChallengeSetPage_OnMissionRoadSelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIChallengeSetSelectPage __instance)
        {
            AnnounceChallengeSetPageDescription(__instance);
        }
    }

    [HarmonyPatch(typeof(UIChallengeSetSelectPage), nameof(UIChallengeSetSelectPage.OnMasterySelect))]
    public static class ChallengeSetPage_OnMasterySelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIChallengeSetSelectPage __instance)
        {
            AnnounceChallengeSetPageDescription(__instance);
        }
    }

    [HarmonyPatch(typeof(UIChallengeSetSelectPage), nameof(UIChallengeSetSelectPage.OnAnomalySelect))]
    public static class ChallengeSetPage_OnAnomalySelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIChallengeSetSelectPage __instance)
        {
            AnnounceChallengeSetPageDescription(__instance);
        }
    }

    [HarmonyPatch(typeof(UIChallengeSetSelectPage), nameof(UIChallengeSetSelectPage.OnDailyRunSelect))]
    public static class ChallengeSetPage_OnDailyRunSelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIChallengeSetSelectPage __instance)
        {
            AnnounceChallengeSetPageDescription(__instance);
        }
    }

    [HarmonyPatch(typeof(UIChallengeSetSelectPage), nameof(UIChallengeSetSelectPage.OnWeeklyRunSelect))]
    public static class ChallengeSetPage_OnWeeklyRunSelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIChallengeSetSelectPage __instance)
        {
            AnnounceChallengeSetPageDescription(__instance);
        }
    }

    // === UIMasteryTypePage ===

    [HarmonyPatch(typeof(UIMasteryTypePage), nameof(UIMasteryTypePage.OnChallengeSetSelect))]
    public static class MasteryPage_OnChallengeSetSelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIMasteryTypePage __instance)
        {
            AnnounceDescription(__instance.descriptionTitle, __instance.description, "MasteryPage");
        }
    }

    // === UIAnomalySelectPage ===

    [HarmonyPatch(typeof(UIAnomalySelectPage), nameof(UIAnomalySelectPage.OnChallengeSetSelect))]
    public static class AnomalyPage_OnChallengeSetSelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIAnomalySelectPage __instance)
        {
            AnnounceDescription(__instance.windowTitle, __instance.description, "AnomalyPage");
        }
    }

    // === UIMissionSelectPage (Elimination / Escort) ===

    [HarmonyPatch(typeof(UIMissionSelectPage), nameof(UIMissionSelectPage.OnMissionRoadSelect))]
    public static class MissionPage_OnMissionRoadSelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIMissionSelectPage __instance)
        {
            AnnounceDescription(__instance.windowTitle, __instance.description, "MissionPage");
        }
    }

    // === UIBiomeSelectPage (biome selection - shows biome name + description) ===

    [HarmonyPatch(typeof(UIBiomeSelectPage), nameof(UIBiomeSelectPage.OnBiomeSelect))]
    public static class BiomePage_OnBiomeSelect
    {
        [HarmonyPostfix]
        public static void Postfix(UIBiomeSelectPage __instance)
        {
            AnnounceDescription(__instance.title, __instance.description, "BiomePage");
        }
    }

    // === Helpers ===

    private static void AnnounceChallengeSetPageDescription(UIChallengeSetSelectPage page)
    {
        try
        {
            var sb = new StringBuilder();
            var titleText = page.title;
            if (titleText != null && !string.IsNullOrEmpty(titleText.text))
                sb.Append(CleanText(titleText.text));

            var descText = page.description;
            if (descText != null && !string.IsNullOrEmpty(descText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(descText.text));
            }

            if (sb.Length > 0)
                ScreenReader.Say(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIPageDescriptionPatches.ChallengeSetPage error: {ex.Message}");
        }
    }

    private static void AnnounceDescription(TextMeshProUGUI titleTMP, TextMeshProUGUI descTMP, string context)
    {
        try
        {
            var sb = new StringBuilder();
            if (titleTMP != null && !string.IsNullOrEmpty(titleTMP.text))
                sb.Append(CleanText(titleTMP.text));

            if (descTMP != null && !string.IsNullOrEmpty(descTMP.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(descTMP.text));
            }

            if (sb.Length > 0)
                ScreenReader.Say(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIPageDescriptionPatches.{context} error: {ex.Message}");
        }
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        return text.Trim();
    }
}
