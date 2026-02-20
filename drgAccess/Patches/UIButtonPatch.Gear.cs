using Assets.Scripts.Data;
using DRS.UI;
using Il2CppSystem.Collections.Generic;
using System.Text;
using drgAccess.Helpers;

namespace drgAccess.Patches;

// Gear inventory and stat upgrade button text extraction
public static partial class UIButtonPatch
{
    // Cached from UIGearInventoryForm when it opens
    internal static GearEconomyConfig CachedGearEconomy;
    internal static Wallet CachedGearWallet;
    internal static GearManager CachedGearManager;

    private static string GetGearViewCompactText(UIGearViewCompact button)
    {
        try
        {
            var sb = new StringBuilder();

            // Read gear name from data
            var gear = button.gear;
            if (gear != null)
            {
                var gearData = gear.Data;
                if (gearData != null)
                {
                    string gearName = gearData.GetTitle();
                    if (!string.IsNullOrEmpty(gearName))
                        sb.Append(TextHelper.CleanText(gearName));

                    // Slot type
                    string slotType = LocalizationHelper.GetGearSlotTypeText(gearData.SlotType);
                    if (!string.IsNullOrEmpty(slotType))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(slotType);
                    }
                }

                // Equipped status (announced early so user knows before stats/costs)
                if (CachedGearManager != null)
                {
                    try { if (CachedGearManager.IsGearEquippedOnAny(gear)) sb.Append(", Equipped"); }
                    catch { }
                }

                // Rarity
                string rarity = LocalizationHelper.GetRarityText(gear.Rarity);
                if (!string.IsNullOrEmpty(rarity))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(rarity);
                }
            }

            if (sb.Length == 0)
                sb.Append("Gear");

            // Tier
            var tierText = button.tierText;
            if (tierText != null && !string.IsNullOrEmpty(tierText.text))
            {
                sb.Append(", Tier " + TextHelper.CleanText(tierText.text));
            }

            // Stats from gear view
            if (gear != null)
            {
                try
                {
                    var statMods = gear.StatMods;
                    if (statMods != null && statMods.Count > 0)
                    {
                        sb.Append(". Stats: ");
                        for (int i = 0; i < statMods.Count; i++)
                        {
                            var mod = statMods[i];
                            if (mod == null) continue;
                            if (i > 0) sb.Append(", ");
                            string statName = LocalizationHelper.GetStatTypeName(mod.StatType);
                            sb.Append($"{statName}: {LocalizationHelper.FormatStatValue(mod.StatType, mod.value, showSign: true)}");
                        }
                    }
                }
                catch { /* StatMods not available */ }

                // Quirk description
                try
                {
                    var gearData = gear.Data;
                    if (gearData != null)
                    {
                        string quirkDesc = gearData.GetQuirkDesc(gear);
                        if (!string.IsNullOrEmpty(quirkDesc))
                        {
                            sb.Append(". " + TextHelper.CleanText(quirkDesc));
                        }
                    }
                }
                catch { /* Quirk description not available */ }
            }

            // Upgrade cost (when in upgrade tab)
            if (button.upgradeIndicator != null && button.upgradeIndicator.activeSelf)
            {
                try
                {
                    if (CachedGearEconomy != null && gear != null
                        && CachedGearEconomy.TryGetUpgradeCost(gear, out List<CurrencyValue> costs)
                        && costs != null && costs.Count > 0)
                    {
                        sb.Append(". Upgrade cost: ");
                        sb.Append(FormatCurrencyList(costs));
                        if (CachedGearWallet != null && !CachedGearWallet.CanAfford(costs))
                            sb.Append(", Cannot afford");
                    }
                    else
                    {
                        sb.Append(", Upgrade available");
                    }
                }
                catch { sb.Append(", Upgrade available"); }
            }

            // Salvage value (when in sell tab)
            if (button.sellIndicator != null && button.sellIndicator.activeSelf)
            {
                try
                {
                    if (CachedGearEconomy != null && gear != null
                        && CachedGearEconomy.TryGetSalvageValue(gear, out List<CurrencyValue> values)
                        && values != null && values.Count > 0)
                    {
                        sb.Append(". Sell value: ");
                        sb.Append(FormatCurrencyList(values));
                    }
                    else
                    {
                        sb.Append(", Sell available");
                    }
                }
                catch { sb.Append(", Sell available"); }
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

    private static string GetStatUpgradeButtonText(UIStatUpgradeButton button)
    {
        try
        {
            var sb = new StringBuilder();
            var data = button.data;
            if (data != null)
            {
                // Use localized title, fall back to Title property
                string title = null;
                try
                {
                    var locTitle = data.LocTitle;
                    if (locTitle != null)
                        title = locTitle.GetLocalizedString();
                }
                catch { /* Localized title not available */ }
                if (string.IsNullOrEmpty(title))
                    title = data.Title;
                if (!string.IsNullOrEmpty(title))
                    sb.Append(TextHelper.CleanText(title));

                // Try to get localized description
                try
                {
                    var locDesc = data.LocDescription;
                    if (locDesc != null)
                    {
                        string desc = locDesc.GetLocalizedString();
                        if (!string.IsNullOrEmpty(desc))
                            sb.Append(". " + TextHelper.CleanText(desc));
                    }
                }
                catch { /* Localized description not available */ }

                // Add stat type and current value
                try
                {
                    string statName = LocalizationHelper.GetStatTypeName(data.StatType);
                    int level = button.currentLevel;
                    float currentValue = data.GetValue(level);
                    sb.Append($". {statName}: +{LocalizationHelper.FormatStatValue(data.StatType, currentValue)}");
                }
                catch { /* Stat info not available */ }

                int level2 = button.currentLevel;
                int maxLevel = data.Levels != null ? data.Levels.Length : 0;

                if (maxLevel > 0)
                    sb.Append($", Level {level2}/{maxLevel}");
                else
                    sb.Append($", Level {level2}");

                // Read price for next level
                if (level2 < maxLevel)
                {
                    try
                    {
                        var prices = data.GetPrice(level2);
                        if (prices != null && prices.Count > 0)
                        {
                            sb.Append(", Cost: ");
                            for (int i = 0; i < prices.Count; i++)
                            {
                                var cv = prices[i];
                                if (cv != null)
                                {
                                    if (i > 0) sb.Append(", ");
                                    sb.Append($"{cv.Value} {cv.Type}");
                                }
                            }
                        }
                    }
                    catch { /* Price reading failed, skip */ }
                }
                else
                {
                    sb.Append(", Max level");
                }
            }

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

    /// <summary>
    /// Format a list of CurrencyValue into "50 Credits, 10 Morkite" style string.
    /// </summary>
    private static string FormatCurrencyList(List<CurrencyValue> currencies)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < currencies.Count; i++)
        {
            var cv = currencies[i];
            if (cv == null || cv.Value <= 0) continue;
            if (sb.Length > 0) sb.Append(", ");
            string name = LocalizationHelper.GetCurrencyName(cv.type);
            sb.Append($"{cv.Value} {name}");
        }
        return sb.ToString();
    }
}
