using Assets.Scripts.Data;
using DRS.UI;
using System.Text;
using drgAccess.Helpers;

namespace drgAccess.Patches;

// Class and subclass selection button text extraction
public static partial class UIButtonPatch
{
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
                    string result = TextHelper.CleanText(displayName);

                    // Check if class is locked
                    bool isLocked = classButton.lockedGroup != null &&
                                   classButton.lockedGroup.activeSelf;

                    if (isLocked)
                    {
                        result += ", " + ModLocalization.Get("ui_locked");

                        // Find unlock rank requirement from the page
                        int unlockRank = GetClassUnlockRank(classButton.dwarf);
                        if (unlockRank > 0)
                        {
                            result += ". " + ModLocalization.Get("class_unlock_rank", unlockRank);
                        }
                    }
                    else
                    {
                        // Description first
                        if (!string.IsNullOrEmpty(description))
                        {
                            result += ". " + TextHelper.CleanText(description);
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

    private static UIClassSelectPage cachedClassPage;
    private static bool classPageSearched = false;

    private static int GetClassUnlockRank(EDwarf dwarf)
    {
        try
        {
            if (cachedClassPage == null && !classPageSearched)
            {
                classPageSearched = true;
                var pages = UnityEngine.Resources.FindObjectsOfTypeAll<UIClassSelectPage>();
                if (pages != null && pages.Count > 0)
                    cachedClassPage = pages[0];
            }

            if (cachedClassPage == null)
                return 0;

            return dwarf switch
            {
                EDwarf.DRILLER => cachedClassPage.drillerUnlocksAtRank,
                EDwarf.ENGINEER => cachedClassPage.engineerUnlocksAtRank,
                EDwarf.GUNNER => cachedClassPage.gunnerUnlocksAtRank,
                _ => 0
            };
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogDebug($"UIButtonPatch.GetClassUnlockRank error: {ex.Message}");
            return 0;
        }
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
                    string result = TextHelper.CleanText(title);

                    // Check if locked
                    bool isLocked = !artifactButton.isUnlocked;
                    if (isLocked)
                    {
                        int unlockValue = artifactButton.classUnlockValue;
                        if (unlockValue > 0)
                        {
                            result += ", " + ModLocalization.Get("class_unlock_requirement", unlockValue);
                        }
                        else
                        {
                            result += ", " + ModLocalization.Get("ui_locked");
                        }
                    }
                    else
                    {
                        // Get full description with stats using GetDescription and GetStatDescription
                        string description = artifactData.GetDescription(ERarity.COMMON);
                        string statDescription = artifactData.GetStatDescription(ERarity.COMMON);

                        string cleanStatDesc = !string.IsNullOrEmpty(statDescription) ? TextHelper.CleanText(statDescription) : "";
                        string cleanDesc = !string.IsNullOrEmpty(description) ? TextHelper.CleanText(description) : "";

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
                            result += ". " + ModLocalization.Get("class_weapon", weaponInfo);
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

            string weaponTitle = weapon.Title;
            if (!string.IsNullOrEmpty(weaponTitle))
            {
                sb.Append(TextHelper.CleanText(weaponTitle));
            }

            string weaponDesc = weapon.GetDescription(ERarity.COMMON);
            if (!string.IsNullOrEmpty(weaponDesc))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(weaponDesc));
            }

            string weaponStats = weapon.GetStatDescription(ERarity.COMMON);
            if (!string.IsNullOrEmpty(weaponStats))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(weaponStats));
            }

            string targetText = LocalizationHelper.GetTargetTypeText(weapon.TargetType);
            if (!string.IsNullOrEmpty(targetText))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(targetText);
            }

            string damageTypeText = LocalizationHelper.GetDamageTypeText(weapon.damageType);
            if (!string.IsNullOrEmpty(damageTypeText))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(ModLocalization.Get("class_damage_type", damageTypeText));
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetWeaponInfo error: {ex.Message}");
            return null;
        }
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

                string statName = LocalizationHelper.GetStatTypeName(mod.statType);

                if (sb.Length > 0)
                    sb.Append(", ");

                sb.Append($"{statName}: {LocalizationHelper.FormatStatValue(mod.statType, mod.value)}");
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetBaseStatsText error: {ex.Message}");
            return null;
        }
    }
}
