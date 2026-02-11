# DRG Survivor Accessibility Mod

## Project Overview
Accessibility mod for **Deep Rock Galactic Survivor** using:
- **BepInEx 6** (IL2CPP)
- **Harmony** for patching
- **Tolk / TolkDotNet** for screen reader support

---

## GitHub & Releases

- **Repository**: https://github.com/Ali-Bueno/drg-access
- **Latest release page**: https://github.com/Ali-Bueno/drg-access/releases/latest
- **Current version**: v0.3.0
- **Permanent download links** (always point to latest release):
  - Full: https://github.com/Ali-Bueno/drg-access/releases/latest/download/DRGAccess-full.zip
  - Plugin only: https://github.com/Ali-Bueno/drg-access/releases/latest/download/DRGAccess-plugin-only.zip
- **Release folders**:
  - `DRGAccess release full/` — contains everything (BepInEx, NAudio, Tolk, the mod DLL, unity-libs, interop). For first-time install.
  - `DRGAccess release plugin only/` — contains only BepInEx/plugins, README, and CHANGELOG. For users updating an existing install.
- **Release process**: Create zips with fixed names (`DRGAccess-full.zip`, `DRGAccess-plugin-only.zip`), then `gh release create vX.Y.Z *.zip --title "..." --notes "..."`. The permanent download links always resolve to the latest release automatically.

---

## Current Status

### Implemented Features
- **Menu Navigation**: Buttons announce their text when selected (all specialized button types supported)
- **Class Selection**: Reads class name, description, and base stats (HP, Evasion, Crit Chance, Crit Damage)
- **Subclass Selection**: Reads subclass name, stat bonuses, starter weapon info (name, description, stats, targeting, damage type)
- **Biome Selection**: Reads biome name and locked status
- **Hazard Level Selection**: Reads hazard level and locked status
- **Mutators**: Reads mutator name and description
- **Shop Screen (Inter-Level)**: Full accessibility for the between-levels shop after extraction
  - Form announcement ("Shop") when opened
  - Shop buttons read: weapon name (if weapon-specific), skill name, rarity, stats, description, price with currency name (e.g. "50 Gold"), affordability
  - Empty slots announce "Empty"
  - Pin toggles: "Pin [item name], Pinned/Unpinned" — detected by matching UIShopScreen.shopButtons[i].pinnedToggle (toggles aren't children of shop buttons in hierarchy)
  - Reroll/Heal buttons (`UIButtonPrice`): reads label + price with currency name + affordability (e.g. "Heal 50%. Price: 30 Gold")
  - Purchase feedback: "Purchased [name]" / "Cannot afford"
  - Reroll feedback: "Rerolled" / "Cannot afford reroll"
  - Heal feedback: "Healed" / "Cannot afford heal"
  - G key reads wallet balance (Gold always shown even when 0, plus all other currencies > 0)
- **Toggle Settings**: Announces label + On/Off state when clicked (finds label from children/siblings)
- **Tooltips**: Tooltip reading with rich text and serial number cleanup
- **Splash Screen (Press Any Key)**: Announces the "press any key" prompt only after intro videos finish (patches `UISplashForm.AdvanceFlow`, checks `flow == SPLASH`)
- **Brightness Adjustment (First Launch)**: Full accessibility for the GammaAdjuster screen
  - Announces "Brightness Adjustment" + instructions + slider label/value on open
  - Slider reads value on left/right arrow changes (via existing `SetValueText` patch)
  - Slider reads label + value on focus via `SettingsFocusTracker` (up/down navigation)
  - "Brightness confirmed" feedback on OK
  - `GammaAdjusterOpen` flag suppresses "Main Menu" announcement while brightness screen is on top
  - `SuppressUntilFrame` prevents focus tracker from interrupting the initial announcement
- **Save Slot Selection**: Reads save slot details when selecting a save file
  - Slot number, rank, total dives, mission goals completed, last saved date
  - Empty slots announce "Empty"
  - Delete confirmation dialog announced, with "Save deleted" / "Cancelled" feedback
- **Form Announcements**: Announces when forms/menus open (splash, play, settings, gear, stats, milestones, skins, pause, end screen, loading, popups, level up, overclock, unlock, progression summary, mutator, gear found/inspect, score, shop, save slot selector)
- **Page Descriptions**: Reads description panels when selecting game modes, masteries, anomalies, and missions
- **Biome Selection**: Reads biome name, lore/description, and high score when selecting mission nodes (biomes)
- **Settings Menu**: Sliders (label + value on focus, value-only on change), toggles (label + On/Off state), selectors (label + value + direction), tab navigation (PageLeft/PageRight)
- **Settings Focus Tracking**: MonoBehaviour polls EventSystem for focus changes on non-button controls (sliders, toggles, generic selectables). Coordinates with SetValueText patch via frame counter to avoid double announcements. Also active during GammaAdjuster screen. Supports `SuppressUntilFrame` to avoid interrupting screen open announcements
- **Step Selectors**: Left/right selector buttons announce label, current value, and direction (Previous/Next)
- **Stat Upgrades**: Reads localized title, description, stat type + correctly formatted value (percentage stats ×100), level, cost, and affordability
- **Gear Inventory**: Reads gear name, slot type, rarity, tier, correctly formatted stat mods, and quirk descriptions
- **Level-Up Skill Selection**: Reads weapon name (if weapon-specific), skill name, rarity (Common/Uncommon/Rare/Epic/Legendary), stats, and description
- **Mineral Market**: Reads localized button text instead of raw enum names. Action feedback: "Bought"/"Sold" on success, "Cannot afford"/"Nothing to sell" on failure
- **Action Feedback**: Screen reader announces results when pressing Enter on actionable buttons:
  - Mineral market: "Bought" / "Cannot afford" / "Sold" / "Nothing to sell"
  - Stat upgrades: "Upgraded to level X/Y" / "Max level reached" / "Cannot afford"
  - Gear: "Equipped [name]" / "Unequipped [name]"
  - Shop: "Purchased [name]" / "Cannot afford" / "Rerolled" / "Cannot afford reroll" / "Healed" / "Cannot afford heal"
- **Wallet Reading**: Press G in the stat upgrades menu or shop screen to hear all currency balances (Gold, Credits, minerals, special currencies)
- **HP Reading**: Press H during gameplay to hear current and max HP (e.g. "HP: 85 / 120")
- **Localized Game Data**: Stat names, rarity names, gear slot types, and currency names use the game's own localization system (StatSettingCollection, UiRarityData, LocalizedResources) with English fallbacks
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
  - Rare loot enemies (Golden Lootbug, Huuli Hoarder only): Bright ascending chime, 1500-2200 Hz, 120ms
  - Common lootbugs (LOOTBUG) are filtered out to avoid audio spam — they are too numerous
  - Critical proximity warning (< 3.5m): Faster beeps, boosted volume, higher pitch for urgency
  - 8-directional detection with stereo panning + **directional pitch modulation** (higher when enemy is ahead, lower when behind)
  - Enemy name announcements via screen reader: new enemy types announced when first detected, cooldown of 3 seconds between announcements
  - Note: MINI_ELITE classified as normal to avoid confusion (they're common)
- **Gameplay Audio - Drop Pod Beacon**: Metallic sonar ping guiding to extraction pod ramp
  - Targets the pod's **ramp position** (`rampDetector` Transform) — the specific entry side, not pod center
  - Uses MixingSampleProvider with BeaconBeepGenerator (sonar ping: detuned oscillators with 5th harmonic, reverberant decay) + SineWaveGenerator (ramp proximity tone)
  - Accelerating interval: 250ms (far) → 30ms (very close)
  - 3D positional audio with distance-based volume (0.25-0.45) and frequency (800-1400 Hz)
  - **Top-down pitch modulation**: Higher pitch when pod is ahead (W direction, 1.0x), lower when behind (S direction, 0.4x) — pronounced range for clear orientation
  - Critical proximity (< 8m): Double-beep pattern ("dit-DIT"), higher pitch (1200-1600 Hz), louder, screen reader announces "Drop pod very close"
  - **Ramp proximity (< 2.5m)**: Continuous tone (1200-1600 Hz) pans toward pod interior (`playerPoint` — the exact spot that triggers departure), screen reader announces "On the ramp, follow the tone inside"
  - **Inside pod**: All audio stops, screen reader announces "Inside the pod"
  - **NavMesh pathfinding**: Uses Unity's NavMesh to calculate paths around walls/obstacles instead of pointing in a straight line. Beacon guides toward next path waypoint. Falls back to direct targeting if path fails or within 8m of pod. Path recalculated every 0.5s via `NavMeshPathHelper`
  - **F key compass**: Announces screen-relative direction (up/down/left/right/diagonals) + path distance to ramp, adapted for top-down perspective (directions correspond to WASD movement)
  - Only activates for extraction pod (not initial drop pod)
- **Gameplay Audio - Supply Pod Beacon**: Warbling trill for ActivationZone (supply pod zones)
  - Uses BeaconBeepGenerator (warble: 18 Hz frequency oscillation, sawtooth+sine mix with sub-octave)
  - Accelerating interval: 250ms (far) → 30ms (very close)
  - 3D positional audio with distance-based volume (0.2-0.38) and frequency (350-650 Hz) + **directional pitch modulation**
  - Clearly distinct from drop pod beacon (different waveform and frequency range)
  - **Zone enter/exit announcements**: "Inside supply zone" / "Left supply zone" (+ "return to zone" if activating)
  - **Beacon stays active when ACTIVATING + player outside**: guides player back to zone
  - Silent when player is inside zone (player is where they need to be)
  - **State announcements**: "Clearing zone, X rocks to mine" on activation start, "Supply zone complete" on done
  - **Progress feedback**: remaining rocks announced as they're mined, timer announced every 10 seconds
  - Detects nearest active zone within 100m
- **Gameplay Audio - Hazard Warning**: Siren alarm for nearby dangers
  - Exploders (horde enemies): Detected by name, alarm at 900-1400 Hz within 8m range
  - Ground Spikes (Dreadnought boss): Registered via patch on GroundSpike.OnSpawn, alarm at 600-1000 Hz
  - Alarm uses oscillating frequency (siren effect) clearly distinct from enemy beeps
  - Alarm rate increases with proximity (5-25 Hz oscillation)
  - Stereo panning toward the hazard direction + **directional pitch modulation**
- **Gameplay Audio - Collectible Items**: 3D positional audio for pickups, mineral veins, and loot crates
  - 7 distinct sound categories, each with unique synthesis: Red Sugar (water-drop bloop 500-750 Hz), Gear Drop (two-tone chord 800-1200 Hz), Buff Pickup (FM synthesis buzz 1000-1500 Hz), Currency (crystalline chime 600-900 Hz), Mineral Vein (metallic clink 300-500 Hz), Loot Crate (shimmering sparkle 1200-1800 Hz), XP Nearby (triangle wave + tremolo 350-700 Hz)
  - Detection distances: 8-18m depending on importance (gear/crates 18m, buffs 12m, XP 8m)
  - Only the nearest item per category gets audio (max 7 simultaneous sounds)
  - Beacon-style accelerating beeps for all pickups/crates/minerals, continuous tone for XP only
  - XP uses triangle wave with 6 Hz tremolo to distinguish from wall detection sine tones
  - Stereo panning toward the target, volume and frequency increase with proximity + **directional pitch modulation**
  - Uses 1 WaveOutEvent + 1 MixingSampleProvider with 7 CollectibleSoundGenerator channels
- **Objective Announcements**: Mission objectives announced via screen reader
  - Objective text announced when it first appears (Show)
  - Progress updates announced with 3-second throttle to avoid spam (OnProgress)
  - Objective completion announced with interrupt priority (OnObjectiveComplete)
- **Unlock Screen Accessibility**: Weapon/artifact/mastery unlock details announced when unlocked (patches ShowMilestone and ShowMastery)
- **Audio Cue Preview Menu**: Standalone menu to preview all audio cues outside gameplay
  - Opens with Backspace (only when NOT in active gameplay), closes with Backspace/Escape
  - Navigate with W/S or Up/Down arrows, preview with Enter
  - 19 cues: Wall Forward/Backward/Sides, Enemy Normal/Elite/Boss, Rare Loot Enemy, Drop Pod Beacon/Critical/Ramp Tone, Supply Pod Beacon, Hazard Warning, Collectible Red Sugar/Gear/Buff/Currency/Mineral Vein/Loot Crate/XP Nearby
  - Each item announces name + description via screen reader, Enter plays ~1.5s audio preview
  - Deactivates EventSystem while open to block game UI input, toggles InputSystemUIInputModule on close to restore navigation
  - Uses shared WaveOutEvent + MixingSampleProvider, created on menu open, disposed on close
- **Milestones Menu**: Arrow-key navigable reader for milestone progress
  - Activates when milestone form opens, collects all visible milestones
  - Navigate with W/S or Up/Down arrows to browse milestones
  - Reads: description, progress (X/Y), completion state, reward, requirements
  - Auto-refreshes when changing tabs (All, Weapons, Artifacts, Classes, Challenges, Gear)
- **Pause Menu**: Reads weapon name/level on weapon select, artifact name on artifact select, all player stats (name + value) when pause form opens
- **End Screen (Death/Victory Stats)**: Arrow-key navigable reader for post-run statistics
  - Activates when end screen opens, collects all visible text into ordered list
  - Navigate with Up/Down arrows, Enter to activate buttons
  - Sections: title/result, class progression + XP, score/high score, credits, resources collected, weapon report (per weapon: name, level, stacks, damage, DPS), damage breakdown (per type), player stats, exploration stats
  - Action buttons at end: Retry (OnRetryButton), Continue (OnMenuButton), Go Endless (OnEndlessButton, endless mode only)
  - Blocks EventSystem while active, restores on button activation
  - The "Continue" button has no field on UIEndScreen (connected via Inspector), calls OnMenuButton() directly
- **HP Reader**: Press H during active gameplay to hear current/max HP (e.g. "HP: 85 / 120"), falls back to percentage if MAX_HP stat unavailable
- **Directional Pitch Modulation**: All spatial audio cues (drop pod, supply pod, enemies, hazards, collectibles) use forward/behind pitch modulation — higher pitch when target is ahead (W/up), lower when behind (S/down). Uses shared `AudioDirectionHelper` to avoid code duplication. Drop pod uses pronounced 0.4x–1.0x range; all others use 0.6x–1.0x
- **Pickup Announcements**: Screen reader announces pickups during gameplay via CoreGameEvents patches
  - Heals: "Healed X HP" (only HEAL type — skips REGEN and MAX_HP to avoid spam on stat setup/game start)
  - Currency: "X [currency name]" with 2-second cooldown per currency type to avoid rapid-pickup spam
  - Gear: "Picked up [gear name]" via GearData.GetTitle()
  - Loot crates: "[rarity] loot crate"

### Known Issues
- [ ] Biome statistics panel (complete exploration, weapon level, gold requirements, etc.) not being read - needs investigation of the UI structure to find where these stats are displayed

### Pending Improvements
- [ ] In-game HUD reading (health, XP, wave, etc.)
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
│   ├── LocalizationHelper.cs      # Cached localization lookups (stats, rarity, gear slots, formatting)
│   ├── NavMeshPathHelper.cs       # NavMesh pathfinding for beacon guidance around obstacles
│   └── AudioDirectionHelper.cs    # Shared forward/behind pitch modulation for all audio cues
├── Components/
│   ├── WallNavigationAudio.cs     # Wall detection with continuous tones (1 shared WaveOutEvent)
│   ├── EnemyAudioSystem.cs        # 3D positional audio for enemies (1 shared WaveOutEvent)
│   ├── EnemyTracker.cs            # Tracks active enemies in scene
│   ├── DropPodAudio.cs            # Drop pod beacon + BeaconBeepGenerator (sonar ping / warble trill)
│   ├── ActivationZoneAudio.cs     # Supply pod zone beacon (warble trill)
│   ├── CollectibleAudioSystem.cs  # Collectible items/minerals/crates positional audio
│   ├── CollectibleSoundGenerator.cs # ISampleProvider for 7 collectible sound types
│   ├── HazardWarningAudio.cs      # Hazard warning siren (exploders, ground spikes)
│   ├── AudioCueMenu.cs            # Audio cue preview menu (Backspace to open/close)
│   ├── WalletReaderComponent.cs   # G key wallet balance reading (stat upgrades menu)
│   ├── HPReaderComponent.cs       # H key HP reading during gameplay
│   ├── EndScreenReaderComponent.cs # Arrow-key navigable end screen stats reader
│   └── MilestoneReaderComponent.cs # W/S navigable milestone reader
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
│   ├── UIButtonPatch.Pause.cs     # Pause menu weapon/artifact button text (partial)
│   ├── UICorePausePatch.cs        # Pause menu detail panels (weapon stats, artifact desc, player stats)
│   ├── UIActionFeedbackPatch.cs   # Action results (buy/sell, upgrade, equip/unequip, wallet reader)
│   ├── UIObjectivePatches.cs      # Objective announcements (show, progress, completion)
│   ├── EnemyPatches.cs            # Enemy registration for audio system
│   ├── DropPodPatches.cs          # Drop pod event detection (landing/extraction)
│   ├── HazardPatches.cs           # Ground spike detection for hazard warnings
│   └── PickupAnnouncementPatches.cs # Pickup announcements (heal, currency, gear, loot crate)
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
| `UIMineralMarketButton` | Mineral market (reads localized TMP children, buy/sell feedback) |
| `GearManager` | Gear equip/unequip action feedback |
| `UIGearViewCompact` | Gear inventory (name, rarity, stats, quirks) |
| `UIShopScreen` | Inter-level shop (purchase, reroll, heal, wallet reading, pin toggle matching) |
| `UIButtonPrice` | Price buttons (shop heal/reroll — label, price, affordability) |
| `UISliderToggle` | Toggle settings + shop pin toggles |
| `UITooltip` | Tooltips |
| `UISettingsSlider` | Settings sliders (label + value) |
| `UISettingsPageGameplay` | Gameplay toggle callbacks |
| `UISettingsPageVideo` | Video selector callbacks |
| `UISettingsForm` | Settings tabs (PageLeft/PageRight) |
| `StepSelectorBase` | Left/right selector buttons |
| `UICorePauseForm` | Pause menu (weapon/artifact select, player stats) |
| `UIPauseWeapon` / `UIPauseArtifact` | Pause menu weapon/artifact buttons |
| `UIEndScreen` | End screen stats reader (arrow-key navigable) |
| `UIMilestoneForm` | Milestone form visibility + tab change refresh |
| `UIMilestoneProgress` | Individual milestone display (desc, progress, reward) |
| `UISplashForm` | Splash screen "press any key" (via AdvanceFlow, flow == SPLASH) |
| `GammaAdjuster` | First-launch brightness adjustment (Show, Hide, OnClickOK) |
| `UISaveSlot` | Save slot button (rank, dives, mission goals, last saved) |
| `UISaveSlotSelector` | Save slot selection screen + delete confirmation dialog |
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

**NAudio shared mixer architecture:** Too many simultaneous `WaveOutEvent` instances (~30+) overwhelms Windows audio handles, causing ~10 second startup delay before any audio plays. Components that need multiple audio channels (WallNavigationAudio: 4 directions, EnemyAudioSystem: 8 directions × 4 types = 32 channels) must use a single shared `MixingSampleProvider` + one `WaveOutEvent`. Individual generators are added as mixer inputs and controlled independently via their Volume/Pan properties. **Important:** With a shared mixer, beeps triggered on the same frame merge into one sound. Use `delaySamples` stagger (10ms per direction) in `EnemyAlertSoundGenerator.Play()` to offset beeps at the audio buffer level so they sound distinct.

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

Enable a blind player to fully play Deep Rock Galactic Survivor — menus, loadout setup, gameplay, and post-run screens — with screen reader support and spatial audio cues.
