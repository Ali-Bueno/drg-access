using HarmonyLib;
using DRS.UI;
using TMPro;
using UnityEngine;
using System.Text;
using System.Reflection;
using drgAccess.Helpers;

namespace drgAccess.Patches;

/// <summary>
/// Patches to announce when UI forms become visible.
/// Forms that override SetVisibility are patched directly.
/// Forms that don't override SetVisibility are patched via their Show/Setup methods.
/// </summary>
public static class UIFormPatches
{
    /// <summary>
    /// True while the GammaAdjuster brightness screen is visible.
    /// Used to suppress "Main Menu" and enable focus tracking on the slider.
    /// </summary>
    internal static bool GammaAdjusterOpen;

    // Main Menu Splash - suppress when gamma adjuster is on top
    [HarmonyPatch(typeof(UIMenuSplashForm), nameof(UIMenuSplashForm.SetVisibility))]
    public static class UIMenuSplashForm_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIMenuSplashForm __instance, bool visible)
        {
            if (visible && !GammaAdjusterOpen)
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
            UISettingsPatch.SettingsOpen = visible;
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
            WalletReader.UpgradeFormOpen = visible;
            if (visible)
            {
                ScreenReader.Interrupt("Stat Upgrades");
                try { WalletReader.CachedWallet = __instance.wallet; } catch { }
            }
        }
    }

    // Shop Screen (inter-level shop after extraction)
    [HarmonyPatch(typeof(UIShopScreen), nameof(UIShopScreen.SetVisibility))]
    public static class UIShopScreen_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIShopScreen __instance, bool visible)
        {
            WalletReader.ShopFormOpen = visible;
            if (visible)
            {
                ScreenReader.Interrupt("Shop");
                try { WalletReader.CachedWallet = __instance.wallet; } catch { }
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
            var reader = Components.MilestoneReaderComponent.Instance;
            if (reader == null) return;

            if (visible)
                reader.Activate(__instance);
            else
                reader.Deactivate();
        }
    }

    // Milestone Form - refresh reader when tab changes (milestones re-setup)
    [HarmonyPatch(typeof(UIMilestoneForm), nameof(UIMilestoneForm.SetupMilestoneUI))]
    public static class UIMilestoneForm_SetupMilestoneUI
    {
        [HarmonyPostfix]
        public static void Postfix(UIMilestoneForm __instance)
        {
            var reader = Components.MilestoneReaderComponent.Instance;
            if (reader != null)
                reader.Refresh();
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

    // End Screen (game over / victory) - activates arrow-key navigable reader
    [HarmonyPatch(typeof(UIEndScreen), nameof(UIEndScreen.SetVisibility))]
    public static class UIEndScreen_SetVisibility
    {
        [HarmonyPostfix]
        public static void Postfix(UIEndScreen __instance, bool visible)
        {
            var reader = Components.EndScreenReaderComponent.Instance;
            if (reader == null) return;

            if (visible)
                reader.Activate(__instance);
            else
                reader.Deactivate();
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

    // =========================================================
    // Forms below do NOT override SetVisibility directly.
    // They are patched via their Setup/Show methods instead.
    // =========================================================

    // Level Up Form - patched via Setup (overloaded, uses TargetMethod)
    [HarmonyPatch]
    public static class UILevelUpForm_Setup
    {
        static MethodBase TargetMethod()
        {
            foreach (var m in typeof(UILevelUpForm).GetMethods())
            {
                if (m.Name == "Setup" && m.GetParameters().Length == 8)
                    return m;
            }
            return null;
        }

        [HarmonyPostfix]
        public static void Postfix(UILevelUpForm __instance)
        {
            try
            {
                var titleText = __instance.titleText;
                string title = titleText != null ? titleText.text : null;
                string message = !string.IsNullOrEmpty(title) ? $"Level Up: {TextHelper.CleanText(title)}" : "Level Up";
                ScreenReader.Interrupt(message);
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UILevelUpForm announce error: {ex.Message}");
                ScreenReader.Interrupt("Level Up");
            }
        }
    }

    // Overclock Form - patched via SetupOverclock
    [HarmonyPatch(typeof(UIOverclockForm), nameof(UIOverclockForm.SetupOverclock))]
    public static class UIOverclockForm_SetupOverclock
    {
        [HarmonyPostfix]
        public static void Postfix(UIOverclockForm __instance)
        {
            try
            {
                var sb = new StringBuilder("Overclock");
                var weaponTitle = __instance.weaponTitleText;
                if (weaponTitle != null && !string.IsNullOrEmpty(weaponTitle.text))
                {
                    sb.Append(": " + TextHelper.CleanText(weaponTitle.text));
                }
                var weaponLevel = __instance.weaponLevelText;
                if (weaponLevel != null && !string.IsNullOrEmpty(weaponLevel.text))
                {
                    sb.Append(", " + TextHelper.CleanText(weaponLevel.text));
                }
                ScreenReader.Interrupt(sb.ToString());
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UIOverclockForm announce error: {ex.Message}");
                ScreenReader.Interrupt("Overclock");
            }
        }
    }

    // Unlock Form - patched via Show
    [HarmonyPatch(typeof(UIUnlockForm), nameof(UIUnlockForm.Show))]
    public static class UIUnlockForm_Show
    {
        [HarmonyPostfix]
        public static void Postfix(UIUnlockForm __instance)
        {
            try
            {
                var sb = new StringBuilder("Unlock");
                var typeTitle = __instance.unlockTypeTitle;
                if (typeTitle != null && !string.IsNullOrEmpty(typeTitle.text))
                {
                    sb.Append(": " + TextHelper.CleanText(typeTitle.text));
                }
                var unlockName = __instance.unlockName;
                if (unlockName != null && !string.IsNullOrEmpty(unlockName.text))
                {
                    sb.Append(". " + TextHelper.CleanText(unlockName.text));
                }
                var unlockDesc = __instance.unlockDescription;
                if (unlockDesc != null && !string.IsNullOrEmpty(unlockDesc.text))
                {
                    sb.Append(". " + TextHelper.CleanText(unlockDesc.text));
                }
                ScreenReader.Interrupt(sb.ToString());
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UIUnlockForm announce error: {ex.Message}");
                ScreenReader.Interrupt("Unlock");
            }
        }
    }

    // Progression Summary Form - patched via Show
    [HarmonyPatch(typeof(UIProgressionSummaryForm), nameof(UIProgressionSummaryForm.Show))]
    public static class UIProgressionSummaryForm_Show
    {
        [HarmonyPostfix]
        public static void Postfix(UIProgressionSummaryForm __instance)
        {
            try
            {
                var sb = new StringBuilder("Run Summary");
                var credits = __instance.creditsText;
                if (credits != null && !string.IsNullOrEmpty(credits.text))
                    sb.Append($". Credits: {TextHelper.CleanText(credits.text)}");

                var xp = __instance.xpText;
                if (xp != null && !string.IsNullOrEmpty(xp.text))
                    sb.Append($". XP: {TextHelper.CleanText(xp.text)}");

                var rank = __instance.rankGainedText;
                if (rank != null && !string.IsNullOrEmpty(rank.text))
                    sb.Append($". Rank: {TextHelper.CleanText(rank.text)}");

                ScreenReader.Interrupt(sb.ToString());
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UIProgressionSummaryForm announce error: {ex.Message}");
                ScreenReader.Interrupt("Run Summary");
            }
        }
    }

    // Generic Popup Form - patched via ShowDualButton
    [HarmonyPatch(typeof(UIGenericPopupForm), nameof(UIGenericPopupForm.ShowDualButton))]
    public static class UIGenericPopupForm_ShowDualButton
    {
        [HarmonyPostfix]
        public static void Postfix(UIGenericPopupForm __instance)
        {
            AnnounceGenericPopup(__instance);
        }
    }

    // Generic Popup Form - patched via ShowSingleButton
    [HarmonyPatch(typeof(UIGenericPopupForm), nameof(UIGenericPopupForm.ShowSingleButton))]
    public static class UIGenericPopupForm_ShowSingleButton
    {
        [HarmonyPostfix]
        public static void Postfix(UIGenericPopupForm __instance)
        {
            AnnounceGenericPopup(__instance);
        }
    }

    // Mutator Form - patched via Setup
    [HarmonyPatch(typeof(UIMutatorForm), nameof(UIMutatorForm.Setup))]
    public static class UIMutatorForm_Setup
    {
        [HarmonyPostfix]
        public static void Postfix(UIMutatorForm __instance)
        {
            ScreenReader.Interrupt("Mutator Selection");
        }
    }

    // Gear Found Form - patched via Show
    [HarmonyPatch(typeof(UIGearFoundForm), nameof(UIGearFoundForm.Show))]
    public static class UIGearFoundForm_Show
    {
        [HarmonyPostfix]
        public static void Postfix(UIGearFoundForm __instance)
        {
            ScreenReader.Interrupt("Gear Found");
        }
    }

    // Gear Inspect Form - patched via Show
    [HarmonyPatch(typeof(UIGearInspectForm), nameof(UIGearInspectForm.Show))]
    public static class UIGearInspectForm_Show
    {
        [HarmonyPostfix]
        public static void Postfix(UIGearInspectForm __instance)
        {
            ScreenReader.Interrupt("Gear Details");
        }
    }

    // Score Inspect Form - patched via Show (note: class name has typo "Inpect" in game code)
    [HarmonyPatch(typeof(UIScoreInpectForm), nameof(UIScoreInpectForm.Show))]
    public static class UIScoreInpectForm_Show
    {
        [HarmonyPostfix]
        public static void Postfix(UIScoreInpectForm __instance)
        {
            ScreenReader.Interrupt("Score");
        }
    }

    // Splash Form - announce "press any key" only when flow reaches SPLASH state (after intro videos)
    [HarmonyPatch(typeof(UISplashForm), nameof(UISplashForm.AdvanceFlow))]
    public static class UISplashForm_AdvanceFlow
    {
        [HarmonyPostfix]
        public static void Postfix(UISplashForm __instance)
        {
            try
            {
                if (__instance.flow != UISplashForm.EFlow.SPLASH) return;

                var keyText = __instance.pressAnyKeyText;
                if (keyText != null && !string.IsNullOrEmpty(keyText.text))
                {
                    ScreenReader.Interrupt(TextHelper.CleanText(keyText.text));
                    return;
                }
                var btnText = __instance.pressAnyButtonText;
                if (btnText != null && !string.IsNullOrEmpty(btnText.text))
                {
                    ScreenReader.Interrupt(TextHelper.CleanText(btnText.text));
                    return;
                }
                ScreenReader.Interrupt("Press any key");
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UISplashForm AdvanceFlow error: {ex.Message}");
            }
        }
    }

    // Gamma Adjuster (first-time brightness setup screen)
    [HarmonyPatch(typeof(GammaAdjuster), nameof(GammaAdjuster.Show))]
    public static class GammaAdjuster_Show
    {
        [HarmonyPostfix]
        public static void Postfix(GammaAdjuster __instance)
        {
            GammaAdjusterOpen = true;
            SettingsFocusTracker.SuppressUntilFrame = Time.frameCount + 3;
            try
            {
                var sb = new StringBuilder("Brightness Adjustment. ");
                sb.Append("Use left and right arrows to adjust brightness, then press Enter to confirm");

                var slider = __instance.slider;
                if (slider != null)
                {
                    string label = UISettingsPatch.GetSliderLabel(slider);
                    string value = UISettingsPatch.GetSliderValue(slider);
                    if (!string.IsNullOrEmpty(label))
                        sb.Append($". {label}");
                    if (!string.IsNullOrEmpty(value))
                        sb.Append($": {value}");
                }

                ScreenReader.Interrupt(sb.ToString());
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"GammaAdjuster Show error: {ex.Message}");
                ScreenReader.Interrupt("Brightness Adjustment. Use left and right arrows to adjust, Enter to confirm");
            }
        }
    }

    [HarmonyPatch(typeof(GammaAdjuster), nameof(GammaAdjuster.Hide))]
    public static class GammaAdjuster_Hide
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            GammaAdjusterOpen = false;
        }
    }

    [HarmonyPatch(typeof(GammaAdjuster), nameof(GammaAdjuster.OnClickOK))]
    public static class GammaAdjuster_OnClickOK
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            GammaAdjusterOpen = false;
            ScreenReader.Interrupt("Brightness confirmed");
        }
    }

    private static void AnnounceGenericPopup(UIGenericPopupForm instance)
    {
        try
        {
            var sb = new StringBuilder();
            var title = instance.title;
            if (title != null && !string.IsNullOrEmpty(title.text))
                sb.Append(TextHelper.CleanText(title.text));

            var desc = instance.decription;
            if (desc != null && !string.IsNullOrEmpty(desc.text))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(TextHelper.CleanText(desc.text));
            }

            if (sb.Length > 0)
                ScreenReader.Interrupt(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIGenericPopupForm announce error: {ex.Message}");
        }
    }

}
