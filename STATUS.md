# STATUS — Deep Rock Galactic Survivor

> Per-mod status ledger / dashboard. Open this first when resuming the mod so progress isn't re-derived from the code each session. Keep it short — a dashboard, not docs. Update the **Next step** line and the section table whenever you finish a chunk. Derive every value from the game's real data — no guessed offsets.

**Last updated:** 2026-07-10

## Identity
- **Engine / framework:** Unity IL2CPP, BepInEx 6 (BepInEx.Unity.IL2CPP, be.785 post Unity-6 migration) + Il2CppInterop + Harmony, net6.0; NAudio.
- **Screen-reader transport:** Tolk via TolkDotNet (`ScreenReader.cs`); native `Tolk.dll` / `nvdaControllerClient64.dll` copied manually.
- **Build command:** `dotnet build` (csproj copies the DLL to the plugins folder).
- **Mod install path:** `D:\games\...\Deep Rock Survivor\BepInEx\plugins\drgAccess` (+ `lang/` localization + sounds). Ship the FULL package (repaired interop assemblies) for Unity 6.
- **Run / test:** Launch through Steam with a screen reader; menus announce on load, gameplay cues play in-mission.

## Section status
`done` = works with the screen reader on; `wip` = started; `todo` = not begun.

| Section / feature | Status | Notes |
|---|---|---|
| Full menu/UI screen reader | done | `Patches/UI*` (Button, Settings, Slider/Toggle, Form, Objective, PageDescription, Tooltip, ActionFeedback, CorePause) |
| Class/subclass/biome/gear/shop flow | done | `UIButtonPatch.Mission/Gear/Pause.cs`, `SettingsFocusTracker.cs` |
| Localization (14 languages) | done | `Helpers/ModLocalization.cs`, `LocalizationHelper.cs`, `lang/*.txt` |
| Wall detection (4-dir sonification) | done | continuous tones, distinct freq per direction |
| Enemy detection | done | `Components/EnemyTracker.cs`, `Patches/EnemyPatches.cs`; panned beeps, elite/boss/loot variants |
| Drop pod / supply pod beacons | done | `Patches/DropPodPatches.cs`; grid pathfinding rebuilt for Unity 6 (CHANGELOG v0.10.0) |
| Hazard warning + healing zone | done | `Patches/HealingZonePatch.cs`; siren for exploders/spikes |
| Milestones / wallet / eliminations | done | `Components/MilestoneReaderComponent.cs`, `WalletReaderComponent.cs`, `Patches/EliminationPatches.cs` |
| Dreadnought dodge assistance | done | charge/spikes/fireball danger + escape direction (CHANGELOG v0.10.0) |
| Jump-zone beacon | done | Azure Weald bounce holes "boing boing" beacon (CHANGELOG v0.10.0) |
| Smart Beacon (target scoring) | wip | EXPERIMENTAL, off by default; tuning with feedback (CHANGELOG v0.10.0) |
| Audio Cue Preview menu | done | Backspace from any menu |
| Play menu: Endless + locked state | wip | needs user test: locked buttons were silent (label lives in the hidden group) |
| Bonus objectives in pause | wip | needs user test: read from `LevelObjectiveTracker`, the pause form has no field for them |
| Objective resources + lootbugs | wip | needs user test: new collectible cues (`ObjectiveRes`, `Lootbug`) |
| Environment ping (P / R3) | wip | needs user test: on-demand sweep, answers the "audio spam" complaint |
| Hollow Bough spiky roots | wip | needs user test: ONE area cue per vine field, critical inside `RedVineSystem.damageDist` |
| Rock Dozer directions | wip | needs user test: cues measured from the vehicle's nose while driving (toggle, on by default) |
| Player rank on demand | wip | needs user test: G / RB outside gameplay |

## Derived facts (so we never re-RE them)
| Fact | Value | Source |
|---|---|---|
| Decompiled game code | `drg code/` (IL2CPP dump) | repo tree |
| Unity 6 interop breakage | BepInEx generator produces corrupted interop assemblies → repaired by `tools/InteropFixer/Program.cs`; hash in `BepInEx/interop/assembly-hash.txt` | CHANGELOG v0.10.0 |
| Beacon pathfinding | mod's own A* over the game's real block grid (NavMesh crashed at extraction in Unity 6); guidance ends at pod ENTRANCE | CHANGELOG v0.10.0 |
| Boss danger geometry | corridor width / spike radius read from the boss's real size & spike damage radius | CHANGELOG v0.10.0 |
| Dreadnought classes | `DreadnoughtAnimator`, `Ommoran*` (Ability/Phase/Crystal/Settings) | `drg code/*` |

## Next step
Player-test the July 2026 feedback batch (rows marked `wip` above), then consolidate it all into ONE release.
Two things could not be verified from code alone: whether the vehicle facing (`GetFaceDirection`, mapped to world
through the camera axes) really matches the Rock Dozer's nose, and whether one cue per vine field is enough in
Hollow Bough or the roots need per-cluster cues.

## Known issues / open questions
- Unity-6 / BepInEx be.785 fragility: every game update can re-break interop — re-run InteropFixer and re-ship the FULL package.

**Detailed history:** see CHANGELOG.txt.
