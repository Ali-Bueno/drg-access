using HarmonyLib;
using DRS.UI;
using TMPro;
using UnityEngine;

namespace drgAccess.Patches;

/// <summary>
/// Patches to announce when UI forms become visible.
/// Only patches classes that have SetVisibility method directly.
/// </summary>
public static class UIFormPatches
{
    // Main Menu Splash
    [HarmonyPatch(typeof(UIMenuSplashForm), nameof(UIMenuSplashForm.SetVisibility))]
    public static class UIMenuSplashForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIMenuSplashForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Main Menu");
            }
        }
    }

    // Play Select Form (mission/character selection)
    [HarmonyPatch(typeof(UIPlaySelectForm), nameof(UIPlaySelectForm.SetVisibility))]
    public static class UIPlaySelectForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIPlaySelectForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Play Menu");
            }
        }
    }

    // Settings Form
    [HarmonyPatch(typeof(UISettingsForm), nameof(UISettingsForm.SetVisibility))]
    public static class UISettingsForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UISettingsForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Settings");
            }
        }
    }

    // Gear Inventory Form
    [HarmonyPatch(typeof(UIGearInventoryForm), nameof(UIGearInventoryForm.SetVisibility))]
    public static class UIGearInventoryForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIGearInventoryForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Gear Inventory");
            }
        }
    }

    // Stat Upgrade Form
    [HarmonyPatch(typeof(UIStatUpgradeForm), nameof(UIStatUpgradeForm.SetVisibility))]
    public static class UIStatUpgradeForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIStatUpgradeForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Stat Upgrades");
            }
        }
    }

    // Milestone Form
    [HarmonyPatch(typeof(UIMilestoneForm), nameof(UIMilestoneForm.SetVisibility))]
    public static class UIMilestoneForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIMilestoneForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Milestones");
            }
        }
    }

    // Skin Overrides Form
    [HarmonyPatch(typeof(UISkinOverridesForm), nameof(UISkinOverridesForm.SetVisibility))]
    public static class UISkinOverridesForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UISkinOverridesForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Skin Overrides");
            }
        }
    }

    // Pause Form (in-game)
    [HarmonyPatch(typeof(UICorePauseForm), nameof(UICorePauseForm.SetVisibility))]
    public static class UICorePauseForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UICorePauseForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Game Paused");
            }
        }
    }

    // End Screen (game over / victory)
    [HarmonyPatch(typeof(UIEndScreen), nameof(UIEndScreen.SetVisibility))]
    public static class UIEndScreen_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIEndScreen __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Interrupt("Run Complete");
            }
        }
    }

    // Loading Form
    [HarmonyPatch(typeof(UILoadingForm), nameof(UILoadingForm.SetVisibility))]
    public static class UILoadingForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UILoadingForm __instance, bool visible)
        {
            if (visible)
            {
                ScreenReader.Say("Loading");
            }
        }
    }

    // Quit Popup - uses ShowDualButton method
    [HarmonyPatch(typeof(UIQuitPopupForm), nameof(UIQuitPopupForm.ShowDualButton_NON_LOCALIZED))]
    public static class UIQuitPopupForm_Show
    {
        [HarmonyPostfix]
        public static void Postfix(UIQuitPopupForm __instance, string title, string decription)
        {
            string message = $"{title}. {decription}";
            ScreenReader.Interrupt(message);
        }
    }
}
