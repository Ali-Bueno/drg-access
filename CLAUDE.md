# DRG Survivor Accessibility Mod

## Project Overview
Accessibility mod for **Deep Rock Galactic Survivor** using:
- **BepInEx 6** (IL2CPP)
- **Harmony** for patching
- **Tolk / TolkDotNet** for screen reader support

---

## Current Status

### Implemented Features
- **Menu Navigation**: Buttons announce their text when selected (all specialized button types supported)
- **Class Selection**: Reads class name, description, and base stats (HP, Evasion, Crit Chance, Crit Damage)
- **Subclass Selection**: Reads subclass name, stat bonuses, starter weapon info (name, description, stats, targeting, damage type)
- **Biome Selection**: Reads biome name and locked status
- **Hazard Level Selection**: Reads hazard level and locked status
- **Mutators**: Reads mutator name and description
- **Shop Items**: Reads item name, stats, and description
- **Toggle Settings**: Announces On/Off state when clicked
- **Tooltips**: Tooltip reading with rich text and serial number cleanup
- **Form Announcements**: Announces when forms/menus open (splash, play, settings, gear, stats, milestones, skins, pause, end screen, loading, popups, level up, overclock, unlock, progression summary, mutator, gear found/inspect, score)
- **Page Descriptions**: Reads description panels when selecting game modes, masteries, anomalies, and missions
- **Biome Selection**: Reads biome name, lore/description, and high score when selecting mission nodes (biomes)
- **Settings Menu**: Sliders (label + value on focus, value-only on change), toggles (label + On/Off state), selectors (label + value + direction), tab navigation (PageLeft/PageRight)
- **Settings Focus Tracking**: MonoBehaviour polls EventSystem for focus changes on non-button controls (sliders, toggles, generic selectables). Coordinates with SetValueText patch via frame counter to avoid double announcements
- **Step Selectors**: Left/right selector buttons announce label, current value, and direction (Previous/Next)
- **Stat Upgrades**: Reads localized title, description, stat type + value, level, cost, and affordability
- **Gear Inventory**: Reads gear name, slot type, rarity, tier, stat mods, and quirk descriptions
- **Mineral Market**: Reads localized button text instead of raw enum names
- **Localized Game Data**: Stat names, rarity names, and gear slot types use the game's own localization system (StatSettingCollection, UiRarityData, LocalizedResources) with English fallbacks
- **Serial Number Cleanup**: Removes "nº XX-XXX-XXX" patterns from all text outputs (Fixed Run descriptions)
- **Gameplay Audio - Wall Navigation**: Continuous tones for wall detection in 4 directions (forward, back, left, right) with volume based on proximity
  - Forward: 500 Hz (medium tone)
  - Back: 180 Hz (low tone)
  - Left/Right: 300 Hz (medium-low tone) with stereo panning
  - Volume increases as walls get closer
- **Gameplay Audio - Enemy Detection**: 3D positional beeps for enemies with type differentiation
  - Normal enemies: short high beeps (500-1000 Hz)
  - Elite enemies: medium beeps with vibrato (150-300 Hz)
  - Boss enemies: long deep tones (60-120 Hz)
  - Pitch adjusts with distance (closer = higher) and height (above = higher, below = lower)
  - 8-directional detection with stereo panning

### Known Issues
- [ ] Biome statistics panel (complete exploration, weapon level, gold requirements, etc.) not being read - needs investigation of the UI structure to find where these stats are displayed

### Pending Improvements
- [ ] In-game HUD reading (health, XP, wave, etc.)
- [ ] Level-up skill selection improvements
- [ ] Death/victory announcements
- [ ] Settings: tab content accessibility for remaining pages
- [ ] Gameplay audio fine-tuning based on testing

---

## Project Structure

```
drgAccess/
├── Plugin.cs                  # Main plugin entry point
├── ScreenReader.cs            # Tolk wrapper for screen reader output
├── SettingsFocusTracker.cs    # MonoBehaviour polling EventSystem for settings focus
├── Components/
│   ├── WallNavigationAudio.cs # Wall detection with continuous tones
│   ├── EnemyAudioSystem.cs    # 3D positional audio for enemies
│   └── EnemyTracker.cs        # Tracks active enemies in scene
├── Patches/
│   ├── UIButtonPatch.cs       # All button types (class, subclass, shop, selectors, etc.)
│   ├── UIFormPatches.cs       # Form/menu announcements
│   ├── UIPageDescriptionPatches.cs  # Page description panel reading
│   ├── UISettingsPatch.cs     # Settings sliders, toggles, selectors, tabs
│   ├── UITooltipPatch.cs      # Tooltip reading
│   ├── UISliderTogglePatch.cs # Toggle state announcements
│   └── EnemyPatches.cs        # Enemy registration for audio system
└── drgAccess.csproj           # Project file

drg code/                      # Decompiled game code for reference (not included in repo)
references/tolk/               # Tolk DLL references
```

---

## Dependencies

- **BepInEx 6.x** (IL2CPP version)
- **NAudio 2.2.1** for procedural audio generation (walls and enemy detection)
- **Tolk** screen reader library
  - `TolkDotNet.dll` in plugins folder
  - `Tolk.dll` and `nvdaControllerClient64.dll` in game root folder

---

## Key Classes Patched

| UI Class | Purpose |
|----------|---------|
| `UIButton` | Base button class |
| `UIClassSelectButton` | Main class selection (Driller, Scout, etc.) |
| `UIClassArtifactButton` | Subclass selection (Classic, Sniper, etc.) |
| `UISkillButton` | Level-up upgrades and weapons |
| `UIShopButton` | Shop items |
| `UIBiomeSelectButton` | Biome selection |
| `UIHazLevelButton` | Hazard level selection |
| `UIMutatorView` / `UIMutatorButton` | Hazard modifiers |
| `UIStatUpgradeButton` | Stat upgrade menu (localized title/desc, stat values, cost) |
| `UIMineralMarketButton` | Mineral market (reads localized TMP children) |
| `UIGearViewCompact` | Gear inventory (name, rarity, stats, quirks) |
| `UISliderToggle` | Toggle settings |
| `UITooltip` | Tooltips |
| `UISettingsSlider` | Settings sliders (label + value) |
| `UISettingsPageGameplay` | Gameplay toggle callbacks |
| `UISettingsPageVideo` | Video selector callbacks |
| `UISettingsForm` | Settings tabs (PageLeft/PageRight) |
| `StepSelectorBase` | Left/right selector buttons |
| Various `UIForm` subclasses | Menu/form announcements |
| Various page classes | Description panel reading |

---

## IL2CPP / Harmony Critical Rules

**NEVER patch base Unity/IL2CPP class methods** like `Selectable.OnSelect`, `UIFormTabbedPageAlbum.OnPageChanged`, etc. These fire during early IL2CPP initialization before types are ready, causing **fatal AccessViolationException** crashes that cannot be caught in .NET 6. The game will silently fail to open.

**NEVER use `TryCast<T>()` or `GetComponentInChildren<T>()` inside patches on base class methods** — same crash reason. Only use these in patches on specific game classes (e.g., `UISettingsSlider.SetValueText`).

**Harmony postfix parameter names MUST match the original method exactly.** If the game method has `bool value`, your postfix cannot use `bool toggle`. Safest approach: don't capture the parameter at all — read state from the instance instead (e.g., `Toggle.isOn`).

**Forms that don't override `SetVisibility`** cannot be patched with `[HarmonyPatch(typeof(X), nameof(X.SetVisibility))]`. Harmony's DeclaredMethod only finds methods declared on the type, not inherited. Use their actual declared methods (Setup, Show, SetupOverclock, etc.).

**Overloaded methods** need `TargetMethod()` approach to disambiguate. Use `GetMethods()` and filter by parameter count.

**Some native method detours crash the game** even with empty postfixes. Known: `UISettingsPageVideo.OnToggleVsync` — the native detour itself corrupts something. No workaround except avoiding the patch entirely. The Vsync toggle is handled by `SettingsFocusTracker` instead.

**Slider labels are grandchildren** of UISettingsSlider: `UISettingsSlider → MasterSlider → NameText (TMP)`. Use `GetComponentsInChildren<TMP>()` skipping `valueText`, not sibling searches.

**Target Framerate slider** has `textMultiplier=100`, making `valueText` show "6000" instead of "60". Corrected by name check (`Target_Framerate_SettingsSlider`) — do NOT apply generic textMultiplier correction as other sliders use it legitimately (e.g. volume 0-1 → 0-100).

**Rule of thumb:** Only patch methods that are **declared directly on the target class**, not inherited from base classes. When in doubt, check the decompiled code.

**Localization approach:** Always prefer the game's own localized text. Use `LocalizedString.GetLocalizedString()`, `StatSettingCollection.Get(statType).GetDisplayName`, `UiRarityData.GetRarityName(rarity)`, `LocalizedResources.GearType*` etc. Only mod-created messages (labels like "Stats:", "Level", "Cost:") should be in English. Cache ScriptableObject singletons via `Resources.FindObjectsOfTypeAll<T>()` with a "searched" flag to avoid repeated lookups.

---

## Code Conventions

- All code in **English**
- All comments in **English**
- Commits in **English**
- Avoid `FindObjectOfType` in frequent calls (performance)
- Handle exceptions gracefully with logging

---

## Building

```bash
cd drgAccess
dotnet build
```

The build automatically copies the DLL to:
`D:\games\steam\steamapps\common\Deep Rock Survivor\BepInEx\plugins\drgAccess\`

---

## Goal

Enable a blind player to navigate menus, select classes/loadouts, and eventually play Deep Rock Galactic Survivor with full screen reader support.
