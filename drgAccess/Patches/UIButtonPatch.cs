using HarmonyLib;
using DRS.UI;
using Assets.Scripts.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine;
using System.Text;

namespace drgAccess.Patches;

/// <summary>
/// Patches for UIButton to announce button text when selected.
/// Handles all specialized buttons with tooltip data.
/// </summary>
[HarmonyPatch(typeof(UIButton))]
public static class UIButtonPatch
{
    [HarmonyPatch(nameof(UIButton.OnSelect))]
    [HarmonyPostfix]
    public static void OnSelect_Postfix(UIButton __instance, BaseEventData bed)
    {
        try
        {
            string buttonText = GetButtonText(__instance);
            if (!string.IsNullOrEmpty(buttonText))
            {
                ScreenReader.Interrupt(buttonText);
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

        // UIClassArtifactButton - subclass selection (Classic, Sniper, Tracker, etc.)
        var classArtifactButton = button.TryCast<UIClassArtifactButton>();
        if (classArtifactButton != null)
        {
            return GetClassArtifactButtonText(classArtifactButton);
        }

        // UISkillButton - level-up upgrades and weapons
        var skillButton = button.TryCast<UISkillButton>();
        if (skillButton != null)
        {
            return GetSkillButtonText(skillButton);
        }

        // UIClassSelectButton - class selection (Driller, Engineer, etc.)
        var classButton = button.TryCast<UIClassSelectButton>();
        if (classButton != null)
        {
            return GetClassButtonText(classButton);
        }

        // UIMutatorView - hazard modifiers
        var mutatorView = button.TryCast<UIMutatorView>();
        if (mutatorView != null)
        {
            return GetMutatorViewText(mutatorView);
        }

        // UIBiomeSelectButton - biome selection
        var biomeButton = button.TryCast<UIBiomeSelectButton>();
        if (biomeButton != null)
        {
            return GetBiomeButtonText(biomeButton);
        }

        // UIShopButton - shop items
        var shopButton = button.TryCast<UIShopButton>();
        if (shopButton != null)
        {
            return GetShopButtonText(shopButton);
        }

        // UIHazLevelButton - hazard level selection
        var hazLevelButton = button.TryCast<UIHazLevelButton>();
        if (hazLevelButton != null)
        {
            return GetHazLevelButtonText(hazLevelButton);
        }

        // UISliderToggle - toggle settings
        var sliderToggle = button.TryCast<UISliderToggle>();
        if (sliderToggle != null)
        {
            return GetSliderToggleText(sliderToggle);
        }

        // Mission buttons
        var missionRoadButton = button.TryCast<UIMissionRoadButton>();
        if (missionRoadButton != null)
        {
            return GetMissionRoadButtonText(missionRoadButton);
        }

        var missionSectorButton = button.TryCast<UIMissionSectorButton>();
        if (missionSectorButton != null)
        {
            return GetMissionSectorButtonText(missionSectorButton);
        }

        var missionNodeButton = button.TryCast<UIMissionNodeButton>();
        if (missionNodeButton != null)
        {
            return GetMissionNodeButtonText(missionNodeButton);
        }

        var missionGateButton = button.TryCast<UIMissionGateButton>();
        if (missionGateButton != null)
        {
            return GetMissionGateButtonText(missionGateButton);
        }

        // Campaign/Challenge buttons
        var campaignSetButton = button.TryCast<UICampaignSetButton>();
        if (campaignSetButton != null)
        {
            return GetCampaignSetButtonText(campaignSetButton);
        }

        var challengeButton = button.TryCast<UIChallengeButton>();
        if (challengeButton != null)
        {
            return GetChallengeButtonText(challengeButton);
        }

        var challengeSetButton = button.TryCast<UIChallengeSetButton>();
        if (challengeSetButton != null)
        {
            return GetChallengeSetButtonText(challengeSetButton);
        }

        // Fixed run button
        var fixedRunButton = button.TryCast<UIFixedRunButton>();
        if (fixedRunButton != null)
        {
            return GetFixedRunButtonText(fixedRunButton);
        }

        // Stat upgrade button
        var statUpgradeButton = button.TryCast<UIStatUpgradeButton>();
        if (statUpgradeButton != null)
        {
            return GetStatUpgradeButtonText(statUpgradeButton);
        }

        // Mutator button (not UIMutatorView)
        var mutatorButton = button.TryCast<UIMutatorButton>();
        if (mutatorButton != null)
        {
            return GetMutatorButtonText(mutatorButton);
        }

        // Mineral market button
        var mineralMarketButton = button.TryCast<UIMineralMarketButton>();
        if (mineralMarketButton != null)
        {
            return GetMineralMarketButtonText(mineralMarketButton);
        }

        // Set progress button
        var setProgressButton = button.TryCast<UISetProgressButton>();
        if (setProgressButton != null)
        {
            return GetSetProgressButtonText(setProgressButton);
        }

        // Skin overrides button
        var skinOverridesButton = button.TryCast<UISkinOverridesButton>();
        if (skinOverridesButton != null)
        {
            return GetSkinOverridesButtonText(skinOverridesButton);
        }

        // Gear view compact
        var gearViewCompact = button.TryCast<UIGearViewCompact>();
        if (gearViewCompact != null)
        {
            return GetGearViewCompactText(gearViewCompact);
        }

        // Default: try to get text from buttonText field
        return GetDefaultButtonText(button);
    }

    private static string GetClassArtifactButtonText(UIClassArtifactButton artifactButton)
    {
        try
        {
            var artifactData = artifactButton.data;
            if (artifactData != null)
            {
                string title = artifactData.Title;

                if (!string.IsNullOrEmpty(title))
                {
                    string result = CleanText(title);

                    // Check if locked
                    bool isLocked = !artifactButton.isUnlocked;
                    if (isLocked)
                    {
                        int unlockValue = artifactButton.classUnlockValue;
                        if (unlockValue > 0)
                        {
                            result += $", Locked. Requires {unlockValue} class completions to unlock";
                        }
                        else
                        {
                            result += ", Locked";
                        }
                    }
                    else
                    {
                        // Get full description with stats using GetDescription and GetStatDescription
                        string description = artifactData.GetDescription(ERarity.COMMON);
                        string statDescription = artifactData.GetStatDescription(ERarity.COMMON);

                        string cleanStatDesc = !string.IsNullOrEmpty(statDescription) ? CleanText(statDescription) : "";
                        string cleanDesc = !string.IsNullOrEmpty(description) ? CleanText(description) : "";

                        // Add stat description first
                        if (!string.IsNullOrEmpty(cleanStatDesc))
                        {
                            result += ". " + cleanStatDesc;
                        }

                        // Only add description if it's different from stat description (avoid duplicates)
                        if (!string.IsNullOrEmpty(cleanDesc) && cleanDesc != cleanStatDesc)
                        {
                            result += ". " + cleanDesc;
                        }

                        // Add starter weapon information
                        string weaponInfo = GetWeaponInfo(artifactData.StarterWeapon);
                        if (!string.IsNullOrEmpty(weaponInfo))
                        {
                            result += ". Weapon: " + weaponInfo;
                        }
                    }

                    return result;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetClassArtifactButtonText error: {ex.Message}");
        }

        return null;
    }

    private static string GetWeaponInfo(WeaponSkillData weapon)
    {
        if (weapon == null)
            return null;

        try
        {
            StringBuilder sb = new StringBuilder();

            // Weapon name
            string weaponTitle = weapon.Title;
            if (!string.IsNullOrEmpty(weaponTitle))
            {
                sb.Append(CleanText(weaponTitle));
            }

            // Weapon description
            string weaponDesc = weapon.GetDescription(ERarity.COMMON);
            if (!string.IsNullOrEmpty(weaponDesc))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(weaponDesc));
            }

            // Weapon stats (damage, range, etc.)
            string weaponStats = weapon.GetStatDescription(ERarity.COMMON);
            if (!string.IsNullOrEmpty(weaponStats))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(weaponStats));
            }

            // Target type (attacks closest enemy, etc.)
            string targetText = GetTargetTypeText(weapon.TargetType);
            if (!string.IsNullOrEmpty(targetText))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(targetText);
            }

            // Damage type
            string damageTypeText = GetDamageTypeText(weapon.damageType);
            if (!string.IsNullOrEmpty(damageTypeText))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append("Damage type: " + damageTypeText);
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetWeaponInfo error: {ex.Message}");
            return null;
        }
    }

    private static string GetTargetTypeText(EProjectileTargetType targetType)
    {
        return targetType switch
        {
            EProjectileTargetType.CLOSEST => "Attacks the closest enemy",
            EProjectileTargetType.HIGHEST_HP => "Attacks the highest HP enemy",
            EProjectileTargetType.AI => "AI controlled targeting",
            EProjectileTargetType.PLAYER_FORWARD => "Fires forward",
            EProjectileTargetType.PLAYER_BACKWARD => "Fires backward",
            EProjectileTargetType.WORLD_DIR => "Fixed direction",
            EProjectileTargetType.AVERAGE_DIR => "Average direction",
            EProjectileTargetType.EXTERNAL_AIM => "External aim",
            _ => null
        };
    }

    private static string GetDamageTypeText(EDamageType damageType)
    {
        return damageType switch
        {
            EDamageType.KINETIC => "Kinetic",
            EDamageType.CRYO => "Cryo",
            EDamageType.FIRE => "Fire",
            EDamageType.ACID => "Acid",
            EDamageType.PLASMA => "Plasma",
            EDamageType.ELECTRICAL => "Electrical",
            _ => damageType.ToString()
        };
    }

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
                    string result = CleanText(title);

                    // Get stat description with rarity (contains HP, damage, speed, etc.)
                    string statDescription = skillData.GetStatDescription(rarity);
                    if (!string.IsNullOrEmpty(statDescription))
                    {
                        result += ". " + CleanText(statDescription);
                    }

                    // Get general description with rarity
                    string description = skillData.GetDescription(rarity);
                    if (!string.IsNullOrEmpty(description))
                    {
                        result += ". " + CleanText(description);
                    }

                    return result;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetSkillButtonText error: {ex.Message}");
        }

        return null;
    }

    private static string GetClassButtonText(UIClassSelectButton classButton)
    {
        try
        {
            var classData = classButton.classData;
            if (classData != null)
            {
                string displayName = classData.DisplayName;
                string description = classData.Description;

                if (!string.IsNullOrEmpty(displayName))
                {
                    string result = CleanText(displayName);

                    // Check if class is locked
                    bool isLocked = classButton.lockedGroup != null &&
                                   classButton.lockedGroup.activeSelf;

                    if (isLocked)
                    {
                        result += ", Locked";
                    }
                    else
                    {
                        // Description first
                        if (!string.IsNullOrEmpty(description))
                        {
                            result += ". " + CleanText(description);
                        }

                        // Then base stats (HP, Evasion, Crit chance, Crit damage)
                        string statsText = GetBaseStatsText(classData.BaseStats);
                        if (!string.IsNullOrEmpty(statsText))
                        {
                            result += ". " + statsText;
                        }
                    }

                    return result;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetClassButtonText error: {ex.Message}");
        }

        return null;
    }

    private static string GetBaseStatsText(BaseStats baseStats)
    {
        if (baseStats == null)
            return null;

        try
        {
            var mods = baseStats.mods;
            if (mods == null || mods.Length == 0)
                return null;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < mods.Length; i++)
            {
                var mod = mods[i];
                if (mod == null)
                    continue;

                string statName = GetStatTypeName(mod.statType);
                float value = mod.value;

                if (sb.Length > 0)
                    sb.Append(", ");

                // Format value based on stat type
                if (IsPercentageStat(mod.statType))
                {
                    sb.Append($"{statName}: {value:0}%");
                }
                else
                {
                    sb.Append($"{statName}: {value:0}");
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetBaseStatsText error: {ex.Message}");
            return null;
        }
    }

    private static string GetStatTypeName(EStatType statType)
    {
        return statType switch
        {
            EStatType.MAX_HP => "Max HP",
            EStatType.ARMOR => "Armor",
            EStatType.DODGE => "Evasion",
            EStatType.MOVESPEED => "Move Speed",
            EStatType.FIRE_RATE => "Fire Rate",
            EStatType.CRIT_CHANCE => "Crit Chance",
            EStatType.CRIT_DAMAGE => "Crit Damage",
            EStatType.PIERCING => "Piercing",
            EStatType.LIFE_REGEN => "Life Regen",
            EStatType.PICKUP_RADIUS => "Pickup Radius",
            EStatType.STATUS_EFFECT_DAMAGE => "Status Effect Damage",
            EStatType.DAMAGE => "Damage",
            EStatType.MINING_SPEED => "Mining Speed",
            EStatType.RELOAD_SPEED => "Reload Speed",
            EStatType.LIFETIME => "Lifetime",
            EStatType.CLIP_SIZE => "Clip Size",
            EStatType.WEAPON_RANGE => "Weapon Range",
            EStatType.EXPLOSION_RADIUS => "Explosion Radius",
            EStatType.LUCK => "Luck",
            EStatType.XP_GAIN => "XP Gain",
            EStatType.BEAM_COUNT => "Beam Count",
            EStatType.DRONE_COUNT => "Drone Count",
            EStatType.TURRET_COUNT => "Turret Count",
            EStatType.POTENCY => "Potency",
            _ => statType.ToString()
        };
    }

    private static bool IsPercentageStat(EStatType statType)
    {
        return statType switch
        {
            EStatType.MAX_HP => false,
            EStatType.ARMOR => false,
            EStatType.BEAM_COUNT => false,
            EStatType.DRONE_COUNT => false,
            EStatType.TURRET_COUNT => false,
            EStatType.CLIP_SIZE => false,
            _ => true
        };
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
                    string result = CleanText(title);
                    if (!string.IsNullOrEmpty(description))
                    {
                        result += ". " + CleanText(description);
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
            var biomeLevelData = biomeButton.biomeLevelData;
            if (biomeLevelData != null)
            {
                var biomeData = biomeLevelData.BiomeData;
                if (biomeData != null)
                {
                    string displayName = biomeData.DisplayName;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        string result = CleanText(displayName);

                        // Check if locked
                        bool isLocked = biomeButton.lockedGroup != null &&
                                       biomeButton.lockedGroup.activeSelf;

                        if (isLocked)
                        {
                            result += ", Locked";
                        }

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
            var skillData = shopButton.skillData;
            if (skillData != null)
            {
                string title = skillData.Title;

                if (!string.IsNullOrEmpty(title))
                {
                    string result = CleanText(title);

                    // Get stat description (HP, damage, etc.)
                    string statDescription = skillData.GetStatDescription(ERarity.COMMON);
                    if (!string.IsNullOrEmpty(statDescription))
                    {
                        result += ". " + CleanText(statDescription);
                    }

                    // Get general description
                    string description = skillData.GetDescription(ERarity.COMMON);
                    if (!string.IsNullOrEmpty(description))
                    {
                        result += ". " + CleanText(description);
                    }

                    return result;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetShopButtonText error: {ex.Message}");
        }

        return null;
    }

    private static string GetHazLevelButtonText(UIHazLevelButton hazLevelButton)
    {
        try
        {
            // Try to get hazard level text
            var hazLevelText = hazLevelButton.hazLevelText;
            if (hazLevelText != null && !string.IsNullOrEmpty(hazLevelText.text))
            {
                string result = CleanText(hazLevelText.text);

                // Check if locked
                bool isLocked = hazLevelButton.lockedGroup != null &&
                               hazLevelButton.lockedGroup.activeSelf;

                if (isLocked)
                {
                    result += ", Locked";
                }

                return result;
            }

            // Fallback to hazard level number
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
                labelText = CleanText(textComponent.text);
            }
            else
            {
                TextMeshProUGUI childText = sliderToggle.GetComponentInChildren<TextMeshProUGUI>();
                if (childText != null && !string.IsNullOrEmpty(childText.text))
                {
                    labelText = CleanText(childText.text);
                }
                else
                {
                    labelText = sliderToggle.gameObject.name;
                }
            }

            bool isToggled = sliderToggle.IsToggled;
            string state = isToggled ? "On" : "Off";

            return $"{labelText}, {state}";
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetSliderToggleText error: {ex.Message}");
        }

        return null;
    }

    private static string GetMissionRoadButtonText(UIMissionRoadButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var nameText = button.nameText;
            if (nameText != null && !string.IsNullOrEmpty(nameText.text))
                sb.Append(CleanText(nameText.text));

            var goalText = button.goalCounterText;
            if (goalText != null && !string.IsNullOrEmpty(goalText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(goalText.text));
            }

            if (!button.isUnlocked)
            {
                var unlockText = button.unlockConditionText;
                if (unlockText != null && !string.IsNullOrEmpty(unlockText.text))
                {
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append("Locked. " + CleanText(unlockText.text));
                }
                else
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append("Locked");
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
            sb.Append($"Sector {button.sectorNumber}");

            var goalText = button.biomeGoalCountText;
            if (goalText != null && !string.IsNullOrEmpty(goalText.text))
            {
                sb.Append(". " + CleanText(goalText.text));
            }

            if (button.isLocked)
            {
                sb.Append(", Locked");
            }
            else if (button.nodeCompleted)
            {
                sb.Append(", Completed");
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
            sb.Append("Mission Node");

            var reqText = button.biomeGoalRequirementText;
            if (reqText != null && !string.IsNullOrEmpty(reqText.text))
            {
                sb.Append(". " + CleanText(reqText.text));
            }

            if (button.isLocked)
            {
                sb.Append(", Locked");
            }
            else if (button.nodeCompleted)
            {
                sb.Append(", Completed");
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetMissionNodeButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetMissionGateButtonText(UIMissionGateButton button)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"Gate {button.gateNumber}");

            if (button.isLocked)
            {
                sb.Append(", Locked");
            }
            else if (button.nodeCompleted)
            {
                sb.Append(", Completed");
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
                sb.Append(CleanText(nameText.text));

            var progressText = button.progressText;
            if (progressText != null && !string.IsNullOrEmpty(progressText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(progressText.text));
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
                    sb.Append(CleanText(title));

                string desc = challengeData.GetPreDescription();
                if (!string.IsNullOrEmpty(desc))
                {
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(CleanText(desc));
                }
            }

            var progressText = button.progressText;
            if (progressText != null && !string.IsNullOrEmpty(progressText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(progressText.text));
            }

            bool isCompleted = button.completedGroup != null && button.completedGroup.activeSelf;
            if (isCompleted)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("Completed");
            }
            else
            {
                bool isLocked = button.lockedGroup != null && button.lockedGroup.activeSelf;
                if (isLocked)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append("Locked");
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
                sb.Append(CleanText(nameText.text));

            var progressText = button.progressText;
            if (progressText != null && !string.IsNullOrEmpty(progressText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(progressText.text));
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
            sb.Append("Fixed Run");

            var counterText = button.completedCounter;
            if (counterText != null && !string.IsNullOrEmpty(counterText.text))
            {
                sb.Append(". " + CleanText(counterText.text));
            }

            if (button.isLocked)
            {
                sb.Append(", Locked");
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetFixedRunButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetStatUpgradeButtonText(UIStatUpgradeButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var data = button.data;
            if (data != null)
            {
                string title = data.Title;
                if (!string.IsNullOrEmpty(title))
                    sb.Append(CleanText(title));
            }

            int level = button.currentLevel;
            sb.Append($", Level {level}");

            if (!button.canAfford)
            {
                sb.Append(", Cannot afford");
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetStatUpgradeButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetMutatorButtonText(UIMutatorButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var titleText = button.title;
            if (titleText != null && !string.IsNullOrEmpty(titleText.text))
                sb.Append(CleanText(titleText.text));

            var descText = button.description;
            if (descText != null && !string.IsNullOrEmpty(descText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(descText.text));
            }

            var pointsText = button.pointsText;
            if (pointsText != null && !string.IsNullOrEmpty(pointsText.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(CleanText(pointsText.text) + " points");
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
            sb.Append(button.material.ToString());

            string action = button.marketAction.ToString();
            sb.Append($", {action}");

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
                return CleanText(progressText.text);
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
                return CleanText(titleText.text);

            var streamerTitle = button.streamerTitle;
            if (streamerTitle != null && !string.IsNullOrEmpty(streamerTitle.text))
                return CleanText(streamerTitle.text);

            return null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetSkinOverridesButtonText error: {ex.Message}");
            return null;
        }
    }

    private static string GetGearViewCompactText(UIGearViewCompact button)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("Gear");

            var tierText = button.tierText;
            if (tierText != null && !string.IsNullOrEmpty(tierText.text))
            {
                sb.Append(" Tier " + CleanText(tierText.text));
            }

            if (button.isFavorite != null && button.isFavorite.activeSelf)
            {
                sb.Append(", Favorite");
            }

            if (button.isNew != null && button.isNew.activeSelf)
            {
                sb.Append(", New");
            }

            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetGearViewCompactText error: {ex.Message}");
            return null;
        }
    }

    private static string GetDefaultButtonText(UIButton button)
    {
        // Try to get text from buttonText field
        TextMeshProUGUI textComponent = button.buttonText;
        if (textComponent != null)
        {
            string text = textComponent.text;
            if (!string.IsNullOrEmpty(text))
                return CleanText(text);
        }

        // Fallback: try to find TextMeshProUGUI in children
        TextMeshProUGUI childText = button.GetComponentInChildren<TextMeshProUGUI>();
        if (childText != null)
        {
            string text = childText.text;
            if (!string.IsNullOrEmpty(text))
                return CleanText(text);
        }

        // Last resort: use GameObject name
        return CleanText(button.gameObject.name);
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
                // Skip the buttons themselves
                if (selector.leftButton != null && child.gameObject == selector.leftButton.gameObject)
                    continue;
                if (selector.rightButton != null && child.gameObject == selector.rightButton.gameObject)
                    continue;

                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp == null)
                    tmp = child.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                {
                    value = CleanText(tmp.text);
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

    private static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove rich text tags
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");

        // Clean up whitespace
        text = text.Trim();

        return text;
    }
}
