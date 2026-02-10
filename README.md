# DRG Survivor Accessibility Mod

Accessibility mod for **Deep Rock Galactic Survivor** that enables blind and visually impaired players to play the game using screen reader support and spatial audio cues.

## Download

**[Download latest release](https://github.com/Ali-Bueno/drg-access/releases/latest)**

## Installation

1. Download and extract the zip from the latest release.
2. Copy all files and folders into your game directory.
   - You can find it in Steam: right-click the game > Manage > Browse Local Files.
3. Launch the game normally through Steam.

Everything is included in the release (BepInEx, audio libraries, screen reader support). No additional setup needed.

## Requirements

- Deep Rock Galactic Survivor (Steam, Windows)
- A screen reader: NVDA (recommended) or any SAPI-compatible screen reader

## Features

### Screen Reader Support

All menus, buttons, forms, and settings are fully accessible. Every screen is announced when it opens, and all interactive elements read their content when focused. This covers the entire game flow: class and subclass selection, biome and hazard selection, gear and stat management, the shop, level-up choices, milestones, pause menu, end screen, and more.

Action feedback is provided for purchases, upgrades, equip/unequip, and market transactions.

### Gameplay Audio Cues

Spatial audio cues provide awareness of the game environment during missions:

- **Wall Detection** — Continuous tones in 4 directions that get louder as walls get closer. Each direction has a distinct frequency.
- **Enemy Detection** — Positional beeps with stereo panning. Distinct sounds for normal enemies, elites, bosses, and rare loot enemies. Beeps accelerate at close range. New enemy types are announced by name.
- **Drop Pod Beacon** — Chirp beeps guiding you to the extraction pod. A pulsing tone confirms when you're inside.
- **Supply Pod Beacon** — Lower-frequency chirp beeps guiding you to supply zones.
- **Hazard Warning** — Siren alarm for nearby dangers (Exploders, Ground Spikes).

### Special Keys

| Key | Context | Action |
|-----|---------|--------|
| F | During extraction | Compass — direction and distance to the drop pod |
| G | Stat upgrades / Shop | Read all currency balances |
| Backspace | Menus (not in gameplay) | Open the Audio Cue Preview menu |

### Audio Cue Preview

Press Backspace from any menu to listen to all audio cues the mod uses. Navigate with Up/Down arrows and press Enter to play a preview.

## Uninstallation

Verify or reinstall the game through Steam to restore original files.

## Built With

- [BepInEx 6](https://github.com/BepInEx/BepInEx) (IL2CPP)
- [Harmony](https://github.com/pardeike/Harmony)
- [NAudio](https://github.com/naudio/NAudio)
- [Tolk](https://github.com/ndarilek/tolk)
