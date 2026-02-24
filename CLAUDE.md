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
- **Current version**: v0.6.1
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
- **Class Selection**: Reads class name, description, and base stats (HP, Evasion, Crit Chance, Crit Damage). Locked classes announce unlock requirement (e.g. "Unlocks at player rank 3")
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
- **Settings Menu**: Sliders (label + value on focus, value-only on change), toggles (label + On/Off state), selectors (label + value on change via IncreaseIndex/DecreaseIndex), tab navigation (PageLeft/PageRight)
- **Settings Focus Tracking**: MonoBehaviour polls EventSystem for focus changes on non-button controls (sliders, toggles, generic selectables). Coordinates with SetValueText patch via frame counter to avoid double announcements. Also active during GammaAdjuster screen. Supports `SuppressUntilFrame` to avoid interrupting screen open announcements. Detects toggle value changes (for VSync and Target Framerate toggles that can't be patched directly) by comparing `isOn` state each frame
- **Step Selectors**: Left/right selector buttons announce label, current value, and direction (Previous/Next). Value changes announced via generic `StepSelectorBase.IncreaseIndex`/`DecreaseIndex` patches (all selectors: language, display, resolution, screen mode, anti-aliasing)
- **Gear Inventory Tabs**: Tab changes in gear inventory announced via `UITabGroup.SetActiveTab` patches, with frame delay to suppress default tab on form init. Button announcements queued after tab name to prevent cutoff
- **Stat Upgrades**: Reads localized title, description, stat type + correctly formatted value (percentage stats ×100), level, cost, and affordability
- **Gear Inventory**: Reads gear name, slot type, **equipped status**, rarity, tier, correctly formatted stat mods, and quirk descriptions. "Equipped" is announced early (right after name and slot type) so the user immediately knows the item's status. In upgrade tab: shows upgrade cost with currency names + affordability (via `GearEconomyConfig.TryGetUpgradeCost`). In sell tab: shows salvage value with currency names (via `GearEconomyConfig.TryGetSalvageValue`). Press T to hear a summary of all currently equipped gear organized by slot type
- **Level-Up Skill Selection**: Reads weapon name (if weapon-specific), skill name, rarity (Common/Uncommon/Rare/Epic/Legendary), stats, and description
- **Mineral Market**: Reads localized button text instead of raw enum names. Action feedback: "Bought"/"Sold" on success, "Cannot afford"/"Nothing to sell" on failure
- **Action Feedback**: Screen reader announces results when pressing Enter on actionable buttons:
  - Mineral market: "Bought" / "Cannot afford" / "Sold" / "Nothing to sell"
  - Stat upgrades: "Upgraded to level X/Y" / "Max level reached" / "Cannot afford"
  - Gear: "Equipped [name]" / "Unequipped [name]" / "Upgraded [name]" / "Salvaged [name]"
  - Shop: "Purchased [name]" / "Cannot afford" / "Rerolled" / "Cannot afford reroll" / "Healed" / "Cannot afford heal"
- **Wallet Reading**: Press G in the stat upgrades menu, shop screen, or gear inventory to hear all currency balances (Gold, Credits, minerals, special currencies)
- **HP Reading**: Press H during gameplay to hear current and max HP (e.g. "HP: 85 / 120")
- **Localized Game Data**: Stat names, rarity names, gear slot types, and currency names use the game's own localization system (StatSettingCollection, UiRarityData, LocalizedResources) with English fallbacks
- **Serial Number Cleanup**: Removes "nº XX-XXX-XXX" patterns from all text outputs (Fixed Run descriptions)
- **Gameplay Audio - Wall Navigation**: Continuous tones for wall detection in 4 directions (forward, back, left, right) with volume based on proximity
  - Forward: 500 Hz (medium tone)
  - Back: 180 Hz (low tone)
  - Left/Right: 300 Hz (medium-low tone) with stereo panning
  - Volume increases as walls get closer (base volume 0.12)
  - Detection range: configurable 5-25m (default 12m, via Mod Settings)
- **Gameplay Audio - Enemy Detection**: 3D positional beeps for enemies with type differentiation
  - Detection range: configurable 10-60m (default 35m, via Mod Settings)
  - Normal enemies (including MINI_ELITE): Pure sine wave, 700-1400 Hz, 50ms duration
  - Elite enemies (ELITE only): Triangle wave + 2nd harmonic, 200-400 Hz, 180ms, 15Hz vibrato
  - Boss enemies: Square wave + sub-bass, 40-100 Hz, 350ms, dramatic pitch descent
  - Rare loot enemies (Golden Lootbug, Huuli Hoarder only): Bright ascending chime, 1500-2200 Hz, 120ms
  - Common lootbugs (LOOTBUG) are filtered out to avoid audio spam — they are too numerous
  - Critical proximity warning (< 3.5m): Faster beeps, boosted volume, higher pitch for urgency
  - 8-directional detection with stereo panning + **directional pitch modulation** (higher when enemy is ahead, lower when behind)
  - Enemy name announcements via screen reader: new enemy types announced when first detected, cooldown of 3 seconds between announcements
  - **Proximity announcements**: Priority-based screen reader alerts for dangerous enemies
    - "Explosive very close!" / "Boss very close!" / "Elite very close!" — interrupt, < 8m, 3s cooldown
    - "Explosive nearby" / "Boss nearby" / "Elite nearby" — < 20m, 5s cooldown
    - "Enemy nearby" / "X enemies nearby" — on transition from 0 to some, 8s cooldown
    - Exploders detected by `EEnemy.EXPLODER` and `EEnemy.EXPLODER_FAST`
  - Note: MINI_ELITE classified as normal to avoid confusion (they're common)
- **Gameplay Audio - Drop Pod Beacon**: Metallic sonar ping guiding to extraction pod ramp
  - Targets the pod's **ramp position** (`rampDetector` Transform) — the specific entry side, not pod center
  - Uses MixingSampleProvider with BeaconBeepGenerator (sonar ping: detuned oscillators with 5th harmonic, reverberant decay) + SineWaveGenerator (ramp proximity tone)
  - Accelerating interval: 250ms (far) → 30ms (very close)
  - 3D positional audio with distance-based volume (0.25-0.45) and frequency (800-1400 Hz)
  - **Top-down pitch modulation**: Higher pitch when pod is ahead (W direction, 1.0x), lower when behind (S direction, 0.4x) — pronounced range for clear orientation
  - Critical proximity (< 8m): Double-beep pattern ("dit-DIT"), higher pitch (1200-1600 Hz), louder, screen reader announces "Drop pod very close"
  - **Ramp guidance zone (< 5m)**: Continuous tone (900-2200 Hz) targets playerPoint with directional pitch modulation. Volume and frequency respond to distance from playerPoint, not ramp. Both beacon beeps and tone pan toward playerPoint
  - **Very close (< 1.5m from playerPoint)**: Peak intensity (1800-2200 Hz), screen reader announces "Almost inside, keep going"
  - Screen reader announces "Near the pod, follow the tone inside" at 5m
  - **Inside pod**: All audio stops, screen reader announces "Inside the pod"
  - **NavMesh pathfinding**: Uses Unity's NavMesh to calculate paths around walls/obstacles instead of pointing in a straight line. Beacon guides toward next path waypoint. Falls back to direct targeting only within 3m AND with clear line of sight (raycast check). Path recalculated every 0.3s via `NavMeshPathHelper`
  - **F key compass**: Announces screen-relative direction (up/down/left/right/diagonals) + path distance to ramp, adapted for top-down perspective (directions correspond to WASD movement)
  - Only activates for extraction pod (not initial drop pod)
- **Gameplay Audio - Supply Pod Beacon**: Warbling trill for ActivationZone (supply pod zones)
  - Uses BeaconBeepGenerator (warble: 18 Hz frequency oscillation, sawtooth+sine mix with sub-octave)
  - Accelerating interval: 250ms (far) → 30ms (very close)
  - 3D positional audio with distance-based volume (0.35-0.55) and frequency (350-650 Hz) + **directional pitch modulation**
  - Clearly distinct from drop pod beacon (different waveform and frequency range)
  - **Zone enter/exit announcements**: "Inside supply zone" / "Left supply zone" (+ "return to zone" if activating)
  - **Beacon stays active when ACTIVATING + player outside**: guides player back to zone
  - Silent when player is inside zone (player is where they need to be)
  - **State announcements**: "Clearing zone, X rocks to mine" on activation start, "Supply zone complete" on done
  - **Progress feedback**: remaining rocks announced as they're mined, timer announced every 10 seconds
  - Detects nearest active zone within 100m
- **Gameplay Audio - Hazard Warning**: Multi-channel siren alarm for nearby dangers
  - Tracks up to 5 simultaneous hazards (configurable 1-5, default 3 via Mod Settings)
  - Each hazard gets its own audio channel with independent panning and frequency
  - Closest hazard is loudest, secondary channels at 70% volume
  - Exploders (horde enemies): Detected via EnemyTracker, alarm at 900-1400 Hz within 12m
  - Ground Spikes (Dreadnought boss): Registered via patch on GroundSpike.OnSpawn, alarm at 600-1000 Hz
  - Alarm uses oscillating frequency (siren effect) clearly distinct from enemy beeps
  - Alarm rate increases with proximity (5-25 Hz oscillation)
  - Warning range: configurable 10-50m (default 25m, via Mod Settings)
  - Stereo panning toward the hazard direction + **directional pitch modulation**
- **Gameplay Audio - Collectible Items**: 3D positional audio for pickups, mineral veins, and loot crates
  - 7 distinct sound categories, each with unique synthesis: Red Sugar (water-drop bloop 500-750 Hz), Gear Drop (two-tone chord 800-1200 Hz), Buff Pickup (FM synthesis buzz 1000-1500 Hz), Currency (crystalline chime 600-900 Hz), Mineral Vein (metallic clink 300-500 Hz), Loot Crate (shimmering sparkle 1200-1800 Hz), XP Nearby (triangle wave + tremolo 350-700 Hz)
  - Base detection distances: Red Sugar 30m, Gear/Loot Crate 40m, Buff 25m, Currency/Mineral Vein 28m, XP 8m
  - All distances multiplied by configurable range multiplier (0.5x-2.0x, default 1.0x via Mod Settings)
  - Only the nearest item per category gets audio (max 7 simultaneous sounds)
  - Beacon-style accelerating beeps for all pickups/crates/minerals, continuous tone for XP only
  - XP uses triangle wave with 6 Hz tremolo to distinguish from wall detection sine tones
  - Stereo panning toward the target, volume and frequency increase with proximity + **directional pitch modulation**
  - **Proximity announcements with direction**: Zone-based screen reader alerts as player approaches items
    - "Red Sugar nearby up-right" → "Red Sugar closer left" → "Red Sugar very close down"
    - Zones based on distance ratio: nearby (100-55%), closer (55-25%), very close (< 25%)
    - 8 directions adapted for top-down perspective (up/down/left/right + diagonals)
    - XP excluded from announcements (too common)
  - Uses 1 WaveOutEvent + 1 MixingSampleProvider with 7 CollectibleSoundGenerator channels
- **Objective Announcements**: Mission objectives announced via screen reader
  - Objective text announced when it first appears (Show)
  - Progress updates announced with 3-second throttle to avoid spam (OnProgress)
  - Objective completion announced with interrupt priority (OnObjectiveComplete)
- **Unlock Screen Accessibility**: Weapon/artifact/mastery unlock details announced when unlocked (patches ShowMilestone and ShowMastery)
- **Gamepad Support**: All mod-specific inputs support both keyboard and gamepad via shared `InputHelper`
  - D-Pad Up/Down: navigate custom menus (end screen, milestones, mod settings)
  - D-Pad Left/Right: adjust values in mod settings menu
  - A (buttonSouth): confirm/activate
  - B (buttonEast): close/back
  - Y (buttonNorth): toggle mod settings menu
  - LB (leftShoulder): read HP
  - RB (rightShoulder): read wallet balance
  - L3 (leftStickButton): compass direction to drop pod
- **Audio Cue Preview Menu**: Submenu within the Mod Settings Menu to preview all audio cues
  - Access via "Audio Cue Preview" item in Mod Settings (F1), navigate with Up/Down, preview with Enter / A
  - 24 cues: Wall Forward/Backward/Sides, Enemy Normal/Elite/Boss, Rare Loot Enemy, Drop Pod Beacon/Critical/Ramp Tone, Supply Pod Beacon, Drill Beacon, Hazard Warning, Boss Charge/Spikes/Fireball/Heal, Collectible Red Sugar/Gear/Buff/Currency/Mineral Vein/Loot Crate/XP Nearby
  - Each item announces name + description via screen reader, Enter plays ~1.5s audio preview
  - Escape / B button returns to main settings menu
  - Previews use pending volume values (unsaved changes applied during preview)
- **Milestones Menu**: Arrow-key navigable reader for milestone progress
  - Activates when milestone form opens, collects all visible milestones
  - Navigate with W/S or Up/Down arrows to browse milestones
  - Reads: description, progress (X/Y), completion state, reward, requirements
  - Auto-refreshes when changing tabs (All, Weapons, Artifacts, Classes, Challenges, Gear)
- **Pause Menu**: Arrow-key navigable reader for pause screen (similar to end screen reader)
  - Activates when pause form opens, blocks EventSystem to prevent game UI conflicts
  - Sections: Weapons (name, level, stats, tags, upgrades), Artifacts (name, description), Player Stats (individual items), Buttons (Resume, Menu, Settings)
  - Navigate with Up/Down arrows, Enter to select, Escape/B to resume
  - Fixes first-level navigation bug where stats overlay blocked pause menu buttons
  - **Overlay resume**: Reader properly resumes after opening Settings or Menu (abort popup) from pause. Uses patches on `UISettingsForm.SetVisibility(false)` and `UIAbortPopupForm.HidePopup` to detect overlay close, since `UICorePauseForm.Show()` is called native-to-native (IL2CPP bypass). Short `resumeDelay` lets the pause form re-appear before re-blocking EventSystem
- **End Screen (Death/Victory Stats)**: Arrow-key navigable reader for post-run statistics
  - Activates when end screen opens, collects all visible text into ordered list
  - Navigate with Up/Down arrows, Enter to activate buttons
  - Sections: title/result, class progression + XP, score/high score, credits, resources collected, weapon report (per weapon: name, level, stacks, damage, DPS), damage breakdown (per type), player stats, exploration stats
  - Action buttons at end: Retry (OnRetryButton), Continue (OnMenuButton), Go Endless (OnEndlessButton, endless mode only)
  - Blocks EventSystem while active, restores on button activation
  - The "Continue" button has no field on UIEndScreen (connected via Inspector), calls OnMenuButton() directly
- **HP Reader**: Press H during active gameplay or in the shop to hear current/max HP (e.g. "HP: 85 / 120"), falls back to percentage if MAX_HP stat unavailable
- **Mod Settings Menu**: Configurable mod settings accessible with F1 key (outside gameplay)
  - Opens with F1 / Y button, closes with Escape / B button or Save/Cancel
  - Navigate with Up/Down arrows or D-Pad, adjust values with Left/Right arrows or D-Pad
  - **Volume Sliders**: 9 categories (Wall Navigation, Enemy Detection, Drop Pod Beacon, Supply Pod Beacon, Hazard Warning, Collectibles, Footsteps, Boss Attacks, Drill Beacon) — 0-100% in 5% steps
  - **Toggle Settings**: Footsteps On/Off (enabled by default)
  - **Detection Settings**: Configurable ranges and limits:
    - Enemy Detection Range (10-60m, default 35m)
    - Hazard Warning Range (10-50m, default 25m)
    - Collectible Range Multiplier (0.5x-2.0x, default 1.0x)
    - Wall Detection Range (5-25m, default 12m)
    - Max Hazard Warnings (1-5 simultaneous, default 3)
  - **Audio Cue Preview submenu**: All 24 audio cue previews (moved from standalone menu), Enter to preview
  - Save persists to `drgAccess_settings.cfg` next to the mod DLL
  - Cancel restores previous values via snapshot system
  - Previews respect pending (unsaved) volume values
  - Blocks EventSystem while open to prevent game UI interaction
- **Master Volume Sync**: Mod audio cue volumes automatically scale with the game's master volume setting
  - All `ModConfig.GetVolume()` calls multiply category volume by game master volume
  - Patches `AudioMastering.SetMasterVolume` (slider changes) and `OnSaveDataLoaded` (initial value from save)
  - Both patches needed because IL2CPP native-to-native calls bypass Harmony on the initial load
- **Directional Pitch Modulation**: All spatial audio cues (drop pod, supply pod, enemies, hazards, collectibles) use forward/behind pitch modulation — higher pitch when target is ahead (W/up), lower when behind (S/down). Uses shared `AudioDirectionHelper` to avoid code duplication. Drop pod uses pronounced 0.4x–1.0x range; all others use 0.6x–1.0x
- **Footstep Audio**: Material-based footstep sounds during gameplay
  - Plays preloaded MP3 footstep sounds from `sounds/footsteps/` directory
  - **Stone** sounds during normal gameplay, **Metal** sounds when near the drop pod ramp (< 5m)
  - Fixed interval (~0.34s) with smoothed speed detection — stops when player is stationary or colliding
  - Random sound selection without consecutive repeats for natural variation
  - Toggleable via Mod Settings (Footsteps On/Off) with adjustable volume
  - Sounds loaded at startup, resampled to 44100 Hz stereo, with 1.8x base volume boost
- **Pickup Announcements**: Screen reader announces pickups during gameplay via CoreGameEvents patches
  - Heals: "Healed X HP" (only HEAL type — skips REGEN and MAX_HP to avoid spam on stat setup/game start)
  - Currency: "X [currency name]" with 2-second cooldown per currency type to avoid rapid-pickup spam
  - Gear: "Picked up [gear name]" via GearData.GetTitle()
  - Loot crates: "[rarity] loot crate"
- **Boss Attack Telegraphs**: Screen reader + audio warnings for Dreadnought boss attack patterns
  - Patches `DreadnoughtAnimator` telegraph methods (all declared directly, safe to patch)
  - 4 attack types with distinct screen reader announcements: "Charge!", "Spikes!", "Fireball!", "Healing!"
  - 3-beep pattern audio with attack-specific speed, frequency, and waveform:
    - Charge: 3 fast beeps, 600 Hz (aggressive square+sine, 80ms beep / 100ms gap)
    - Spikes: 3 medium beeps, 350 Hz (deep rumble + sub-octave, 100ms beep / 180ms gap)
    - Fireball: 3 very fast beeps, 800 Hz (sharp sine, 60ms beep / 60ms gap)
    - Heal: 3 slow beeps, 250 Hz (gentle sine + harmonic, 120ms beep / 280ms gap)
  - Directional stereo panning toward boss position
  - Volume configurable via Boss Attacks slider in Mod Settings
- **Boss HP Tracking**: Automatic boss health percentage announcements
  - "Boss!" announced when boss health bar appears (`UIBossTopBar.Show`)
  - HP threshold announcements at 75%, 50%, 25%, 15%, 10%, 5% (via `UpdateFill` patch)
  - "Boss defeated!" on boss death (`OnOwnerDeath`)
  - "Boss healed" with 3-second cooldown to avoid spam (`OnHealed`)
- **Drill Beacon (Bobby)**: 3D positional audio beacon for escort missions
  - Tracks Bobby the drill via `FindObjectOfType<Bobby>()`, polls state in Update()
  - Rhythmic chug/pulse sound (triangle + pulse wave + sub-octave with amplitude tremolo)
  - Running state: fast 6 Hz tremolo, 250-450 Hz; Stopped state: slow 3 Hz tremolo, 200-350 Hz
  - State announcements: "Bobby arriving", "Bobby started", "Bobby stopped", "Bobby mining the heartstone", "Bobby is broken", "Bobby finished"
  - Fuel tracking: announces at 50%, 25%, 10% ("Fuel critical, 10 percent")
  - Progress tracking: announces at 25%, 50%, 75% of drill path
  - Player range: "Out of Bobby's range, get closer" with 5s cooldown
  - F key compass: direction + distance to Bobby (drop pod gets priority if active)
  - NavMesh pathfinding via `NavMeshPathHelper` (no conflict — Bobby during ESCORT, drop pod during extraction)
  - Volume configurable via Drill Beacon slider in Mod Settings
  - `BeaconMode.Drill` added to `BeaconBeepGenerator` with `DrillRunning` property
- **Objective Reader (O key)**: Press O during gameplay to hear all active objectives
  - Finds `UIObjectiveTracker` in scene, reads all visible `UIObjective` items
  - Announces description + progress for each (e.g. "Objective: Collect 10 Morkite: 7/10")
  - Multiple objectives joined with periods (e.g. "Objectives: Kill 5 elites: 3/5. Collect minerals: 12/20")

- **Mod Localization System**: All mod-specific UI strings are now localized via external text files
  - 22 languages supported (matching the game's localization): English, German, French, Spanish (Spain), Spanish (Latin America), Italian, Portuguese (Portugal), Portuguese (Brazil), Russian, Japanese, Korean, Chinese Simplified, Chinese Traditional, Dutch, Bulgarian, Czech, Hungarian, Polish, Romanian, Slovak, Turkish, Ukrainian
  - External `localization/*.txt` files with simple `key=value` format — users can freely edit to customize messages
  - Automatic locale detection via `UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale`
  - Runtime language switching: mod messages update automatically when the player changes the game language in settings
  - English fallback for any missing keys
  - `ModLocalization.Get(key)` and `ModLocalization.Get(key, args)` for all mod strings
  - ~360 localized string keys covering all mod UI: form announcements, boss telegraphs, drop pod/supply zone/drill beacon announcements, enemy proximity, collectible proximity, action feedback, wallet, pickups, HP reader, objectives, milestones, pause/end screen, mod settings menu, audio cue previews, gear labels, mission labels, save slots, and common UI terms

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
│   ├── ModLocalization.cs         # Mod string localization (loads localization/*.txt, locale detection, Get())
│   ├── NavMeshPathHelper.cs       # NavMesh pathfinding for beacon guidance around obstacles
│   ├── AudioDirectionHelper.cs    # Shared forward/behind pitch modulation for all audio cues
│   ├── InputHelper.cs             # Shared keyboard + gamepad input checking
│   └── ModConfig.cs               # Settings persistence (volumes, detection ranges, master volume sync)
├── Components/
│   ├── WallNavigationAudio.cs     # Wall detection with continuous tones (1 shared WaveOutEvent)
│   ├── EnemyAudioSystem.cs        # 3D positional audio for enemies (1 shared WaveOutEvent)
│   ├── EnemyTracker.cs            # Tracks active enemies in scene
│   ├── DropPodAudio.cs            # Drop pod beacon + BeaconBeepGenerator (sonar ping / warble trill)
│   ├── ActivationZoneAudio.cs     # Supply pod zone beacon (warble trill)
│   ├── CollectibleAudioSystem.cs  # Collectible items/minerals/crates positional audio
│   ├── CollectibleSoundGenerator.cs # ISampleProvider for 7 collectible sound types
│   ├── HazardWarningAudio.cs      # Multi-channel hazard warning siren (exploders, ground spikes)
│   ├── BossAttackAudio.cs         # Boss attack telegraph charging sounds (rising-pitch alarm per type)
│   ├── FootstepAudio.cs           # Material-based footstep sounds (stone/metal MP3 playback)
│   ├── DrillBeaconAudio.cs        # Bobby drill beacon (escort mission positional audio)
│   ├── ObjectiveReaderComponent.cs # O key objective reader during gameplay
│   ├── ModSettingsMenu.cs         # Mod settings menu (F1 key, volumes, detection settings, audio cue preview)
│   ├── WalletReaderComponent.cs   # G key wallet balance reading (stat upgrades menu)
│   ├── HPReaderComponent.cs       # H key HP reading during gameplay
│   ├── EndScreenReaderComponent.cs # Arrow-key navigable end screen stats reader
│   ├── PauseReaderComponent.cs    # Arrow-key navigable pause menu reader
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
│   ├── UICorePausePatch.cs        # Pause menu reader activation/deactivation
│   ├── UIActionFeedbackPatch.cs   # Action results (buy/sell, upgrade, equip/unequip, wallet reader)
│   ├── UIObjectivePatches.cs      # Objective announcements (show, progress, completion)
│   ├── EnemyPatches.cs            # Enemy registration for audio system
│   ├── DropPodPatches.cs          # Drop pod event detection (landing/extraction)
│   ├── HazardPatches.cs           # Ground spike detection for hazard warnings
│   ├── BossAttackPatches.cs        # Boss attack telegraphs + HP threshold announcements
│   ├── PickupAnnouncementPatches.cs # Pickup announcements (heal, currency, gear, loot crate)
│   └── AudioMasteringPatch.cs     # Master volume sync (SetMasterVolume + OnSaveDataLoaded)
├── localization/                   # Mod string translations (22 language .txt files)
│   ├── en.txt                      # English (master/reference)
│   ├── de.txt, fr.txt, it.txt ...  # 21 other languages
├── sounds/
│   └── footsteps/
│       ├── stone/                    # 10 stone footstep MP3s
│       └── metal/                    # 10 metal footstep MP3s
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
| `GearManager` | Gear equip/unequip/upgrade/salvage action feedback |
| `UIGearViewCompact` | Gear inventory (name, rarity, stats, quirks, upgrade cost, salvage value) |
| `GearEconomyConfig` | Gear upgrade cost and salvage value calculations |
| `UIShopScreen` | Inter-level shop (purchase, reroll, heal, wallet reading, pin toggle matching) |
| `UIButtonPrice` | Price buttons (shop heal/reroll — label, price, affordability) |
| `UISliderToggle` | Toggle settings + shop pin toggles |
| `UITooltip` | Tooltips |
| `UISettingsSlider` | Settings sliders (label + value) |
| `UISettingsPageGameplay` | Gameplay toggle callbacks |
| `UISettingsForm` | Settings tabs (PageLeft/PageRight) |
| `StepSelectorBase` | Left/right selector value changes (IncreaseIndex/DecreaseIndex) |
| `UITabGroup` | Tab changes in gear inventory (SetActiveTab) |
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
| `DreadnoughtAnimator` | Boss attack telegraph detection (charge, spikes, fireball, heal) |
| `UIBossTopBar` | Boss HP tracking (show, updateFill thresholds, death, heal) |
| `AudioMastering` | Master volume sync (SetMasterVolume, OnSaveDataLoaded) |
| `UIAbortPopupForm` | Abort popup close detection (HidePopup) for pause reader resume |
| `Bobby` | Drill escort tracking (state, fuel, progress, position — polled, not patched) |
| `UIObjectiveTracker` | Active objective reading (uiObjectives array, O key reader) |

---

## IL2CPP / Harmony Critical Rules

**NEVER patch base Unity/IL2CPP class methods** like `Selectable.OnSelect`, `UIFormTabbedPageAlbum.OnPageChanged`, etc. These fire during early IL2CPP initialization before types are ready, causing **fatal AccessViolationException** crashes that cannot be caught in .NET 6. The game will silently fail to open.

**NEVER use `TryCast<T>()` or `GetComponentInChildren<T>()` inside patches on base class methods** — same crash reason. Only use these in patches on specific game classes (e.g., `UISettingsSlider.SetValueText`).

**Harmony postfix parameter names MUST match the original method exactly.** If the game method has `bool value`, your postfix cannot use `bool toggle`. Safest approach: don't capture the parameter at all — read state from the instance instead (e.g., `Toggle.isOn`).

**Forms that don't override `SetVisibility`** cannot be patched with `[HarmonyPatch(typeof(X), nameof(X.SetVisibility))]`. Harmony's DeclaredMethod only finds methods declared on the type, not inherited. Use their actual declared methods (Setup, Show, SetupOverclock, etc.).

**Overloaded methods** need `TargetMethod()` approach to disambiguate. Use `GetMethods()` and filter by parameter count.

**Some native method detours crash the game** even with empty postfixes. Known: `UISettingsPageVideo.OnToggleVsync` — the native detour itself corrupts something. No workaround except avoiding the patch entirely. The Vsync toggle is handled by `SettingsFocusTracker` instead.

**IL2CPP native-to-native calls bypass Harmony patches.** When method A calls method B internally in native IL2CPP code, patching B's managed wrapper won't fire. Must patch the managed entry point (A) instead. Example: `StepSelectorBase.IncreaseIndex` calls `SetIndex` natively — patching `SetIndex` doesn't work, must patch `IncreaseIndex`/`DecreaseIndex`.

**Slider labels are grandchildren** of UISettingsSlider: `UISettingsSlider → MasterSlider → NameText (TMP)`. Use `GetComponentsInChildren<TMP>()` skipping `valueText`, not sibling searches.

**Target Framerate slider** has `textMultiplier=100`, making `valueText` show "6000" instead of "60". Corrected by name check (`Target_Framerate_SettingsSlider`) — do NOT apply generic textMultiplier correction as other sliders use it legitimately (e.g. volume 0-1 → 0-100).

**Rule of thumb:** Only patch methods that are **declared directly on the target class**, not inherited from base classes. When in doubt, check the decompiled code.

**Localization approach:** For game data (stat names, rarity names, gear slot types, currency names), prefer the game's own localized text via `LocalizedString.GetLocalizedString()`, `StatSettingCollection`, `UiRarityData`, `LocalizedResources` etc. Cache ScriptableObject singletons via `Resources.FindObjectsOfTypeAll<T>()` with a "searched" flag to avoid repeated lookups. For all mod-specific messages (announcements, labels, screen reader text), use `ModLocalization.Get(key)` with keys defined in `localization/en.txt`. Never hardcode English strings in source code — always use ModLocalization.

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
