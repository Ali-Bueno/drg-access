using HarmonyLib;
using DRS.UI;
using Assets.Scripts.UI;
using Assets.Scripts.Data;
using UnityEngine.EventSystems;
using TMPro;
using System.Text;
using drgAccess.Helpers;

namespace drgAccess.Patches;

/// <summary>
/// Patches for UIButton to announce button text when selected.
/// Handles all specialized buttons with tooltip data.
/// Split into partial class files by domain:
///   - UIButtonPatch.cs (this file): Core dispatch + simple handlers
///   - UIButtonPatch.ClassSelection.cs: Class/subclass selection
///   - UIButtonPatch.Mission.cs: Mission/campaign/challenge buttons
///   - UIButtonPatch.Gear.cs: Gear inventory + stat upgrades
/// </summary>
[HarmonyPatch(typeof(UIButton))]
public static partial class UIButtonPatch
{
    [HarmonyPatch(nameof(UIButton.OnSelect))]
    [HarmonyPostfix]
    public static void OnSelect_Postfix(UIButton __instance, BaseEventData bed)
    {
        try
        {
            string buttonType = __instance.GetType().Name;
            Plugin.Log?.LogInfo($"UIButton.OnSelect - Button type: {buttonType}, GameObject: {__instance.gameObject.name}");

            string buttonText = GetButtonText(__instance);
            if (!string.IsNullOrEmpty(buttonText))
            {
                Plugin.Log?.LogInfo($"UIButton.OnSelect - Announcing: '{buttonText}'");
                ScreenReader.Interrupt(buttonText);
            }
            else
            {
                Plugin.Log?.LogWarning($"UIButton.OnSelect - No text generated for button type: {buttonType}");
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.OnSelect error: {ex.Message}");
        }
    }

    private static string GetButtonText(UIButton button)
    {
        if (button == null)
            return null;

        // StepSelectorBase left/right buttons: announce selector label + value + direction
        var selector = button.GetComponentInParent<StepSelectorBase>();
        if (selector != null)
        {
            return GetStepSelectorText(selector, button);
        }

        // Check specialized button types in order of specificity

        var classArtifactButton = button.TryCast<UIClassArtifactButton>();
        if (classArtifactButton != null)
            return GetClassArtifactButtonText(classArtifactButton);

        var skillButton = button.TryCast<UISkillButton>();
        if (skillButton != null)
            return GetSkillButtonText(skillButton);

        var classButton = button.TryCast<UIClassSelectButton>();
        if (classButton != null)
            return GetClassButtonText(classButton);

        var mutatorView = button.TryCast<UIMutatorView>();
        if (mutatorView != null)
            return GetMutatorViewText(mutatorView);

        var biomeButton = button.TryCast<UIBiomeSelectButton>();
        if (biomeButton != null)
            return GetBiomeButtonText(biomeButton);

        var shopButton = button.TryCast<UIShopButton>();
        if (shopButton != null)
            return GetShopButtonText(shopButton);

        var hazLevelButton = button.TryCast<UIHazLevelButton>();
        if (hazLevelButton != null)
            return GetHazLevelButtonText(hazLevelButton);

        var sliderToggle = button.TryCast<UISliderToggle>();
        if (sliderToggle != null)
            return GetSliderToggleText(sliderToggle);

        // Mission buttons
        var missionRoadButton = button.TryCast<UIMissionRoadButton>();
        if (missionRoadButton != null)
            return GetMissionRoadButtonText(missionRoadButton);

        var missionSectorButton = button.TryCast<UIMissionSectorButton>();
        if (missionSectorButton != null)
            return GetMissionSectorButtonText(missionSectorButton);

        var missionNodeButton = button.TryCast<UIMissionNodeButton>();
        if (missionNodeButton != null)
            return GetMissionNodeButtonText(missionNodeButton);

        var missionGateButton = button.TryCast<UIMissionGateButton>();
        if (missionGateButton != null)
            return GetMissionGateButtonText(missionGateButton);

        // Campaign/Challenge buttons
        var campaignSetButton = button.TryCast<UICampaignSetButton>();
        if (campaignSetButton != null)
            return GetCampaignSetButtonText(campaignSetButton);

        var challengeButton = button.TryCast<UIChallengeButton>();
        if (challengeButton != null)
            return GetChallengeButtonText(challengeButton);

        var challengeSetButton = button.TryCast<UIChallengeSetButton>();
        if (challengeSetButton != null)
            return GetChallengeSetButtonText(challengeSetButton);

        var fixedRunButton = button.TryCast<UIFixedRunButton>();
        if (fixedRunButton != null)
            return GetFixedRunButtonText(fixedRunButton);

        // Stat upgrade button
        var statUpgradeButton = button.TryCast<UIStatUpgradeButton>();
        if (statUpgradeButton != null)
            return GetStatUpgradeButtonText(statUpgradeButton);

        var mutatorButton = button.TryCast<UIMutatorButton>();
        if (mutatorButton != null)
            return GetMutatorButtonText(mutatorButton);

        var mineralMarketButton = button.TryCast<UIMineralMarketButton>();
        if (mineralMarketButton != null)
            return GetMineralMarketButtonText(mineralMarketButton);

        var setProgressButton = button.TryCast<UISetProgressButton>();
        if (setProgressButton != null)
            return GetSetProgressButtonText(setProgressButton);

        var skinOverridesButton = button.TryCast<UISkinOverridesButton>();
        if (skinOverridesButton != null)
            return GetSkinOverridesButtonText(skinOverridesButton);

        var gearViewCompact = button.TryCast<UIGearViewCompact>();
        if (gearViewCompact != null)
            return GetGearViewCompactText(gearViewCompact);

        // Save slot buttons
        var saveSlot = button.TryCast<UISaveSlot>();
        if (saveSlot != null)
            return GetSaveSlotText(saveSlot);

        // Pause menu buttons
        var pauseWeapon = button.TryCast<UIPauseWeapon>();
        if (pauseWeapon != null)
            return GetPauseWeaponText(pauseWeapon);

        var pauseArtifact = button.TryCast<UIPauseArtifact>();
        if (pauseArtifact != null)
            return GetPauseArtifactText(pauseArtifact);

        // Default: try to get text from buttonText field
        return GetDefaultButtonText(button);
    }

    // === Simple button handlers ===

    private static string GetSkillButtonText(UISkillButton skillButton)
    {
        try
        {
            var skillData = skillButton.skillData;
            if (skillData != null)
            {
                string title = skillData.Title;
                ERarity rarity = skillButton.rarity;

                if (!string.IsNullOrEmpty(title))
                {
                    var sb = new StringBuilder();

                    // Weapon name prefix if weapon-specific
                    string weaponName = GetWeaponNameFromSkillRaritySet(skillButton.skillRaritySet);
                    if (!string.IsNullOrEmpty(weaponName))
                        sb.Append(weaponName + ": ");

                    sb.Append(TextHelper.CleanText(title) + ", " + LocalizationHelper.GetRarityText(rarity));

                    string statDescription = skillData.GetStatDescription(rarity);
                    if (!string.IsNullOrEmpty(statDescription))
                        sb.Append(". " + TextHelper.CleanText(statDescription));

                    string description = skillData.GetDescription(rarity);
                    if (!string.IsNullOrEmpty(description))
                        sb.Append(". " + TextHelper.CleanText(description));

                    return sb.ToString();
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetSkillButtonText error: {ex.Message}");
        }

        return null;
    }

    private static string GetMutatorViewText(UIMutatorView mutatorView)
    {
        try
        {
            var mutatorData = mutatorView.data;
            if (mutatorData != null)
            {
                string title = mutatorData.GetTitle();
                string description = mutatorData.GetDescription();

                if (!string.IsNullOrEmpty(title))
                {
                    string result = TextHelper.CleanText(title);
                    if (!string.IsNullOrEmpty(description))
                    {
                        result += ". " + TextHelper.CleanText(description);
                    }
                    return result;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMutatorViewText error: {ex.Message}");
        }

        return null;
    }

    private static string GetBiomeButtonText(UIBiomeSelectButton biomeButton)
    {
        try
        {
            var allTMPs = biomeButton.GetComponentsInChildren<TextMeshProUGUI>();
            if (allTMPs != null && allTMPs.Length > 0)
            {
                Plugin.Log?.LogInfo($"BiomeButton has {allTMPs.Length} TMP components:");
                for (int i = 0; i < allTMPs.Length; i++)
                {
                    var tmp = allTMPs[i];
                    if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                    {
                        Plugin.Log?.LogInfo($"  TMP[{i}] ({tmp.gameObject.name}): '{tmp.text}'");
                    }
                }
            }

            var biomeLevelData = biomeButton.biomeLevelData;
            if (biomeLevelData != null)
            {
                var biomeData = biomeLevelData.BiomeData;
                if (biomeData != null)
                {
                    string displayName = biomeData.DisplayName;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        string result = TextHelper.CleanText(displayName);

                        bool isLocked = biomeButton.lockedGroup != null &&
                                       biomeButton.lockedGroup.activeSelf;

                        if (isLocked)
                        {
                            result += ", Locked";
                        }

                        Plugin.Log?.LogInfo($"BiomeButton final result: '{result}'");
                        return result;
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetBiomeButtonText error: {ex.Message}");
        }

        return null;
    }

    private static string GetShopButtonText(UIShopButton shopButton)
    {
        try
        {
            // Check if this is an empty slot
            if (!shopButton.HasContent())
                return "Empty";

            var skillData = shopButton.skillData;
            if (skillData != null)
            {
                string title = skillData.Title;

                if (!string.IsNullOrEmpty(title))
                {
                    var sb = new StringBuilder();

                    // Weapon name prefix if weapon-specific
                    string weaponName = GetWeaponNameFromSkillRaritySet(shopButton.skillRaritySet);
                    if (!string.IsNullOrEmpty(weaponName))
                        sb.Append(weaponName + ": ");

                    sb.Append(TextHelper.CleanText(title));

                    // Rarity
                    ERarity rarity = shopButton.rarity;
                    sb.Append(", " + LocalizationHelper.GetRarityText(rarity));

                    string statDescription = skillData.GetStatDescription(rarity);
                    if (!string.IsNullOrEmpty(statDescription))
                        sb.Append(". " + TextHelper.CleanText(statDescription));

                    string description = skillData.GetDescription(rarity);
                    if (!string.IsNullOrEmpty(description))
                        sb.Append(". " + TextHelper.CleanText(description));

                    // Price with currency name
                    var priceText = shopButton.priceText;
                    if (priceText != null && !string.IsNullOrEmpty(priceText.text))
                    {
                        string priceAmount = TextHelper.CleanText(priceText.text);
                        string currencyName = null;
                        try
                        {
                            var priceValue = shopButton.price;
                            if (priceValue != null)
                                currencyName = LocalizationHelper.GetCurrencyName(priceValue.type);
                        }
                        catch { }

                        if (!string.IsNullOrEmpty(currencyName))
                            sb.Append($". Price: {priceAmount} {currencyName}");
                        else
                            sb.Append(". Price: " + priceAmount);
                    }

                    // Affordability
                    if (!shopButton.canAfford)
                        sb.Append(", Cannot afford");

                    return sb.ToString();
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetShopButtonText error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Tries to extract a weapon name from a SkillRaritySet by casting to WeaponSkillRaritySet.
    /// Returns null if not weapon-specific.
    /// </summary>
    private static string GetWeaponNameFromSkillRaritySet(SkillRaritySet srs)
    {
        try
        {
            if (srs == null) return null;
            var weaponSrs = srs.TryCast<WeaponSkillRaritySet>();
            if (weaponSrs == null) return null;

            var wh = weaponSrs.WeaponHandler;
            if (wh == null) return null;

            var weaponData = wh.Data;
            if (weaponData == null) return null;

            string weaponTitle = weaponData.Title;
            if (!string.IsNullOrEmpty(weaponTitle))
                return TextHelper.CleanText(weaponTitle);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogDebug($"GetWeaponNameFromSkillRaritySet: {ex.Message}");
        }
        return null;
    }

    private static string GetHazLevelButtonText(UIHazLevelButton hazLevelButton)
    {
        try
        {
            var hazLevelText = hazLevelButton.hazLevelText;
            if (hazLevelText != null && !string.IsNullOrEmpty(hazLevelText.text))
            {
                string result = TextHelper.CleanText(hazLevelText.text);

                bool isLocked = hazLevelButton.lockedGroup != null &&
                               hazLevelButton.lockedGroup.activeSelf;

                if (isLocked)
                {
                    result += ", Locked";
                }

                return result;
            }

            int hazLevel = hazLevelButton.hazLevel;
            return $"Hazard {hazLevel}";
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetHazLevelButtonText error: {ex.Message}");
        }

        return null;
    }

    private static string GetSliderToggleText(UISliderToggle sliderToggle)
    {
        try
        {
            string labelText = "";

            TextMeshProUGUI textComponent = sliderToggle.buttonText;
            if (textComponent != null && !string.IsNullOrEmpty(textComponent.text))
            {
                labelText = TextHelper.CleanText(textComponent.text);
            }
            else
            {
                // Search children, siblings, and parent hierarchy for a label
                string controlLabel = UISettingsPatch.GetControlLabel(sliderToggle.transform);
                if (!string.IsNullOrEmpty(controlLabel))
                    labelText = controlLabel;
            }

            bool isToggled = sliderToggle.IsToggled;
            string state = isToggled ? "On" : "Off";

            return !string.IsNullOrEmpty(labelText)
                ? $"{labelText}, {state}"
                : state;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetSliderToggleText error: {ex.Message}");
        }

        return null;
    }

    private static string GetMutatorButtonText(UIMutatorButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var titleText = button.title;
            if (titleText != null && !string.IsNullOrEmpty(titleText.text))
                sb.Append(TextHelper.CleanText(titleText.text));

            var descText = button.description;
            if (descText != null && !string.IsNullOrEmpty(descText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(descText.text));
            }

            var pointsText = button.pointsText;
            if (pointsText != null && !string.IsNullOrEmpty(pointsText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(pointsText.text) + " points");
            }

            if (button.isToggled)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("Active");
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMutatorButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetMineralMarketButtonText(UIMineralMarketButton button)
    {
        try
        {
            var sb = new StringBuilder();

            // Read localized text from TMP children instead of enum ToString()
            var tmps = button.GetComponentsInChildren<TextMeshProUGUI>();
            if (tmps != null && tmps.Length > 0)
            {
                foreach (var tmp in tmps)
                {
                    if (tmp == null) continue;
                    string text = TextHelper.CleanText(tmp.text);
                    if (!string.IsNullOrEmpty(text))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(text);
                    }
                }
            }

            // Fallback to enum values if no TMP text found
            if (sb.Length == 0)
            {
                sb.Append(button.material.ToString());
                sb.Append($", {button.marketAction}");
            }

            // Add price
            int price = button.marketAction == UIMineralMarketButton.EMineralMarketAction.BUY ? button.buyPrice : button.sellPrice;
            sb.Append($", Price: {price}");

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMineralMarketButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetSetProgressButtonText(UISetProgressButton button)
    {
        try
        {
            var progressText = button.progressText;
            if (progressText != null && !string.IsNullOrEmpty(progressText.text))
                return TextHelper.CleanText(progressText.text);
            return null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetSetProgressButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetSkinOverridesButtonText(UISkinOverridesButton button)
    {
        try
        {
            var titleText = button.title;
            if (titleText != null && !string.IsNullOrEmpty(titleText.text))
                return TextHelper.CleanText(titleText.text);

            var streamerTitle = button.streamerTitle;
            if (streamerTitle != null && !string.IsNullOrEmpty(streamerTitle.text))
                return TextHelper.CleanText(streamerTitle.text);

            return null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetSkinOverridesButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetSaveSlotText(UISaveSlot slot)
    {
        try
        {
            int slotNum = (int)slot.SaveSlot + 1;
            var sb = new StringBuilder($"Save Slot {slotNum}");

            // Check if slot has data by looking at the rank value text
            var rankVal = slot.playerRankValueText;
            if (rankVal == null || string.IsNullOrEmpty(rankVal.text))
            {
                sb.Append(", Empty");
                return sb.ToString();
            }

            // Rank
            var rankHeader = slot.playerRankHeaderText;
            if (rankHeader != null && !string.IsNullOrEmpty(rankHeader.text))
                sb.Append($". {TextHelper.CleanText(rankHeader.text)}: {TextHelper.CleanText(rankVal.text)}");

            // Dives
            var divesVal = slot.divesValueText;
            var divesHeader = slot.divesHeaderText;
            if (divesHeader != null && divesVal != null
                && !string.IsNullOrEmpty(divesVal.text))
                sb.Append($". {TextHelper.CleanText(divesHeader.text)}: {TextHelper.CleanText(divesVal.text)}");

            // Mission Goals
            var goalsVal = slot.missionGoalsValueText;
            var goalsHeader = slot.missionGoalsHeaderText;
            if (goalsHeader != null && goalsVal != null
                && !string.IsNullOrEmpty(goalsVal.text))
                sb.Append($". {TextHelper.CleanText(goalsHeader.text)}: {TextHelper.CleanText(goalsVal.text)}");

            // Last Saved
            var savedVal = slot.lastSavedValueText;
            var savedHeader = slot.lastSavedHeaderText;
            if (savedHeader != null && savedVal != null
                && !string.IsNullOrEmpty(savedVal.text))
                sb.Append($". {TextHelper.CleanText(savedHeader.text)}: {TextHelper.CleanText(savedVal.text)}");

            // Early Access indicator
            var earlyRoot = slot.earlyAccessRoot;
            if (earlyRoot != null && earlyRoot.activeSelf)
            {
                var earlyText = slot.earlyAccessText;
                if (earlyText != null && !string.IsNullOrEmpty(earlyText.text))
                    sb.Append($". {TextHelper.CleanText(earlyText.text)}");
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"GetSaveSlotText error: {ex.Message}");
        }
        return null;
    }

    private static string GetDefaultButtonText(UIButton button)
    {
        Plugin.Log?.LogInfo($"GetDefaultButtonText for: {button.gameObject.name}");

        TextMeshProUGUI textComponent = button.buttonText;
        if (textComponent != null)
        {
            string text = textComponent.text;
            Plugin.Log?.LogInfo($"  buttonText field: '{text}'");
            if (!string.IsNullOrEmpty(text))
            {
                string result = TextHelper.CleanText(text);
                Plugin.Log?.LogInfo($"  Returning from buttonText: '{result}'");
                return result;
            }
        }

        // Fallback: try to find TextMeshProUGUI in children
        var allTMPs = button.GetComponentsInChildren<TextMeshProUGUI>();
        Plugin.Log?.LogInfo($"  Found {allTMPs?.Length ?? 0} TMP children");

        if (allTMPs != null && allTMPs.Length > 0)
        {
            var textParts = new System.Collections.Generic.List<string>();

            foreach (var tmp in allTMPs)
            {
                if (tmp == null || string.IsNullOrEmpty(tmp.text))
                    continue;

                string cleaned = TextHelper.CleanText(tmp.text);
                Plugin.Log?.LogInfo($"  TMP '{tmp.gameObject.name}': raw='{tmp.text}', cleaned='{cleaned}', IsJustNumber={TextHelper.IsJustNumber(cleaned)}");

                if (string.IsNullOrEmpty(cleaned))
                    continue;

                // Skip pure numbers (scores/stats) from mission node buttons
                if (button.gameObject.name.Contains("MissionNode") && TextHelper.IsJustNumber(cleaned))
                {
                    Plugin.Log?.LogInfo($"  -> Skipping numeric TMP: '{cleaned}'");
                    continue;
                }

                textParts.Add(cleaned);
            }

            if (textParts.Count > 0)
            {
                string result = string.Join(". ", textParts);
                Plugin.Log?.LogInfo($"  Returning joined result: '{result}'");
                return result;
            }
        }

        // Last resort: use GameObject name
        Plugin.Log?.LogInfo($"  Returning GameObject name");
        return TextHelper.CleanText(button.gameObject.name);
    }

    private static string GetStepSelectorText(StepSelectorBase selector, UIButton button)
    {
        try
        {
            string label = UISettingsPatch.GetControlLabel(selector.transform);

            // Determine direction
            string direction = "";
            if (selector.leftButton != null && button.gameObject == selector.leftButton.gameObject)
                direction = "Previous";
            else if (selector.rightButton != null && button.gameObject == selector.rightButton.gameObject)
                direction = "Next";

            // Find value text: look for TMP that's NOT inside leftButton or rightButton
            string value = null;
            for (int i = 0; i < selector.transform.childCount; i++)
            {
                var child = selector.transform.GetChild(i);
                if (selector.leftButton != null && child.gameObject == selector.leftButton.gameObject)
                    continue;
                if (selector.rightButton != null && child.gameObject == selector.rightButton.gameObject)
                    continue;

                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp == null)
                    tmp = child.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                {
                    value = TextHelper.CleanText(tmp.text);
                    break;
                }
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(label))
                sb.Append(label);
            if (!string.IsNullOrEmpty(value))
            {
                if (sb.Length > 0) sb.Append(": ");
                sb.Append(value);
            }
            if (!string.IsNullOrEmpty(direction))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(direction);
            }
            return sb.Length > 0 ? sb.ToString() : "Selector";
        }
        catch
        {
            return "Selector";
        }
    }
}
