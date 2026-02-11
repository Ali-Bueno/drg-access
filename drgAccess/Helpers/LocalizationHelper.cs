using Assets.Scripts.Data;
using UnityEngine;
using UnityEngine.Localization;

namespace drgAccess.Helpers;

/// <summary>
/// Cached localization lookups for stat names, rarity, gear slots, and enum text.
/// Uses the game's own localization system with English fallbacks.
/// </summary>
public static class LocalizationHelper
{
    // Cached ScriptableObject singletons (searched once)
    private static StatSettingCollection _statSettings;
    private static UiRarityData _rarityData;
    private static LocalizedResources _localizedResources;
    private static bool _statSettingsSearched;
    private static bool _rarityDataSearched;
    private static bool _localizedResourcesSearched;

    private static StatSettingCollection GetStatSettings()
    {
        if (_statSettings != null) return _statSettings;
        if (_statSettingsSearched) return null;
        _statSettingsSearched = true;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<StatSettingCollection>();
            if (all != null && all.Length > 0)
                _statSettings = all[0];
        }
        catch { /* Not available */ }
        return _statSettings;
    }

    private static UiRarityData GetRarityData()
    {
        if (_rarityData != null) return _rarityData;
        if (_rarityDataSearched) return null;
        _rarityDataSearched = true;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<UiRarityData>();
            if (all != null && all.Length > 0)
                _rarityData = all[0];
        }
        catch { /* Not available */ }
        return _rarityData;
    }

    private static LocalizedResources GetLocalizedResources()
    {
        if (_localizedResources != null) return _localizedResources;
        if (_localizedResourcesSearched) return null;
        _localizedResourcesSearched = true;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<LocalizedResources>();
            if (all != null && all.Length > 0)
                _localizedResources = all[0];
        }
        catch { /* Not available */ }
        return _localizedResources;
    }

    // === Stat type names ===

    public static string GetStatTypeName(EStatType statType)
    {
        // Try localized name from game's StatSettingCollection
        try
        {
            var settings = GetStatSettings();
            if (settings != null)
            {
                var stat = settings.Get(statType);
                if (stat != null)
                {
                    string name = stat.GetDisplayName;
                    if (!string.IsNullOrEmpty(name))
                        return TextHelper.CleanText(name);
                }
            }
        }
        catch { /* Fall back to English */ }

        // English fallback
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

    public static bool IsPercentageStat(EStatType statType)
    {
        // Try to get from game's StatSetting data
        try
        {
            var settings = GetStatSettings();
            if (settings != null)
            {
                var stat = settings.Get(statType);
                if (stat != null)
                    return stat.IsPercentage;
            }
        }
        catch { /* Fall back to hardcoded */ }

        // Fallback
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

    /// <summary>
    /// Formats a stat value for display. Percentage stats are stored as fractions
    /// (0.05 = 5%) so they need to be multiplied by 100.
    /// </summary>
    public static string FormatStatValue(EStatType statType, float value, bool showSign = false)
    {
        string formatted;
        if (IsPercentageStat(statType))
        {
            float display = value * 100f;
            formatted = showSign ? $"{display:+0;-0}%" : $"{display:0}%";
        }
        else
        {
            formatted = showSign ? $"{value:+0;-0}" : $"{value:0}";
        }
        return formatted;
    }

    // === Rarity names ===

    public static string GetRarityText(ERarity rarity)
    {
        // Try localized name from game's UiRarityData
        try
        {
            var rarityData = GetRarityData();
            if (rarityData != null)
            {
                string name = rarityData.GetRarityName(rarity);
                if (!string.IsNullOrEmpty(name))
                    return TextHelper.CleanText(name);
            }
        }
        catch { /* Fall back to English */ }

        // English fallback
        return rarity switch
        {
            ERarity.COMMON => "Common",
            ERarity.UNCOMMON => "Uncommon",
            ERarity.RARE => "Rare",
            ERarity.EPIC => "Epic",
            ERarity.LEGENDARY => "Legendary",
            _ => rarity.ToString()
        };
    }

    // === Gear slot type names ===

    public static string GetGearSlotTypeText(EGearSlotType slotType)
    {
        // Try localized name from game's LocalizedResources
        try
        {
            var locRes = GetLocalizedResources();
            if (locRes != null)
            {
                LocalizedString locStr = slotType switch
                {
                    EGearSlotType.TOOL => locRes.GearTypeTool,
                    EGearSlotType.ARMOR => locRes.GearTypeArmor,
                    EGearSlotType.GRINDER => locRes.GearTypeGrinder,
                    EGearSlotType.WEAPON_MOD => locRes.GearTypeWeaponMod,
                    EGearSlotType.TANK => locRes.GearTypeTank,
                    EGearSlotType.COMPANION => locRes.GearTypeCompanion,
                    _ => null
                };
                if (locStr != null)
                {
                    string name = locStr.GetLocalizedString();
                    if (!string.IsNullOrEmpty(name))
                        return TextHelper.CleanText(name);
                }
            }
        }
        catch { /* Fall back to English */ }

        // English fallback
        return slotType switch
        {
            EGearSlotType.TOOL => "Tool",
            EGearSlotType.ARMOR => "Armor",
            EGearSlotType.GRINDER => "Grinder",
            EGearSlotType.WEAPON_MOD => "Weapon Mod",
            EGearSlotType.TANK => "Tank",
            EGearSlotType.COMPANION => "Companion",
            _ => slotType.ToString()
        };
    }

    // === Currency names ===

    public static string GetCurrencyName(ECurrency currency)
    {
        try
        {
            var locRes = GetLocalizedResources();
            if (locRes != null)
            {
                LocalizedString locStr = currency switch
                {
                    ECurrency.GOLD => locRes.LocGold,
                    ECurrency.CREDITS => locRes.LocCredits,
                    ECurrency.MORKITE => locRes.LocMorkite,
                    ECurrency.NITRA => locRes.LocNitra,
                    ECurrency.RED_SUGAR => locRes.LocRedSugar,
                    ECurrency.BISMOR => locRes.LocBismor,
                    ECurrency.CROPPA => locRes.LocCroppa,
                    ECurrency.ENOR_PEARL => locRes.LocEnorPearl,
                    ECurrency.JADIZ => locRes.LocJadiz,
                    ECurrency.MAGNITE => locRes.LocMagnite,
                    ECurrency.UMANITE => locRes.LocUmanite,
                    ECurrency.POWER_CORE => locRes.LocPowerCore,
                    ECurrency.ARTIFACT_REROLL => locRes.LocArtifactReroll,
                    ECurrency.MUTATOR_REROLL => locRes.LocMutatorReroll,
                    ECurrency.OMMORAN_CORE => locRes.LocOmmoranCore,
                    ECurrency.BOBBY_FUEL => locRes.LocBobbyFuel,
                    ECurrency.BOBBY_PART => locRes.LocBobbyPart,
                    _ => null
                };
                if (locStr != null)
                {
                    string name = locStr.GetLocalizedString();
                    if (!string.IsNullOrEmpty(name))
                        return TextHelper.CleanText(name);
                }
            }
        }
        catch { /* Fall back to English */ }

        return currency switch
        {
            ECurrency.GOLD => "Gold",
            ECurrency.CREDITS => "Credits",
            ECurrency.MORKITE => "Morkite",
            ECurrency.NITRA => "Nitra",
            ECurrency.RED_SUGAR => "Red Sugar",
            ECurrency.BISMOR => "Bismor",
            ECurrency.CROPPA => "Croppa",
            ECurrency.ENOR_PEARL => "Enor Pearl",
            ECurrency.JADIZ => "Jadiz",
            ECurrency.MAGNITE => "Magnite",
            ECurrency.UMANITE => "Umanite",
            ECurrency.POWER_CORE => "Power Core",
            ECurrency.ARTIFACT_REROLL => "Artifact Reroll",
            ECurrency.MUTATOR_REROLL => "Mutator Reroll",
            ECurrency.OMMORAN_CORE => "Ommoran Core",
            ECurrency.BOBBY_FUEL => "Bobby Fuel",
            ECurrency.BOBBY_PART => "Bobby Part",
            _ => currency.ToString()
        };
    }

    // === Projectile target type ===

    public static string GetTargetTypeText(EProjectileTargetType targetType)
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

    // === Damage type ===

    public static string GetDamageTypeText(EDamageType damageType)
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
}
