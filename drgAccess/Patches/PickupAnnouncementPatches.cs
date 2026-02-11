using System.Collections.Generic;
using HarmonyLib;
using drgAccess.Helpers;

namespace drgAccess.Patches;

/// <summary>
/// Announces pickup events via screen reader: heals, currency, gear, loot crates.
/// Patches CoreGameEvents.TriggerXxx methods.
/// </summary>
[HarmonyPatch(typeof(CoreGameEvents))]
public static class PickupAnnouncementPatches
{
    // Cooldown to avoid spam from rapid pickups of same currency type
    private static readonly Dictionary<ECurrency, float> lastCurrencyAnnounce = new();
    private const float CURRENCY_COOLDOWN = 2f;

    [HarmonyPatch(nameof(CoreGameEvents.TriggerPlayerHeal))]
    [HarmonyPostfix]
    public static void OnHeal_Postfix(PlayerHealArgs args)
    {
        try
        {
            if (args == null) return;

            // Skip REGEN — it fires continuously and would spam
            var healType = args.Type;
            if (healType == EHealType.REGEN) return;

            int healed = args.ActualHeal;
            if (healed <= 0) return;

            if (healType == EHealType.MAX_HP)
                ScreenReader.Say($"Max HP increased by {healed}");
            else
                ScreenReader.Say($"Healed {healed} HP");
        }
        catch (System.Exception e)
        {
            Plugin.Log?.LogError($"[PickupAnnounce] OnHeal error: {e.Message}");
        }
    }

    [HarmonyPatch(nameof(CoreGameEvents.TriggerPlayerCurrencyPickup))]
    [HarmonyPostfix]
    public static void OnCurrency_Postfix(PlayerCurrencyPickupArgs args)
    {
        try
        {
            if (args == null) return;

            var type = args.CurrencyType;
            int amount = args.Amount;
            if (amount <= 0) return;

            // Throttle per currency type to avoid spam from rapid pickups
            float now = UnityEngine.Time.time;
            if (lastCurrencyAnnounce.TryGetValue(type, out float lastTime) &&
                now - lastTime < CURRENCY_COOLDOWN)
                return;
            lastCurrencyAnnounce[type] = now;

            string name = LocalizationHelper.GetCurrencyName(type);
            ScreenReader.Say($"{amount} {name}");
        }
        catch (System.Exception e)
        {
            Plugin.Log?.LogError($"[PickupAnnounce] OnCurrency error: {e.Message}");
        }
    }

    [HarmonyPatch(nameof(CoreGameEvents.TriggerGearPickedUp))]
    [HarmonyPostfix]
    public static void OnGear_Postfix(GearPickupArgs args)
    {
        try
        {
            if (args == null) return;

            var gearView = args.GearView;
            if (gearView == null) return;

            string name = null;
            try
            {
                var data = gearView.Data;
                if (data != null)
                    name = TextHelper.CleanText(data.GetTitle());
            }
            catch { }

            if (!string.IsNullOrEmpty(name))
                ScreenReader.Say($"Picked up {name}");
            else
                ScreenReader.Say("Gear picked up");
        }
        catch (System.Exception e)
        {
            Plugin.Log?.LogError($"[PickupAnnounce] OnGear error: {e.Message}");
        }
    }

    [HarmonyPatch(nameof(CoreGameEvents.TriggerLootCrateAccessed))]
    [HarmonyPostfix]
    public static void OnLootCrate_Postfix(LootCrateArgs args)
    {
        try
        {
            if (args == null) return;

            string rarity = LocalizationHelper.GetRarityText(args.Rarity);
            ScreenReader.Say($"{rarity} loot crate");
        }
        catch (System.Exception e)
        {
            Plugin.Log?.LogError($"[PickupAnnounce] OnLootCrate error: {e.Message}");
        }
    }
}
