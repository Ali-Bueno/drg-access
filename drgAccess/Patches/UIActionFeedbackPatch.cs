using HarmonyLib;
using DRS.UI;
using drgAccess.Helpers;
using System.Text;

namespace drgAccess.Patches;

/// <summary>
/// Patches for action feedback: announces results when player performs actions
/// (buy/sell minerals, apply upgrades, equip/unequip gear).
/// Also provides wallet reading via G key through cached wallet reference.
/// </summary>

// Mineral Market: announce buy result
[HarmonyPatch(typeof(UIMineralMarketButton), nameof(UIMineralMarketButton.TryBuyMaterial))]
public static class MineralBuyPatch
{
    public static void Postfix(UIMineralMarketButton __instance, bool __result)
    {
        try
        {
            if (__instance.wallet != null)
                WalletReader.CachedWallet = __instance.wallet;

            ScreenReader.Interrupt(__result ? "Bought" : "Cannot afford");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"MineralBuyPatch error: {ex.Message}");
        }
    }
}

// Mineral Market: announce sell result
[HarmonyPatch(typeof(UIMineralMarketButton), nameof(UIMineralMarketButton.TrySellMaterial))]
public static class MineralSellPatch
{
    public static void Postfix(UIMineralMarketButton __instance, bool __result)
    {
        try
        {
            if (__instance.wallet != null)
                WalletReader.CachedWallet = __instance.wallet;

            if (__result)
            {
                ScreenReader.Interrupt("Sold");
            }
            else
            {
                int amount = __instance.wallet.GetAmount(__instance.material);
                ScreenReader.Interrupt(amount <= 0 ? "Nothing to sell" : "Cannot sell");
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"MineralSellPatch error: {ex.Message}");
        }
    }
}

// Stat Upgrades: announce "Cannot afford" on failed click
[HarmonyPatch(typeof(UIStatUpgradeButton), nameof(UIStatUpgradeButton.OnButtonClick))]
public static class StatUpgradeClickPatch
{
    public static void Prefix(UIStatUpgradeButton __instance, out bool __state)
    {
        __state = false;
        try
        {
            __state = __instance.canAfford;
            var wallet = __instance.wallet;
            if (wallet != null)
                WalletReader.CachedWallet = wallet;
        }
        catch { }
    }

    public static void Postfix(UIStatUpgradeButton __instance, bool __state)
    {
        try
        {
            // If couldn't afford before the click, announce it
            // Success case is handled by StatUpgradeSuccessPatch (OnUpgradeSuccess)
            if (!__state)
                ScreenReader.Interrupt("Cannot afford");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"StatUpgradeClickPatch error: {ex.Message}");
        }
    }
}

// Stat Upgrades: announce successful upgrade
[HarmonyPatch(typeof(UIStatUpgradeButton), nameof(UIStatUpgradeButton.OnUpgradeSuccess))]
public static class StatUpgradeSuccessPatch
{
    public static void Postfix(UIStatUpgradeButton __instance, int newLevel)
    {
        try
        {
            int maxLevel = 0;
            try
            {
                var levels = __instance.data?.Levels;
                if (levels != null)
                    maxLevel = levels.Length;
            }
            catch { }

            string feedback;
            if (maxLevel > 0 && newLevel >= maxLevel)
                feedback = "Max level reached";
            else if (maxLevel > 0)
                feedback = $"Upgraded to level {newLevel}/{maxLevel}";
            else
                feedback = $"Upgraded to level {newLevel}";

            ScreenReader.Interrupt(feedback);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"StatUpgradeSuccessPatch error: {ex.Message}");
        }
    }
}

// Shop Screen: announce successful purchase
[HarmonyPatch(typeof(UIShopScreen), nameof(UIShopScreen.OnPurchaseComplete))]
public static class ShopPurchasePatch
{
    public static void Postfix(UIShopScreen __instance, UIShopButton btn)
    {
        try
        {
            string name = "item";
            var skillData = btn?.skillData;
            if (skillData != null && !string.IsNullOrEmpty(skillData.Title))
                name = TextHelper.CleanText(skillData.Title);

            ScreenReader.Interrupt($"Purchased {name}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"ShopPurchasePatch error: {ex.Message}");
        }
    }
}

// Shop Screen: announce reroll result
[HarmonyPatch(typeof(UIShopScreen), nameof(UIShopScreen.OnRerollButton))]
public static class ShopRerollPatch
{
    public static void Prefix(UIShopScreen __instance, out bool __state)
    {
        __state = false;
        try { __state = __instance.CanAffordReroll(); } catch { }
    }

    public static void Postfix(UIShopScreen __instance, bool __state)
    {
        try
        {
            ScreenReader.Interrupt(__state ? "Rerolled" : "Cannot afford reroll");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"ShopRerollPatch error: {ex.Message}");
        }
    }
}

// Shop Screen: announce heal result
[HarmonyPatch(typeof(UIShopScreen), nameof(UIShopScreen.OnHealButton))]
public static class ShopHealPatch
{
    public static void Prefix(UIShopScreen __instance, out bool __state)
    {
        __state = false;
        try { __state = __instance.CanAffordHeal(); } catch { }
    }

    public static void Postfix(UIShopScreen __instance, bool __state)
    {
        try
        {
            ScreenReader.Interrupt(__state ? "Healed" : "Cannot afford heal");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"ShopHealPatch error: {ex.Message}");
        }
    }
}

// Shop Button: announce "Cannot afford" on failed purchase attempt
[HarmonyPatch(typeof(UIShopButton), nameof(UIShopButton.OnButtonClick))]
public static class ShopButtonClickPatch
{
    public static void Prefix(UIShopButton __instance, out bool __state)
    {
        __state = false;
        try { __state = __instance.canAfford; } catch { }
    }

    public static void Postfix(UIShopButton __instance, bool __state)
    {
        try
        {
            if (!__state)
                ScreenReader.Interrupt("Cannot afford");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"ShopButtonClickPatch error: {ex.Message}");
        }
    }
}

// Gear Equip: announce when gear is equipped
[HarmonyPatch(typeof(GearManager), nameof(GearManager.TryEquipGear))]
public static class GearEquipPatch
{
    private static float lastAnnounceTime;

    public static void Postfix(bool __result, GearView gv)
    {
        try
        {
            if (!__result) return;

            // Debounce: TryEquipGearOnAll calls this per class
            float now = UnityEngine.Time.unscaledTime;
            if (now - lastAnnounceTime < 0.3f) return;
            lastAnnounceTime = now;

            string gearName = "gear";
            try
            {
                var data = gv?.Data;
                if (data != null)
                {
                    string title = data.GetTitle();
                    if (!string.IsNullOrEmpty(title))
                        gearName = TextHelper.CleanText(title);
                }
            }
            catch { }

            UIButtonPatch.QueueUntilTime = UnityEngine.Time.unscaledTime + 0.5f;
            ScreenReader.Interrupt($"Equipped {gearName}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"GearEquipPatch error: {ex.Message}");
        }
    }
}

// Gear Unequip: announce when gear is unequipped
[HarmonyPatch(typeof(GearManager), nameof(GearManager.TryUnequipGear))]
public static class GearUnequipPatch
{
    private static float lastAnnounceTime;

    public static void Postfix(bool __result, GearView gv)
    {
        try
        {
            if (!__result) return;

            float now = UnityEngine.Time.unscaledTime;
            if (now - lastAnnounceTime < 0.3f) return;
            lastAnnounceTime = now;

            string gearName = "gear";
            try
            {
                var data = gv?.Data;
                if (data != null)
                {
                    string title = data.GetTitle();
                    if (!string.IsNullOrEmpty(title))
                        gearName = TextHelper.CleanText(title);
                }
            }
            catch { }

            UIButtonPatch.QueueUntilTime = UnityEngine.Time.unscaledTime + 0.5f;
            ScreenReader.Interrupt($"Unequipped {gearName}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"GearUnequipPatch error: {ex.Message}");
        }
    }
}

// Gear Upgrade: announce when gear is upgraded
[HarmonyPatch(typeof(GearManager), nameof(GearManager.UpgradeGear))]
public static class GearUpgradePatch
{
    public static void Postfix(GearView gearView)
    {
        try
        {
            string gearName = "gear";
            try
            {
                var data = gearView?.Data;
                if (data != null)
                {
                    string title = data.GetTitle();
                    if (!string.IsNullOrEmpty(title))
                        gearName = TextHelper.CleanText(title);
                }
            }
            catch { }

            UIButtonPatch.QueueUntilTime = UnityEngine.Time.unscaledTime + 0.5f;
            ScreenReader.Interrupt($"Upgraded {gearName}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"GearUpgradePatch error: {ex.Message}");
        }
    }
}

// Gear Salvage: announce when gear is salvaged/sold
[HarmonyPatch(typeof(GearManager), nameof(GearManager.SalvageGear))]
public static class GearSalvagePatch
{
    public static void Postfix(GearView gv)
    {
        try
        {
            string gearName = "gear";
            try
            {
                var data = gv?.Data;
                if (data != null)
                {
                    string title = data.GetTitle();
                    if (!string.IsNullOrEmpty(title))
                        gearName = TextHelper.CleanText(title);
                }
            }
            catch { }

            UIButtonPatch.QueueUntilTime = UnityEngine.Time.unscaledTime + 0.5f;
            ScreenReader.Interrupt($"Salvaged {gearName}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"GearSalvagePatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Static helper to cache wallet reference and read balances on G key.
/// The MonoBehaviour (WalletReaderComponent) handles Update/key detection.
/// </summary>
public static class WalletReader
{
    internal static Wallet CachedWallet;
    internal static bool UpgradeFormOpen;
    internal static bool ShopFormOpen;
    internal static bool GearInventoryOpen;

    internal static bool IsWalletReadable => UpgradeFormOpen || ShopFormOpen || GearInventoryOpen;

    public static void ReadWallet()
    {
        try
        {
            var wallet = CachedWallet;
            if (wallet == null)
            {
                ScreenReader.Interrupt("No wallet available");
                return;
            }

            var sb = new StringBuilder();

            // Main currencies (Gold always shown — it's the primary shop currency)
            sb.Append($"Gold: {wallet.Gold}");
            AppendCurrency(sb, "Credits", wallet.Credits);

            // Minerals
            AppendCurrency(sb, "Morkite", wallet.Morkite);
            AppendCurrency(sb, "Nitra", wallet.Nitra);
            AppendCurrency(sb, "Bismor", wallet.Bismor);
            AppendCurrency(sb, "Croppa", wallet.Croppa);
            AppendCurrency(sb, "Enor Pearl", wallet.EnorPearl);
            AppendCurrency(sb, "Jadiz", wallet.Jadiz);
            AppendCurrency(sb, "Magnite", wallet.Magnite);
            AppendCurrency(sb, "Umanite", wallet.Umanite);

            // Special currencies
            AppendCurrency(sb, "Power Core", wallet.PowerCore);
            AppendCurrency(sb, "Artifact Rerolls", wallet.ArtifactRerolls);
            AppendCurrency(sb, "Mutator Rerolls", wallet.MutatorRerolls);
            AppendCurrency(sb, "Ommoran Core", wallet.OmmoranCures);

            if (sb.Length == 0)
                sb.Append("No currency");

            ScreenReader.Interrupt(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"WalletReader.ReadWallet error: {ex.Message}");
            ScreenReader.Interrupt("Cannot read wallet");
        }
    }

    private static void AppendCurrency(StringBuilder sb, string name, int amount)
    {
        if (amount <= 0) return;
        if (sb.Length > 0) sb.Append(", ");
        sb.Append($"{name}: {amount}");
    }
}

/// <summary>
/// Reads all equipped gear via T key when gear inventory is open.
/// Iterates the UIGearEquipped panel's slot lists to build a summary.
/// </summary>
public static class EquippedGearReader
{
    public static void ReadEquipped()
    {
        try
        {
            var form = UIFormPatches.CachedGearForm;
            if (form == null)
            {
                ScreenReader.Interrupt("No gear inventory");
                return;
            }

            var equippedPanel = form.gearEquipped;
            if (equippedPanel == null)
            {
                ScreenReader.Interrupt("No equipped gear panel");
                return;
            }

            var sb = new StringBuilder("Equipped gear");
            bool hasAny = false;

            AppendSlotGear(sb, equippedPanel.armorSlots, "Armor", ref hasAny);
            AppendSlotGear(sb, equippedPanel.companionSlots, "Companion", ref hasAny);
            AppendSlotGear(sb, equippedPanel.grinderSlots, "Grinder", ref hasAny);
            AppendSlotGear(sb, equippedPanel.tankSlots, "Tank", ref hasAny);
            AppendSlotGear(sb, equippedPanel.toolSlots, "Tool", ref hasAny);
            AppendSlotGear(sb, equippedPanel.weaponModSlots, "Weapon Mod", ref hasAny);

            if (!hasAny)
                sb.Append(": None");

            ScreenReader.Interrupt(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"EquippedGearReader error: {ex.Message}");
            ScreenReader.Interrupt("Cannot read equipped gear");
        }
    }

    private static void AppendSlotGear(
        StringBuilder sb,
        Il2CppSystem.Collections.Generic.List<DRS.UI.UIGearEquipSlot> slots,
        string slotLabel,
        ref bool hasAny)
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Count; i++)
        {
            try
            {
                var slot = slots[i];
                if (slot == null) continue;
                var compact = slot.gearCompact;
                if (compact == null) continue;
                var gear = compact.gear;
                if (gear == null) continue;
                var data = gear.Data;
                if (data == null) continue;

                string title = data.GetTitle();
                if (string.IsNullOrEmpty(title)) continue;

                sb.Append(hasAny ? ". " : ": ");
                sb.Append($"{slotLabel}: {Helpers.TextHelper.CleanText(title)}");
                hasAny = true;
            }
            catch { }
        }
    }
}
