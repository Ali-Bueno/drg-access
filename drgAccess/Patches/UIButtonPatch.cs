using HarmonyLib;
using DRS.UI;
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
    private static string _lastAnnouncedText = "";

    [HarmonyPatch(nameof(UIButton.OnSelect))]
    [HarmonyPostfix]
    public static void OnSelect_Postfix(UIButton __instance, BaseEventData bed)
    {
        try
        {
            string buttonText = GetButtonText(__instance);
            if (!string.IsNullOrEmpty(buttonText) && buttonText != _lastAnnouncedText)
            {
                _lastAnnouncedText = buttonText;
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
