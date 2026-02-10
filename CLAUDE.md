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
- **Stat Upgrades**: Reads localized title, description, stat type + correctly formatted value (percentage stats ×100), level, cost, and affordability
- **Gear Inventory**: Reads gear name, slot type, rarity, tier, correctly formatted stat mods, and quirk descriptions
- **Level-Up Skill Selection**: Reads skill name, rarity (Common/Uncommon/Rare/Epic/Legendary), stats, and description
- **Mineral Market**: Reads localized button text instead of raw enum names
- **Localized Game Data**: Stat names, rarity names, and gear slot types use the game's own localization system (StatSettingCollection, UiRarityData, LocalizedResources) with English fallbacks
- **Serial Number Cleanup**: Removes "nº XX-XXX-XXX" patterns from all text outputs (Fixed Run descriptions)
- **Gameplay Audio - Wall Navigation**: Continuous tones for wall detection in 4 directions (forward, back, left, right) with volume based on proximity
  - Forward: 500 Hz (medium tone)
  - Back: 180 Hz (low tone)
  - Left/Right: 300 Hz (medium-low tone) with stereo panning
  - Volume increases as walls get closer
- **Gameplay Audio - Enemy Detection**: 3D positional beeps for enemies with type differentiation
  - Normal enemies (including MINI_ELITE): Pure sine wave, 700-1400 Hz, 50ms duration
  - Elite enemies (ELITE only): Triangle wave + 2nd harmonic, 200-400 Hz, 180ms, 15Hz vibrato
  - Boss enemies: Square wave + sub-bass, 40-100 Hz, 350ms, dramatic pitch descent
  - Critical proximity warning (< 3.5m): Faster beeps, boosted volume, higher pitch for urgency
  - 8-directional detection with stereo panning
  - Note: MINI_ELITE classified as normal to avoid confusion (they're common)
- **Gameplay Audio - Drop Pod Beacon**: Chirp beeps for extraction pod location
  - Uses BeaconBeepGenerator with descending-frequency chirp (distinct from enemy flat beeps)
  - Accelerating interval: 250ms (far) → 30ms (very close)
  - 3D positional audio with distance-based volume (0.25-0.45) and frequency (500-900 Hz)
  - Critical proximity (< 8m): Double-beep pattern ("dit-DIT"), higher pitch (900-1200 Hz), louder, screen reader announces "Drop pod very close"
  - Stops when player enters pod
  - Landing warning: Rapid urgent chirps (1000-1400 Hz, 80ms interval) when pod is descending (5 seconds)
  - Only activates for extraction pod (not initial drop pod)
- **Gameplay Audio - Supply Pod Beacon**: Chirp beeps for ActivationZone (supply pod zones)
  - Uses BeaconBeepGenerator with descending-frequency chirp (same as drop pod)
  - Accelerating interval: 250ms (far) → 30ms (very close)
  - 3D positional audio with distance-based volume (0.2-0.38) and frequency (350-650 Hz)
  - Lowest frequency range to distinguish from enemies and drop pod
  - Stops when player is inside zone or zone is activating
  - Detects nearest active zone within 100m
- **Gameplay Audio - Hazard Warning**: Siren alarm for nearby dangers
  - Exploders (horde enemies): Detected by name, alarm at 900-1400 Hz within 8m range
  - Ground Spikes (Dreadnought boss): Registered via patch on GroundSpike.OnSpawn, alarm at 600-1000 Hz
  - Alarm uses oscillating frequency (siren effect) clearly distinct from enemy beeps
  - Alarm rate increases with proximity (5-25 Hz oscillation)
  - Stereo panning toward the hazard direction

### Known Issues
- [ ] Biome statistics panel (complete exploration, weapon level, gold requirements, etc.) not being read - needs investigation of the UI structure to find where these stats are displayed

### Pending Improvements
- [ ] In-game HUD reading (health, XP, wave, etc.)
- [ ] Death/victory announcements
- [ ] Settings: tab content accessibility for remaining pages

---

## Project Structure

```
drgAccess/
├── Plugin.cs                      # Main plugin entry point
├── ScreenReader.cs                # Tolk wrapper for screen reader output
├── SettingsFocusTracker.cs        # MonoBehaviour polling EventSystem for settings focus
├── Helpers/
│   ├── TextHelper.cs              # Shared text cleaning (CleanText, IsJustNumber)
│   └── LocalizationHelper.cs      # Cached localization lookups (stats, rarity, gear slots, formatting)
├── Components/
│   ├── WallNavigationAudio.cs     # Wall detection with continuous tones
│   ├── EnemyAudioSystem.cs        # 3D positional audio for enemies
│   ├── EnemyTracker.cs            # Tracks active enemies in scene
│   ├── DropPodAudio.cs            # Drop pod beacon + BeaconBeepGenerator (chirp beeps)
│   ├── ActivationZoneAudio.cs     # Supply pod zone beacon (chirp beeps)
│   └── HazardWarningAudio.cs      # Hazard warning siren (exploders, ground spikes)
├── Patches/
│   ├── UIButtonPatch.cs           # Core button dispatch + simple handlers (partial class)
│   ├── UIButtonPatch.ClassSelection.cs  # Class/subclass button text (partial)
│   ├── UIButtonPatch.Mission.cs   # Mission/campaign/challenge buttons (partial)
│   ├── UIButtonPatch.Gear.cs      # Gear inventory + stat upgrades (partial)
│   ├── UIFormPatches.cs           # Form/menu announcements
│   ├── UIPageDescriptionPatches.cs  # Page description panel reading
│   ├── UISettingsPatch.cs         # Settings sliders, toggles, selectors, tabs
│   ├── UITooltipPatch.cs          # Tooltip reading
│   ├── UISliderTogglePatch.cs     # Toggle state announcements
│   ├── EnemyPatches.cs            # Enemy registration for audio system
│   ├── DropPodPatches.cs          # Drop pod event detection (landing/extraction)
│   └── HazardPatches.cs           # Ground spike detection for hazard warnings
└── drgAccess.csproj               # Project file

drg code/                          # Decompiled game code for reference (not included in repo)
references/tolk/                   # Tolk DLL references
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
| `GroundSpike` | Ground spike hazard detection (Dreadnought boss attack) |

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

**Percentage stat values are stored as fractions** (0.05 = 5%). Always use `LocalizationHelper.FormatStatValue()` which multiplies by 100 for percentage stats. Never format stat values with raw `{value:0}%` directly.

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
