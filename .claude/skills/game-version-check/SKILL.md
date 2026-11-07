---
name: game-version-check
description: Run after a Captain of Industry game update. Compares game version, rebuilds the mod against the new game DLLs, guides manual in-game testing, analyzes logs, and prompts for version bump + release if needed.
disable-model-invocation: true
---

Walk through each step in order. Stop and report clearly if anything fails.

## What this does and why

The OutpostOverview mod uses only **public** game APIs (no reflection): `WorldMapManager.Mines`, the `WorldMapMine` properties, `ToggleEnabledCmd`, `ToolbarHud`, `KbHandleBuilder` / `IShortcutsRegistry` and UI components like `ProductBufferUi`, `Toggle` and `ButtonIcon`. That means a **game update breaks the mod at compile time**: if the build against the new game DLLs succeeds, all API references still exist. What the compiler cannot catch (changed behavior, layout, broken icons) is covered by the in-game checklist and the log analysis.

## Key files

| File | What it does |
|------|-------------|
| `changelog.txt` (game's, not the mod's) | The game's own changelog in the game install directory (`$COI_ROOT`). The first line is always the current version (e.g. `v0.8.8b | 2026-11-05`), including hotfix letter suffixes. Note: the mod repo also has a `changelog.txt` -- that is the player-facing mod changelog, not this file. |
| `scripts\inspect_dll.ps1` | When the build fails: shows what a game type looks like now so you can spot what changed (renamed, moved, etc.). |
| `OutpostOverviewWindow.cs` | The window and toolbar controller -- nearly all game API usage lives here. |
| `ModShortcuts.cs` | Hotkey declaration (registered in the controller constructor in `OutpostOverviewWindow.cs`). |
| `docs/MODDING-REFERENCE.md` | Game API notes; the "World Map Mines" and "Key bindings (0.8.8+)" sections are specific to this mod. |

---

## Step 0 -- Determine game version and compare

> ⚠️ Use the **PowerShell tool** (not Bash) for the commands in this skill -- the Bash tool strips `$` variable references.
>
> ⚠️ `scripts\inspect_dll.ps1` requires **PowerShell 7+** -- always run it with `pwsh`. For decompiling use `ilspycmd` (installed via `dotnet tool install -g ilspycmd`).

1. Read the first line of the game's `changelog.txt`:
   ```
   powershell.exe -ExecutionPolicy Bypass -Command "& { $r = [System.Environment]::GetEnvironmentVariable('COI_ROOT','User'); if (-not $r) { $r = [System.Environment]::GetEnvironmentVariable('COI_ROOT','Machine') }; Get-Content (Join-Path $r 'changelog.txt') | Select-Object -First 1 }"
   ```
   Strip the leading `v` and everything from ` | ` onward (e.g. `0.8.8b`).

2. If `COI_ROOT` is not set or the file is missing, ask the user which game version they are running (shown on the game's main menu).

3. Read `max_verified_game_version` from `manifest.json`.

4. Show: **Current game version**, **Max verified version in manifest**, **Match?** (`0.8.8b` vs `0.8.8` is a **No** -- hotfix suffixes count).

5. Remember the result for Step 5.

---

## Step 1 -- Rebuild against the new game DLLs

```
dotnet build OutpostOverview.sln
```

- **Build succeeds** -- every game API the mod uses still exists. Tell the user and continue to Step 2. (The build also deploys the mod to `%APPDATA%\Captain of Industry\Mods\OutpostOverview\`, ready for testing.)
- **Build fails** -- explain each compiler error in plain language. For each missing type/member:
  1. Inspect the type: `pwsh -File scripts\inspect_dll.ps1 <TypeName> <DllName>` or decompile it with `ilspycmd "<COI_ROOT>\Captain of Industry_Data\Managed\<Dll>" -t <Full.Type.Name>`.
  2. Explain what happened: **renamed**, **moved** (other namespace/class), **signature changed**, or **removed**. Example from 0.8.8: `ToolbarHud.AddMainMenuButton` switched from a `Func<ShortcutsManager, KeyBindings>` to an `Option<KbHandle>` hotkey parameter.
  3. Fix, rebuild, repeat until the build is clean.
  4. Document the change in `docs/MODDING-REFERENCE.md`.

---

## Step 2 -- Manual in-game testing

Present this checklist and wait for the user's feedback:

> Launch the game, load a save **that has at least one repaired world map mine**, and work through this list:
>
> 1. **Toolbar button** -- The "World mines" button appears next to the world map button. Clicking it opens the window, clicking again closes it.
> 2. **Hotkey** -- F8 toggles the window. In Settings > Controls the action appears under "Mod: Outpost Overview" and can be rebound.
> 3. **One line per mine** -- Each repaired mine is a single row: drag handle, product icon, name, storage bar with %, reserve (e.g. "45k (38%)"), years, status text, pause button. Columns line up with the header.
> 4. **Live values** -- With the game unpaused, the storage bar of a working mine fills up without reopening the window; it turns yellow at 90 % and red at 100 %. Hovering the bar shows "quantity / capacity".
> 5. **Status colors** -- Working is green, paused is yellow, full storage / depleted are red.
> 6. **Pause button** -- Clicking it pauses the mine (button highlighted, status "Paused"); clicking again resumes it. The mine's inspector on the world map shows the same state.
> 7. **Sorting** -- Clicking "Storage" sorts fullest first (▼, green header), again reverses (▲), a third time returns to your own order. Same for "Reserve", "Years" (least first) and "Mine" (A-Z).
> 8. **Manual order** -- Drag a row by its handle: the order changes and stays (also after switching sorting back to manual, and after reloading the save / restarting the game).
> 9. **Reserves toggle** -- The checkbox sits left of its label ("[x] Show remaining reserves"). It hides/shows the Reserve and Years columns and the window gets narrower/wider accordingly; the setting is remembered per game.
> 9a. **Status toggle** -- "Show status" hides the status column (window gets narrower); the mine name then shows the status color (green working, yellow paused, red full/depleted). Hovering the name shows the status text.
> 9b. **Window** -- The window can be dragged by its title bar; after closing/reopening (and restarting the game) it reappears at the same position. The X button and Esc close it; the pin button keeps it open. Mines with infinite deposits show "unlimited" and "–".
> 10. **Button visibility** -- On a save without repaired mines the toolbar button is hidden, but F8 opens the window with the "No world mines yet" hint. Repairing the first mine makes the button appear and the mine shows up in the list.
> 11. **Translation** -- With the game language set to German, the mod's own texts are German.
>
> Say "all clear" or list the items that had issues.

---

## Step 3 -- Analyze game log

Immediately after the user's feedback (don't ask again):

1. Find the newest log:
   ```
   powershell.exe -ExecutionPolicy Bypass -Command "& { $p = Join-Path ([System.Environment]::GetFolderPath('ApplicationData')) 'Captain of Industry\Logs'; Get-ChildItem $p -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName }"
   ```
2. Extract all lines containing `OutpostOverview` (including `Failed to register action 'OutpostOverview.` and translation warnings like `ships translations of N strings that it does not own`).
3. Also look for exceptions whose stack trace mentions `OutpostOverview`.
4. Summarize: **Mod loaded:** yes/no (look for `OutpostOverview: constructed`, `Initialize called` and `toolbar button and hotkey registered` -- the last one is missing if the window controller was never created), **Warnings**, **Errors**, and whether the log matches the user's test results.

---

## Step 4 -- Resolution loop

For any failure or discrepancy: investigate with the user, fix, rebuild (`dotnet build OutpostOverview.sln`), have the user re-test only the failed items, pull the log again. Repeat until clean.

If everything passed: "All clear -- the mod is fully compatible with this game version."

---

## Step 5 -- Update verified version (conditional)

**Skip this step if the game version from Step 0 already matches `max_verified_game_version`.** Say something lighthearted and stop.

### Case A: Nothing broke -- no code changes needed

Don't ship a new release. The COI Hub has a **"Re-verify game version range"** widget on each version's page that extends the verified range without a new upload (it accepts values above the manifest max, including hotfix letters).

1. Tell the user that the cleanest path is the Hub's re-verify widget, plus updating the repo files so the manifest stays honest.
2. Update `manifest.json` `max_verified_game_version` (full version incl. hotfix letter) and the compatibility note in `README.md`.
3. Ask before editing. If yes, edit and commit:
   ```
   git add manifest.json README.md
   git commit -m "Update max verified game version to X.Y.Z"
   ```
4. Tell the user to open the mod's Hub page (URL in CLAUDE.md), open the latest version, and use **Re-verify game version range** with `X.Y.Z`.

### Case B: Something broke and was fixed, or a feature was added

Tell the user a new release is warranted and to run `/ship-it`. Remind them to bump `max_verified_game_version` to the new game version as part of it.

---

## Notes

- The mod has no save data of its own and no reflection, so there is no runtime "health check" -- the compiler is the health check.
- Anyone else debugging the mod needs `COI_ROOT` set to their game install path for builds and scripts.
