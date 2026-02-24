using Assets.Scripts.Data;
using DRS.UI;
using TMPro;
using System.Text;
using drgAccess.Helpers;

namespace drgAccess.Patches;

// Mission, campaign, and challenge button text extraction
public static partial class UIButtonPatch
{
    private static string GetMissionRoadButtonText(UIMissionRoadButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var nameText = button.nameText;
            if (nameText != null && !string.IsNullOrEmpty(nameText.text))
                sb.Append(TextHelper.CleanText(nameText.text));

            var goalText = button.goalCounterText;
            if (goalText != null && !string.IsNullOrEmpty(goalText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(goalText.text));
            }

            if (!button.isUnlocked)
            {
                var unlockText = button.unlockConditionText;
                if (unlockText != null && !string.IsNullOrEmpty(unlockText.text))
                {
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(ModLocalization.Get("ui_locked") + ". " + TextHelper.CleanText(unlockText.text));
                }
                else
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(ModLocalization.Get("ui_locked"));
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMissionRoadButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetMissionSectorButtonText(UIMissionSectorButton button)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(ModLocalization.Get("mission_sector", button.sectorNumber));

            var goalText = button.biomeGoalCountText;
            if (goalText != null && !string.IsNullOrEmpty(goalText.text))
            {
                sb.Append(". " + TextHelper.CleanText(goalText.text));
            }

            if (button.isLocked)
            {
                sb.Append(", " + ModLocalization.Get("ui_locked"));
            }
            else if (button.nodeCompleted)
            {
                sb.Append(", " + ModLocalization.Get("ui_completed"));
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMissionSectorButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetMissionNodeButtonText(UIMissionNodeButton button)
    {
        try
        {
            var sb = new StringBuilder();

            // Read biome name from biomeLevelData
            var biomeLevelData = button.biomeLevelData;
            if (biomeLevelData != null)
            {
                var biomeData = biomeLevelData.BiomeData;
                if (biomeData != null)
                {
                    string biomeName = biomeData.DisplayName;
                    if (!string.IsNullOrEmpty(biomeName))
                        sb.Append(TextHelper.CleanText(biomeName));

                    // Try to read biome description/lore
                    string lore = TryGetBiomeLore(biomeData);
                    if (!string.IsNullOrEmpty(lore))
                    {
                        sb.Append(". ");
                        sb.Append(lore);
                    }
                }
            }

            if (sb.Length == 0)
                sb.Append(ModLocalization.Get("mission_node"));

            var reqText = button.biomeGoalRequirementText;
            if (reqText != null && !string.IsNullOrEmpty(reqText.text))
            {
                string cleanReq = TextHelper.CleanText(reqText.text);
                if (!string.IsNullOrEmpty(cleanReq))
                {
                    // If it's just a number, it's the high score
                    if (TextHelper.IsJustNumber(cleanReq))
                    {
                        sb.Append(". " + ModLocalization.Get("mission_high_score", cleanReq));
                    }
                    else
                    {
                        // Otherwise it's requirement text
                        sb.Append(". ");
                        sb.Append(cleanReq);
                    }
                }
            }

            if (button.isLocked)
            {
                sb.Append(", " + ModLocalization.Get("ui_locked"));
            }
            else if (button.nodeCompleted)
            {
                sb.Append(", " + ModLocalization.Get("ui_completed"));
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMissionNodeButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string TryGetBiomeLore(BiomeData biomeData)
    {
        if (biomeData == null)
            return null;

        try
        {
            // Try native Lore property
            string lore = biomeData.Lore;
            if (!string.IsNullOrEmpty(lore))
                return TextHelper.CleanText(lore);
        }
        catch { /* Native getter failed */ }

        try
        {
            // Try localized lore
            var locLore = biomeData.locLore;
            if (locLore != null)
            {
                string lore = locLore.GetLocalizedString();
                if (!string.IsNullOrEmpty(lore))
                    return TextHelper.CleanText(lore);
            }
        }
        catch { /* Localized lore not available */ }

        return null;
    }

    private static string GetMissionGateButtonText(UIMissionGateButton button)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(ModLocalization.Get("mission_gate", button.gateNumber));

            if (button.isLocked)
            {
                sb.Append(", " + ModLocalization.Get("ui_locked"));
            }
            else if (button.nodeCompleted)
            {
                sb.Append(", " + ModLocalization.Get("ui_completed"));
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMissionGateButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetCampaignSetButtonText(UICampaignSetButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var nameText = button.nameText;
            if (nameText != null && !string.IsNullOrEmpty(nameText.text))
                sb.Append(TextHelper.CleanText(nameText.text));

            var progressText = button.progressText;
            if (progressText != null && !string.IsNullOrEmpty(progressText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(progressText.text));
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetCampaignSetButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetChallengeButtonText(UIChallengeButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var challengeData = button.ChallengeData;
            if (challengeData != null)
            {
                string title = challengeData.GetTitle();
                if (!string.IsNullOrEmpty(title))
                    sb.Append(TextHelper.CleanText(title));

                string desc = challengeData.GetPreDescription();
                if (!string.IsNullOrEmpty(desc))
                {
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(TextHelper.CleanText(desc));
                }
            }

            var progressText = button.progressText;
            if (progressText != null && !string.IsNullOrEmpty(progressText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(progressText.text));
            }

            bool isCompleted = button.completedGroup != null && button.completedGroup.activeSelf;
            if (isCompleted)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(ModLocalization.Get("ui_completed"));
            }
            else
            {
                bool isLocked = button.lockedGroup != null && button.lockedGroup.activeSelf;
                if (isLocked)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(ModLocalization.Get("ui_locked"));
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetChallengeButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetChallengeSetButtonText(UIChallengeSetButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var nameText = button.nameText;
            if (nameText != null && !string.IsNullOrEmpty(nameText.text))
                sb.Append(TextHelper.CleanText(nameText.text));

            var progressText = button.progressText;
            if (progressText != null && !string.IsNullOrEmpty(progressText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(progressText.text));
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetChallengeSetButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetFixedRunButtonText(UIFixedRunButton button)
    {
        try
        {
            var sb = new StringBuilder();

            // Try to read meaningful text from the button's side group TMP children
            string runName = null;
            var sideGroup = button.isLocked ? button.sideLockedGroup : button.sideGroup;
            if (sideGroup != null)
            {
                var tmps = sideGroup.GetComponentsInChildren<TextMeshProUGUI>();
                if (tmps != null)
                {
                    foreach (var tmp in tmps)
                    {
                        if (tmp == null || tmp == button.completedCounter)
                            continue;
                        string text = TextHelper.CleanText(tmp.text);
                        if (!string.IsNullOrEmpty(text))
                        {
                            // Skip texts that are just numbers (visual counters/sprites)
                            if (int.TryParse(text, out _))
                                continue;
                            if (runName == null)
                                runName = text;
                            else
                                runName += ". " + text;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(runName))
                sb.Append(runName);
            else
                sb.Append(ModLocalization.Get("mission_fixed_run"));

            if (button.isLocked)
            {
                sb.Append(", " + ModLocalization.Get("ui_locked"));
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetFixedRunButtonText error: {ex.Message}");
            return null;
        }
    }
}
