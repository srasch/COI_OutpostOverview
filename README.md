# Outpost Overview

> One window showing all your world map mines: storage fill level, remaining reserves and status at a glance, with a pause button for each mine.

Source code for the **Outpost Overview** mod for [Captain of Industry](https://www.captain-of-industry.com).

## Features

- Toolbar button (next to the world map, appears once you own a world mine) and hotkey **F8** (rebindable in Settings > Controls)
- One compact line per repaired world mine: product icon, name, storage fill level, remaining reserve, years left, status, pause button
- Sort by storage, reserve, years left or name with a click on the column header, or arrange the mines yourself by drag & drop
- Sorting, your own order and the reserve toggle are remembered per game
- Pause / resume a mine directly from the list
- English and German texts

## Compatibility

Captain of Industry 0.8.8+ (0.8.8b verified). Works on existing saves and can be removed at any time; the mod creates no save data.

## Build from source

Requires the `COI_ROOT` environment variable pointing at your Captain of Industry install. Open `OutpostOverview.sln` in Visual Studio, or run `dotnet build OutpostOverview.sln` from the repo root. The build deploys the mod into `%APPDATA%\Captain of Industry\Mods\OutpostOverview\`. See [CLAUDE.md](CLAUDE.md) for project setup, structure, and release workflow.

## License

This mod's own source code is released under the [MIT License](LICENSE).

This repository includes short excerpts of and references to Captain of Industry game code (most notably in `docs/MODDING-REFERENCE.md`). Any such Game Code is © MaFi Games and is used only under the [Captain of Industry Modding Policy](https://www.captain-of-industry.com/modding-policy). The MIT grant above does **not** extend to that material.
