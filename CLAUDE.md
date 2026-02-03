# DRG Survivor Accessibility Mod

## Project Overview
Accessibility mod for **Deep Rock Galactic Survivor** using:
- **BepInEx 6** (IL2CPP)
- **Harmony** for patching
- **Tolk / TolkDotNet** for screen reader support

---

## Current Status

### Implemented Features
- **Menu Navigation**: Buttons announce their text when selected
- **Class Selection**: Reads class name, description, and base stats (HP, Evasion, Crit Chance, Crit Damage)
- **Subclass Selection**: Reads subclass name, stat bonuses, starter weapon info (name, description, stats, targeting, damage type)
- **Biome Selection**: Reads biome name and locked status
- **Hazard Level Selection**: Reads hazard level and locked status
- **Mutators**: Reads mutator name and description
- **Shop Items**: Reads item name, stats, and description
- **Toggle Settings**: Announces On/Off state when clicked
- **Tooltips**: Basic tooltip reading
- **Form Announcements**: Announces when forms/menus open

### Pending Improvements
- [ ] In-game HUD reading (health, XP, wave, etc.)
- [ ] Level-up skill selection improvements
- [ ] Combat feedback (damage taken, enemies nearby)
- [ ] Audio cues for spatial awareness
- [ ] Death/victory announcements
- [ ] Settings menu full support

---

## Project Structure

```
drgAccess/
├── Plugin.cs              # Main plugin entry point
├── ScreenReader.cs        # Tolk wrapper for screen reader output
├── Patches/
│   ├── UIButtonPatch.cs   # All button types (class, subclass, shop, etc.)
│   ├── UIFormPatches.cs   # Form/menu announcements
│   ├── UITooltipPatch.cs  # Tooltip reading
│   └── UISliderTogglePatch.cs  # Toggle state announcements
└── drgAccess.csproj       # Project file

drg code/                  # Decompiled game code for reference (not included in repo)
references/tolk/           # Tolk DLL references
```

---

## Dependencies

- **BepInEx 6.x** (IL2CPP version)
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
| `UIMutatorView` | Hazard modifiers |
| `UISliderToggle` | Toggle settings |
| `UITooltip` | Tooltips |
| `UIForm` | Menu/form containers |

---

## Code Conventions

- All code in **English**
- All comments in **English**
- Commits in **English**
- Use IL2CPP reflection methods (`TryCast<T>()`)
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
