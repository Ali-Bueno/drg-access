using HarmonyLib;
using DRS.UI;
using TMPro;
using System.Text;
using drgAccess.Helpers;

namespace drgAccess.Patches;

/// <summary>
/// Patches for pause menu detail panels: reads weapon stats, artifact descriptions,
/// and player stats when interacting with the pause screen.
/// </summary>

// Read weapon details when a weapon is selected in pause menu
[HarmonyPatch(typeof(UICorePauseForm), nameof(UICorePauseForm.OnWeaponSelect))]
public static class PauseWeaponSelectPatch
{
    public static void Postfix(UICorePauseForm __instance)
    {
        try
        {
            var details = __instance.weaponDetails;
            if (details == null) return;

            var sb = new StringBuilder();

            var statsText = details.statsText;
            if (statsText != null && !string.IsNullOrEmpty(statsText.text))
                sb.Append(TextHelper.CleanText(statsText.text));

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

            if (sb.Length > 0)
                ScreenReader.Say(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"PauseWeaponSelectPatch error: {ex.Message}");
        }
    }
}

// Read artifact details when an artifact is selected in pause menu
[HarmonyPatch(typeof(UICorePauseForm), nameof(UICorePauseForm.OnArtifactSelect))]
public static class PauseArtifactSelectPatch
{
    public static void Postfix(UICorePauseForm __instance)
    {
        try
        {
            var genericDetails = __instance.genericDetails;
            if (genericDetails != null && !string.IsNullOrEmpty(genericDetails.text))
            {
                ScreenReader.Say(TextHelper.CleanText(genericDetails.text));
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"PauseArtifactSelectPatch error: {ex.Message}");
        }
    }
}

// Read stats summary when pause form opens
[HarmonyPatch(typeof(UICorePauseForm), nameof(UICorePauseForm.Show))]
public static class PauseFormShowPatch
{
    public static void Postfix(UICorePauseForm __instance)
    {
        try
        {
            var uiStats = __instance.uiStats;
            if (uiStats == null || uiStats.Count == 0) return;

            var sb = new StringBuilder("Stats: ");
            bool first = true;

            for (int i = 0; i < uiStats.Count; i++)
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

                if (!first) sb.Append(", ");
                first = false;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    sb.Append($"{name} {value}");
                else if (!string.IsNullOrEmpty(name))
                    sb.Append(name);
                else
                    sb.Append(value);
            }

            if (!first)
                ScreenReader.Say(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"PauseFormShowPatch error: {ex.Message}");
        }
    }
}
