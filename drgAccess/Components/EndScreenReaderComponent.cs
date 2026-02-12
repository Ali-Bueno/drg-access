using System;
using System.Collections.Generic;
using System.Text;
using DRS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components;

/// <summary>
/// Arrow-key navigable reader for the end screen (death/victory stats).
/// Collects all visible text into an ordered list. Up/Down navigates,
/// Enter activates Retry/Continue buttons at the end of the list.
/// Blocks EventSystem while active to prevent conflicting navigation.
/// </summary>
public class EndScreenReaderComponent : MonoBehaviour
{
    public static EndScreenReaderComponent Instance { get; private set; }

    private bool isActive;
    private UIEndScreen endScreen;
    private List<EndScreenItem> items;
    private int selectedIndex;
    private int collectDelay = -1;

    // EventSystem blocking
    private GameObject eventSystemObject;
    private int restoreStep = -1;

    private struct EndScreenItem
    {
        public string Text;
        public Action OnActivate;
    }

    static EndScreenReaderComponent()
    {
        ClassInjector.RegisterTypeInIl2Cpp<EndScreenReaderComponent>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Activate(UIEndScreen screen)
    {
        endScreen = screen;
        isActive = false;
        items = null;
        selectedIndex = 0;
        collectDelay = 10; // wait frames for text to populate
    }

    public void Deactivate()
    {
        if (!isActive && collectDelay < 0) return;
        isActive = false;
        collectDelay = -1;
        endScreen = null;
        items = null;
        RestoreEventSystem();
    }

    void Update()
    {
        try
        {
            // Restoration state machine
            if (restoreStep > 0)
            {
                restoreStep--;
            }
            else if (restoreStep == 0)
            {
                restoreStep = -1;
                FinishRestore();
            }

            // Wait for text to populate
            if (collectDelay > 0)
            {
                collectDelay--;
                return;
            }
            if (collectDelay == 0)
            {
                collectDelay = -1;
                CollectItems();
                return;
            }

            if (!isActive || items == null || items.Count == 0) return;

            // Navigation
            if (InputHelper.NavigateUp())
                Navigate(-1);
            else if (InputHelper.NavigateDown())
                Navigate(1);
            else if (InputHelper.Confirm())
                ActivateCurrent();
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"EndScreenReader Update error: {e.Message}");
        }
    }


    private void Navigate(int direction)
    {
        selectedIndex += direction;
        if (selectedIndex < 0) selectedIndex = items.Count - 1;
        if (selectedIndex >= items.Count) selectedIndex = 0;

        ScreenReader.Interrupt(items[selectedIndex].Text);
    }

    private void ActivateCurrent()
    {
        var item = items[selectedIndex];
        if (item.OnActivate != null)
        {
            var action = item.OnActivate;
            Deactivate();
            action();
        }
    }

    // ===== Data collection =====

    private void CollectItems()
    {
        if (endScreen == null) return;

        items = new List<EndScreenItem>();

        try
        {
            CollectTitle();
            CollectClassInfo();
            CollectScoreInfo();
            CollectRewards();
            CollectResources();
            CollectWeapons();
            CollectDamageInfo();
            CollectPlayerStats();
            CollectDiveStats();
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"EndScreenReader CollectItems error: {e.Message}");
        }

        CollectButtons();

        if (items.Count == 0)
            items.Add(new EndScreenItem { Text = "No information available" });

        BlockEventSystem();

        isActive = true;
        selectedIndex = 0;

        ScreenReader.Interrupt(
            $"End screen. Up and down arrows to navigate, Enter to select. {items[0].Text}");
    }

    private string GetText(TMP_Text field)
    {
        if (field == null) return null;
        try
        {
            string text = field.text;
            if (string.IsNullOrEmpty(text)) return null;
            return TextHelper.CleanText(text);
        }
        catch { return null; }
    }

    private bool IsVisible(Component comp)
    {
        try { return comp != null && comp.gameObject.activeInHierarchy; }
        catch { return false; }
    }

    private bool IsVisible(GameObject go)
    {
        try { return go != null && go.activeInHierarchy; }
        catch { return false; }
    }

    private void AddItem(string text, Action onActivate = null)
    {
        if (!string.IsNullOrEmpty(text))
            items.Add(new EndScreenItem { Text = text, OnActivate = onActivate });
    }

    private void CollectTitle()
    {
        var sb = new StringBuilder();
        string title = GetText(endScreen.titleText);
        if (!string.IsNullOrEmpty(title)) sb.Append(title);

        string sub = GetText(endScreen.subTitleText);
        if (!string.IsNullOrEmpty(sub))
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(sub);
        }

        if (sb.Length > 0) AddItem(sb.ToString());
    }

    private void CollectClassInfo()
    {
        var sb = new StringBuilder();

        string classMod = GetText(endScreen.classModText);
        if (!string.IsNullOrEmpty(classMod)) sb.Append(classMod);

        string classXp = GetText(endScreen.classXpText);
        if (!string.IsNullOrEmpty(classXp))
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(classXp);
        }

        string rankXp = GetText(endScreen.rankXpText);
        if (!string.IsNullOrEmpty(rankXp))
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append($"Rank XP: {rankXp}");
        }

        string endlessRankXp = GetText(endScreen.endlessRankXpText);
        if (!string.IsNullOrEmpty(endlessRankXp))
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append($"Endless Rank XP: {endlessRankXp}");
        }

        if (sb.Length > 0) AddItem(sb.ToString());
    }

    private void CollectScoreInfo()
    {
        var sb = new StringBuilder();

        // Regular high score
        if (IsVisible(endScreen.highScore))
        {
            string label = GetText(endScreen.highScoreText);
            string counter = GetText(endScreen.highScoreCounterText);
            if (!string.IsNullOrEmpty(label)) sb.Append(label);
            if (!string.IsNullOrEmpty(counter))
            {
                if (sb.Length > 0) sb.Append(": ");
                sb.Append(counter);
            }
        }

        // Endless score
        if (IsVisible(endScreen.endlessScore))
        {
            string hazard = GetText(endScreen.endlessScoreHazardText);
            if (!string.IsNullOrEmpty(hazard))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append($"Hazard: {hazard}");
            }

            string stage = GetText(endScreen.endlessScoreStageCounterText);
            if (!string.IsNullOrEmpty(stage))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append($"Stages: {stage}");
            }
        }

        if (sb.Length > 0) AddItem(sb.ToString());
    }

    private void CollectRewards()
    {
        var sb = new StringBuilder();

        string credits = GetText(endScreen.creditsText);
        if (!string.IsNullOrEmpty(credits))
            sb.Append($"Credits: {credits}");

        string endlessCredits = GetText(endScreen.endlessCreditsText);
        if (!string.IsNullOrEmpty(endlessCredits))
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append($"Endless Credits: {endlessCredits}");
        }

        if (sb.Length > 0) AddItem(sb.ToString());
    }

    private void CollectResources()
    {
        try
        {
            var resources = endScreen.craftingResourceStats;
            if (resources == null || resources.Length == 0) return;

            var sb = new StringBuilder("Resources: ");
            bool hasAny = false;

            for (int i = 0; i < resources.Length; i++)
            {
                var res = resources[i];
                if (!IsVisible(res)) continue;

                string name = GetText(res.nameText);
                string count = GetText(res.counterText);
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(count)) continue;

                if (hasAny) sb.Append(", ");
                hasAny = true;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(count))
                    sb.Append($"{name}: {count}");
                else
                    sb.Append(name ?? count);
            }

            if (hasAny) AddItem(sb.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"EndScreenReader CollectResources error: {e.Message}");
        }
    }

    private void CollectWeapons()
    {
        try
        {
            var weapons = endScreen.weaponStats;
            if (weapons == null || weapons.Length == 0) return;

            bool first = true;

            for (int i = 0; i < weapons.Length; i++)
            {
                var weapon = weapons[i];
                if (!IsVisible(weapon)) continue;

                var sb = new StringBuilder();

                if (first)
                {
                    sb.Append("Weapon Report. ");
                    first = false;
                }

                string name = GetText(weapon.nameText);
                if (!string.IsNullOrEmpty(name)) sb.Append(name);

                string level = GetText(weapon.levelText);
                if (!string.IsNullOrEmpty(level))
                {
                    if (!string.IsNullOrEmpty(name)) sb.Append(", ");
                    sb.Append($"Level {level}");
                }

                string stacks = GetText(weapon.stacksText);
                if (!string.IsNullOrEmpty(stacks) && stacks != "1")
                    sb.Append($", x{stacks}");

                string damage = GetText(weapon.damageText);
                if (!string.IsNullOrEmpty(damage))
                    sb.Append($", Damage: {damage}");

                string dps = GetText(weapon.dpsText);
                if (!string.IsNullOrEmpty(dps))
                    sb.Append($", DPS: {dps}");

                if (sb.Length > 0) AddItem(sb.ToString());
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"EndScreenReader CollectWeapons error: {e.Message}");
        }
    }

    private void CollectDamageInfo()
    {
        try
        {
            string totalDmg = GetText(endScreen.totalDamageText);
            var damageTypes = endScreen.damageStats;

            if (string.IsNullOrEmpty(totalDmg) &&
                (damageTypes == null || damageTypes.Length == 0))
                return;

            var sb = new StringBuilder("Damage Breakdown");

            if (!string.IsNullOrEmpty(totalDmg))
                sb.Append($". Total: {totalDmg}");

            if (damageTypes != null)
            {
                for (int i = 0; i < damageTypes.Length; i++)
                {
                    var dt = damageTypes[i];
                    if (!IsVisible(dt)) continue;

                    string name = GetText(dt.nameText);
                    string dmg = GetText(dt.damageText);
                    if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(dmg)) continue;

                    sb.Append(". ");
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dmg))
                        sb.Append($"{name}: {dmg}");
                    else
                        sb.Append(name ?? dmg);
                }
            }

            AddItem(sb.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"EndScreenReader CollectDamageInfo error: {e.Message}");
        }
    }

    private void CollectPlayerStats()
    {
        try
        {
            var stats = endScreen.playerStats;
            if (stats == null || stats.Length == 0) return;

            var sb = new StringBuilder("Player Stats");
            bool hasAny = false;

            for (int i = 0; i < stats.Length; i++)
            {
                var stat = stats[i];
                if (!IsVisible(stat)) continue;

                string name = GetText(stat.nameText);
                string value = GetText(stat.valueText);
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(value)) continue;

                hasAny = true;
                sb.Append(". ");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    sb.Append($"{name}: {value}");
                else
                    sb.Append(name ?? value);
            }

            if (hasAny) AddItem(sb.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"EndScreenReader CollectPlayerStats error: {e.Message}");
        }
    }

    private void CollectDiveStats()
    {
        try
        {
            var stats = endScreen.diveStats;
            if (stats == null || stats.Length == 0) return;

            var sb = new StringBuilder("Exploration Stats");
            bool hasAny = false;

            for (int i = 0; i < stats.Length; i++)
            {
                var stat = stats[i];
                if (!IsVisible(stat)) continue;

                string name = GetText(stat.nameText);
                string counter = GetText(stat.counterText);
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(counter)) continue;

                hasAny = true;
                sb.Append(". ");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(counter))
                    sb.Append($"{name}: {counter}");
                else
                    sb.Append(name ?? counter);
            }

            if (hasAny) AddItem(sb.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log?.LogError($"EndScreenReader CollectDiveStats error: {e.Message}");
        }
    }

    private void CollectButtons()
    {
        var screen = endScreen;

        // Retry button
        try
        {
            var retry = endScreen.retryButton;
            if (retry != null && IsVisible(retry))
            {
                string label = GetButtonLabel(retry) ?? "Retry";
                AddItem(label, () => { try { screen.OnRetryButton(); } catch { } });
            }
        }
        catch { }

        // Continue button (back to main menu) - no field, connected via Inspector
        AddItem("Continue", () => { try { screen.OnMenuButton(); } catch { } });

        // Endless button (only in endless mode)
        try
        {
            var endless = endScreen.endlessButton;
            if (endless != null && IsVisible(endless))
            {
                string label = GetButtonLabel(endless) ?? "Go Endless";
                AddItem(label, () => { try { screen.OnEndlessButton(); } catch { } });
            }
        }
        catch { }
    }

    private string GetButtonLabel(UIButton button)
    {
        try
        {
            var tmp = button.GetComponentInChildren<TMP_Text>();
            if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                return TextHelper.CleanText(tmp.text);
        }
        catch { }
        return null;
    }

    // ===== EventSystem management =====

    private void BlockEventSystem()
    {
        try
        {
            var es = EventSystem.current;
            if (es != null)
            {
                eventSystemObject = es.gameObject;
                eventSystemObject.SetActive(false);
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"EndScreenReader BlockEventSystem error: {e.Message}");
        }
    }

    private void RestoreEventSystem()
    {
        try
        {
            if (eventSystemObject != null)
            {
                eventSystemObject.SetActive(true);
                eventSystemObject = null;
                restoreStep = 5;
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"EndScreenReader RestoreEventSystem error: {e.Message}");
            eventSystemObject = null;
        }
    }

    private void FinishRestore()
    {
        try
        {
            var es = EventSystem.current;
            if (es == null) return;

            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module != null)
            {
                module.enabled = false;
                module.enabled = true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log?.LogDebug($"EndScreenReader FinishRestore error: {e.Message}");
        }
    }

    void OnDestroy()
    {
        if (isActive || collectDelay >= 0)
            RestoreEventSystem();
        Instance = null;
    }
}
