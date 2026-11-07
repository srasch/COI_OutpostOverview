# Captain of Industry Mod — OutpostOverview

## Project Overview

This is a C# mod for the game **Captain of Industry** (COI), developed using the official Mafi modding framework. The mod adds a window that lists all of the player's world map mines ("outposts") with their storage fill level, optional remaining reserves, status, and a pause/resume button per mine.

The maintainer is not necessarily a programmer by trade. Code should be explained clearly and kept as simple as possible. Conversation language with the maintainer is German; code, comments and docs are English.

## Mod Identity

- **Author:** Nymotikas
- **Mod ID:** `OutpostOverview`
- **Display name:** Outpost Overview
- **GitHub repo:** [`srasch/COI_OutpostOverview`](https://github.com/srasch/COI_OutpostOverview) (remote `origin`: `git@github.com:srasch/COI_OutpostOverview.git`) — source code only, not a player-facing channel; direct players to the Hub.
- **Game:** Captain of Industry (verified with 0.8.8b)
- **Framework:** Mafi (.NET 4.8)
- **Mod type:** `IMod`

## Distribution

- **Exclusive channel:** [COI Mod Hub](https://hub.coigame.com/Mod/1277) — first uploaded as v0.1.0 on 2026-11-07. Players download and install updates manually; the Hub does NOT auto-update.
- **Hub forum:** bug reports go to [/Forum/Mods/OutpostOverview/bugs](https://hub.coigame.com/Forum/Mods/OutpostOverview/bugs), ideas/feedback to [/Forum/Mods/OutpostOverview/ideas](https://hub.coigame.com/Forum/Mods/OutpostOverview/ideas) (URL pattern as for other Hub mods — verify once).
- **License (mod's own code):** MIT (see `LICENSE`).
- **License (game-code excerpts):** Per the [COI Modding Policy](https://www.captain-of-industry.com/modding-policy), short excerpts of game code (type names, method signatures, small samples — e.g. throughout `docs/MODDING-REFERENCE.md`) are © MaFi Games and used only under the modding policy. The MIT grant explicitly excludes them; both `LICENSE` and `docs/MODDING-REFERENCE.md` carry the required attribution notice.
- **Hub license picker:** configured once in the Hub UI at first upload and persists across versions.

## External Resources

- **Hub mod page:** https://hub.coigame.com/Mod/1277
- **Modding policy:** https://www.captain-of-industry.com/modding-policy
- **Official modding repo:** https://github.com/MaFi-Games/Captain-of-industry-modding
- **Sister project (same conventions, source of skills/scripts/docs):** `B:\Projekte\COI_Mods\COI-ResearchQueue`

## Project Structure

```
OutpostOverview.sln          # Visual Studio solution
OutpostOverview.csproj       # Project file (build config, references, auto-deploy, version from manifest)
OutpostOverviewMod.cs        # Mod entry point (IMod); registers strings + F8 hotkey
OutpostOverviewWindow.cs     # The window (OutpostOverviewWindowView) + toolbar controller, auto-registered via DI
ModStrings.cs                # All mod-owned player-visible texts (registered via Loc.Str at mod load)
ModShortcuts.cs              # Key-binding actions (KbHandle) of the mod
OutpostOverviewSettings.cs   # Per-game window settings (sorting, manual order, reserve toggle) in SaveData\<GameId>.txt
Translations/                # <language>.json translation files for ModStrings (deployed + bundled in release zip)
manifest.json                # Mod metadata (see Manifest Fields below)
changelog.txt                # Cumulative player-facing changelog; bundled in every release zip
docs/MODDING-REFERENCE.md    # Game API notes and UI patterns (shared knowledge base, extended for this mod)
scripts/package-release.ps1  # Builds + packages the Hub zip into bin\pkg\
scripts/inspect_dll.ps1      # Inspect game types (PowerShell 7+)
.claude/skills/              # /ship-it, /game-version-check, /skill-retro
```

## Build & Deploy

### Environment Variables Required
- `COI_ROOT` — path to the Captain of Industry install directory (here: `D:\Spiele\SteamLibrary\steamapps\common\Captain of Industry`)

### Build
From the project root:
```
dotnet build OutpostOverview.sln
```
Always specify `OutpostOverview.sln` explicitly. Do not pass `/p:LangVersion=latest` on the command line.

On build, the mod is automatically deployed to `%APPDATA%\Captain of Industry\Mods\OutpostOverview\` (`OutpostOverview.dll`, `manifest.json`, `Translations\*.json`, plus `.pdb` in Debug).

## How the mod works (architecture)

- **Data source:** `WorldMapManager` (public, in DI) → `Mines` (all mines of the world map). The window shows only `IsRepaired` mines.
- **Per mine:** `Buffer.Quantity/Capacity/Product` (storage fill), `QuantityAvailable` (remaining reserve, `null` = unlimited), `Prototype.QuantityAvailable` (reference value for the reserve bar, same as the native inspector), `CurrentState`, `IsPaused`, `ProductionStep`.
- **Pause/resume:** `IInputScheduler.ScheduleInputCmd(new ToggleEnabledCmd(mine.Id))` — the same command the native inspector's pause button uses. Never call `mine.SetPaused()` directly from UI code (simulation state must change via commands).
- **Live updates:** the game's `Observe(...).Do(...)` / `ObserveEnumerable` / `ObserveVisible` bindings (`Mafi.Core.Syncers`), evaluated in the sync phase — no own polling, thread-safe.
- **UI:** the view is a game `Window` (movable by the title bar with locally saved position via `MakeMovableAndEnablePositionSaving()`, pin button, hotkey shown in title); the controller extends `WindowController<OutpostOverviewWindowView>`, which creates the window lazily on first open and handles open/close/Esc. Width shrinks when the reserve columns are hidden (`WindowWidth`). Toolbar button via `ToolbarHud.AddMainMenuButton` (icon `Toolbar/WorldMap.svg`, order 525 next to the world map button); `ProductBufferUi` (native bar with product icon) for storage and reserve.
- **Hotkey:** `ModShortcuts.ToggleWindow` (`KbHandle`, default F8), registered in the `OutpostOverviewWindowController` constructor via injected `IShortcutsRegistry` (not in `IMod.Initialize` — the UI-side registry doesn't exist yet there) — rebindable in Settings > Controls.
- **Toolbar gotcha:** `IToolbarItemController.IsVisible` = "button is shown" (not "window open"); the window's open state is handled in `Activate()`/`Deactivate()`.
- **Layout:** one row per mine — drag handle | product icon | name | storage bar (`ProgressBarPercentInline`, green/yellow ≥90%/red 100%) | reserve "45k (38%)" | years left | status text | pause button. Header and rows share fixed column widths (constants at the top of `OutpostOverviewWindowView`).
- **Sorting:** clickable headers Mine / Storage / Reserve / Years (1st click natural direction — storage descending, others ascending —, 2nd reverses, 3rd back to manual). Drag & drop via the game's `Reorderable` always switches to manual order. The sorted list is an `ObserveEnumerable` (order-sensitive `CompareFixedOrder`); existing row components are re-used and re-arranged with `SetChildren`. Re-arranging is suspended while a drag is in progress.
- **Toolbar button visibility:** `IsVisible` = "player owns at least one repaired mine", checked every `IGameLoopEvents.SyncUpdate`; `DeactivateShortcutsIfNotVisible = false` keeps F8 working while the button is hidden (`UnityInputManager` checks `IsVisible || !DeactivateShortcutsIfNotVisible`).
- **Column toggles:** "Show remaining reserves" (Reserve + Years) and "Show status". With status hidden, the mine name is colored by status; the status text is always the name's tooltip. The window width is reduced by the hidden columns.
- **Settings per game:** `OutpostOverviewSettings` stores sort column/direction, both column toggles and manual order (entity ids) in `<mod folder>\SaveData\<GameId>.txt` (`IGameIdProvider.GameId`, known only after game init → loaded lazily from the first sync update). Not part of the release zip.
- **No reflection, nothing written into the save game.** Safe to add to / remove from existing saves.

## Manifest Fields

Character limits are enforced by the Hub and **cannot be changed after a version is uploaded**.

**Required:** `id` (pattern `[a-zA-Z0-9][a-zA-Z0-9_-]*`, must not start with `COI-`), `version` (`major.minor[.patch[letter]]`), `primary_dlls`.

**Optional (Hub-visible):** `display_name` (max 50 chars), `description_short` (max 180 chars), `description_long` (`\n` for newlines), `links`, `authors`, `min_game_version`, `max_verified_game_version`.

**Optional (behavior flags):** `non_locking_dll_load`, `can_add_to_saved_game`, `can_remove_from_saved_game`, `mod_dependencies`, `optional_mod_dependencies`, `incompatible_mods`, `primary_mod_class_name`.

## Hub Packaging Requirements

- **ZIP structure:** the zip root is the mod folder (`OutpostOverview/manifest.json`), so players can extract directly into `Mods/`. `scripts/package-release.ps1` handles this.
- **Files in zip:** `OutpostOverview.dll`, `manifest.json`, `changelog.txt`, `Translations/*.json`. Nothing else.
- **`changelog.txt` format** — plain text, cumulative, newest first:
  ```
  v0.1.1 | 2026-11-08
  * Bullet one

  v0.1.0 | 2026-11-07
  * Initial release
  ```
  No markdown. The Hub parses it automatically.
- **Versions cannot be deleted or edited** once uploaded. Verify the zip before uploading.
- **Output zip:** `bin\pkg\OutpostOverview-v<version>.zip`

## Release Workflow

`/ship-it` handles everything end-to-end: version bump, What's New drafting, `changelog.txt` update, packaging, and the Hub upload reminder. See `.claude/skills/ship-it/SKILL.md`.

## Health Checks & Game Version Compatibility

`/game-version-check` after any game update. Because the mod uses only public APIs, a successful `dotnet build` against the new game DLLs is the main compatibility check; the skill adds an in-game checklist and log analysis.

## Working Style Notes

- Explain what code does in plain language when making changes.
- Before writing code for a new feature, ask clarifying questions until the intent is clear.
- **Version bumping:** if a session involves code changes, proactively ask whether a version bump should be included before the session ends; if yes, use `/ship-it`.
- **GitHub Issues:** before starting bug fix or feature work, check `gh issue list` for a related open issue and remind the user, so the commit can reference it.
- **Commit messages:** single line describing what changed, no body. With GitHub issues: append `Fixes #N` (bugs) or `Closes #N` (enhancements).
- **Prefer public APIs and game commands** over reflection. If reflection ever becomes necessary, add a probe/health-check mechanism like the one in COI-ResearchQueue (`ReflectionProbe`) and extend `/game-version-check` accordingly.
- **Reuse the game's translated strings** (`Mafi.Core.Tr`, e.g. `Tr.ReserveStatus`, `Tr.EntityStatus__*`) where possible; only add own texts to `ModStrings` and translate them in `Translations/`.

## Versioning

**Semantic Versioning** (`MAJOR.MINOR.PATCH`):

- **Patch (0.0.X)** — bug fixes and small tweaks. When in doubt, bump this one.
- **Minor (0.X.0)** — new player-visible features. Resets patch.
- **Major (X.0.0)** — major milestones (1.0.0 = "complete") or game-update-forced rewrites.

Do NOT bump for docs-only, build-script or comment-only changes. `manifest.json` is the source of truth.

## Documentation Rules (IMPORTANT)

Whenever we discover something new about how the game works — APIs, types, signatures, modding patterns — **update the docs without being asked**:

- **docs/MODDING-REFERENCE.md** — game API discoveries, working code patterns, gotchas, corrections.
- **CLAUDE.md** — project-level changes (structure, scope, architecture).
