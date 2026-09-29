# Build Guide: PC Game Cover Art Screensaver

This guide takes you from an empty PC to a published open-source project. It also explains what each piece of
code does, so you can change it with confidence. Work through the stages in order. Each one ends with a check that
tells you it worked.

---

## Stage 0: How a Windows screensaver works

A screensaver is an ordinary `.exe` renamed to `.scr`. Windows starts it with one of these command-line arguments:

| Windows runs… | When | Our code does |
|---|---|---|
| `PCGameCoverArt.scr /s` | Screensaver kicks in | Full-screen coverflow on every monitor, exit on input |
| `… /p 12345` | Screen Saver Settings shows the little monitor preview | Draw inside window handle `12345` |
| `… /c:12345` or no args | You click **Settings…** or double-click the file | Show the settings dialog |
| `… /w` | *(ours, for development)* | Run in a normal resizable window |

All of this parsing is in `src/CoverArtSaver.Core/ScreensaverArgs.cs`, and `App.xaml.cs` switches on the result.

The other half of the problem is getting data out of Playnite. Playnite keeps its database files locked while
it runs, so we wrote a tiny **Playnite add-on** that writes a JSON snapshot of the library to
`%LOCALAPPDATA%\PCGameCoverArt\library.json` whenever the library changes. The screensaver only reads that file.
The two programs never talk to each other directly.

---

## Stage 1: Install the tools

Open **Terminal (PowerShell)** and run:

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Git.Git
winget install GitHub.cli
```

For an editor, pick one:

- **Visual Studio 2026 Community** (free) with the **.NET desktop development** workload. It has a WPF designer and
  a debugger.
- **VS Code** with the **C# Dev Kit** extension. It's lighter, but has no XAML designer.

Close and reopen Terminal, then check:

```powershell
dotnet --list-sdks   # should list a 10.0.x SDK
git --version
gh --version
```

> **Why .NET 10?** It's the current long-term-support release. .NET 8 support ends in November 2026, so a new
> project should start on 10.

You also need **Playnite 10** installed, with some games that have cover art.

---

## Stage 2: Get the code on your PC and build it

1. Unzip the project to a short path such as `C:\src\pc-game-cover-art-screensaver`.
2. In Terminal:

```powershell
cd C:\src\pc-game-cover-art-screensaver
dotnet test tests/CoverArtSaver.Tests
```

✅ **Check:** the test run ends with `Passed!` and 0 failed.

Then build everything:

```powershell
dotnet build PCGameCoverArt.slnx
```

If the build reports errors, fix them before you continue. Build errors name the file and line.

---

## Stage 3: Install the Playnite add-on (development mode)

While you develop, have Playnite load the add-on straight from your build folder so you don't have to package it
every time:

1. Playnite → **Settings (F4) → For developers** (called *Developer* in some versions).
2. Under **External extensions**, add this folder:
   `C:\src\pc-game-cover-art-screensaver\src\CoverArtExporter\bin\Debug\net462`
3. Restart Playnite.

✅ **Check:**
- The **Extensions** menu (☰ → Extensions) has a **PC Game Cover Art Screensaver** submenu.
- Click **Export library for screensaver now**. It reports how many games it exported.
- `%LOCALAPPDATA%\PCGameCoverArt\library.json` exists. Paste that path into Explorer's address bar to find it.

If anything fails, check Playnite's log at `%APPDATA%\Playnite\extensions.log`, or `extensions.log` in the
Playnite folder if you use the portable version.

---

## Stage 4: Run the screensaver

```powershell
dotnet run --project src/CoverArtSaver -- /w    # windowed test mode (Esc or close to quit)
dotnet run --project src/CoverArtSaver -- /c    # settings dialog
dotnet run --project src/CoverArtSaver -- /s    # real full-screen mode (move the mouse to quit)
```

✅ **Check:** covers slide past from right to left and the title shows at the bottom. In the settings dialog, the
*Library & filters* tab says "✔ N games, exported … ago" and shows how many games each filter hides.

The screensaver has no console, so errors go to `%LOCALAPPDATA%\PCGameCoverArt\screensaver.log`.

---

## Stage 5: A tour of the code

Read the files in this order. Each one is short and commented.

### The add-on: `src/CoverArtExporter`

| File | What to look at |
|---|---|
| `extension.yaml` | Playnite's manifest: id, name, DLL name, type `GenericPlugin` |
| `CoverArtExporterPlugin.cs` | Subscribes to library events and exports after a 5-second quiet period (a "debounce", because a metadata download fires thousands of events). It writes to a `.tmp` file and then swaps it in, so the screensaver never reads a half-written file. |
| `ExportModels.cs` | The JSON "contract" between the two programs |

The add-on targets **.NET Framework 4.6.2** because Playnite 10 does. The screensaver uses modern .NET 10. They
share nothing but the JSON file, which is why this split works.

### Logic without UI: `src/CoverArtSaver.Core`

| File | What to look at |
|---|---|
| `ScreensaverArgs.cs` | The `/s` `/p` `/c` parsing from Stage 0 |
| `LibraryData.cs` | Reads `library.json` |
| `GameFilter.cs` | **The content filter.** It compiles your keyword list into one regex with whole-word boundaries, so `Sexual` matches "Strong Sexual Content" but not "Asexual". Rules are checked in priority order: Hide tag, then hidden, installed, favorites, and missing cover, then the adult and mature filters. The Show tag skips only the adult and mature filters. |
| `CoverflowMath.cs` | **The coverflow effect in about 40 lines.** `PositionAt(time)` gives the current (fractional) centre index. `PoseFor(offset)` places each cover: the centre one faces you, covers in the first step turn gradually, and the rest sit at a fixed angle with tight spacing. |
| `LibraryLoader.cs` | `LibrarySources` reads the game list from the chosen source (Playnite's export or Steam) into the same `LibraryData` shape, so filtering, ordering and both styles don't care where games came from. `LibraryLoader` then applies the filters. |
| `Steam/SteamLibrary.cs` | **The Steam source.** Builds the game list from Steam's own files: cached artwork (`appcache\librarycache`), game details (`appinfo.vdf`), installed games (`appmanifest_*.acf`), hidden/favorites/collections (`userdata\<id>\config\cloudstorage`) and last played and play time (`localconfig.vdf`). The user review rating (`review_score`, 1–9) comes from `appinfo.vdf` too. Collections become tags, so `Screensaver: Hide` works as a Steam collection. |
| `Steam/SteamSoundtracks.cs` | **Music.** Steam installs each soundtrack into its own folder under `steamapps\music` in a library folder. Many come in several formats at once, so it plays one format per album (MP3 first, then FLAC, M4A, WMA, WAV) and sorts tracks with numbers compared as numbers. `SoundtrackPlaylist` handles shuffle (reshuffles each round, never the same track twice in a row) and "start at a random track". Always Steam: Playnite doesn't know about soundtracks. |
| `Steam/SteamAppInfo.cs` | Reads Steam's binary `appinfo.vdf` (format versions 27–29), decoding only the apps it's asked for |
| `Steam/Vdf.cs` | Parser for Valve's text KeyValues format (`.vdf`, `.acf`) |
| `Steam/SteamTags.cs` | Steam's store tag names, generated from Steam's public tag list. Steam stores tags as numbers; this turns them back into words the content filter can match. |
| `CoverSizes.cs` | For the cover shape option (`CoverShape.cs` decides what counts as vertical, square or horizontal). `ImageHeader` reads an image's width and height from its first bytes (JPEG, PNG, GIF, BMP, WebP) without decoding it. `CoverSizeCache` remembers the results in `cover-sizes.json`, so only new or changed covers are read again. |
| `MonitorTurn.cs` | "Monitors take turns". Every monitor works out, from the same seed, which steps of the shared schedule are its own, so only one monitor changes at a time without the windows talking to each other. Each round is shuffled, and the same monitor never goes twice in a row. |
| `MosaicLayout.cs` | **The mosaic style.** `MosaicLayout.Fit` turns "N covers across" into a grid whose row count follows the screen's shape. `MosaicPlanner` picks which tile flips next (never one that's still turning) and which game it flips to (the next one in the list that isn't already on screen). It's seeded, so mirrored monitors flip identically. The wall is a list of "pieces", each 1×1 or N×N cells. A large piece that's picked moves: the planner returns a `MosaicStep` that removes it and the small tiles under its new spot, and adds normal tiles to the space it left. |
| `FeaturedGames.cs` | Which games get large mosaic tiles: little or no play time, and a minimum rating. Steam's rating tiers become percentage thresholds for Playnite's Community (or Critic) Score. |
| `SaverSettings.cs` | Every option and its default, saved as JSON |

Everything here is plain .NET, so it's unit-tested in `tests/`. Try changing `SideSpacing` in `CoverflowMath.cs`
and rerun `/w` to see the effect.

### Rendering and windows: `src/CoverArtSaver`

| File | What to look at |
|---|---|
| `App.xaml.cs` | Entry point: chooses the mode |
| `Rendering/CoverflowView.cs` | A WPF `Viewport3D` with a camera. Every frame (`CompositionTarget.Rendering`), it creates or removes cover "slots" near the centre and moves them with `PoseFor`. Covers are re-sorted back-to-front so the fading edge covers blend correctly. |
| `Rendering/CoverSlot.cs` | One cover is two textured rectangles: the front, and the reflection hanging below it |
| `Rendering/MosaicView.cs` | The mosaic: a `Canvas` of tiles. A timer counts flips from the shared clock and asks the planner for flips a few steps ahead, so their covers are already loaded when the tile turns. Covers are decoded at tile size, not full size, with a second texture cache for large tiles. Steps where a large tile moves turn several tiles away and new ones in at the same moment. |
| `Rendering/MosaicTile.cs` | One tile. A flip squeezes it to a sliver while darkening it, swaps the cover, and opens it back up, so it reads as a card turning over. `TurnAway` and `TurnIn` do just the first or the second half, for tiles that leave or arrive when a large tile moves. |
| `Rendering/CoverTextureCache.cs` | Loads images on a background thread at a reduced size, which saves a lot of memory. It pre-computes each reflection by flipping and darkening pixels once, which is much cheaper than doing it live in 3D. It keeps only nearby covers in memory. |
| `Audio/SoundtrackPlayer.cs` | Plays the soundtracks with WPF's `MediaPlayer`: fades in over 3 seconds, moves to the next track when one ends, and skips files Windows can't play. |
| `Windows/ScreensaverSession.cs` | One window per monitor, plus mirror, independent, or primary-only mode. It also starts the music (once, not per monitor) and stops it on exit. The small preview doesn't go through here, so it's silent. |
| `Windows/ScreensaverWindow.cs` | Borderless topmost window. It exits on a key, a click, or a real mouse move (small jitter is ignored). |
| `Windows/PreviewHost.cs` | Draws inside Windows' tiny preview monitor as a Win32 child window, and exits when that window goes away |
| `Windows/SettingsWindow.xaml(.cs)` | The dialog. It edits a copy of the settings, so Cancel really cancels, and it shows live filter counts. |
| `Interop/NativeMethods.cs` | Win32 calls for monitor bounds in real pixels and precise window placement |
| `app.manifest` | Per-monitor DPI awareness, so mixed 4K and 1080p setups render correctly |

---

## Stage 6: Tune the content filter for your library

The filter only knows what your metadata tells it. Start with these steps:

1. Open the settings (`/c`) → **Library & filters**. Read the line "Hidden: N by the nudity/sexual content filter".
2. In Playnite, check what tags your games have. Filter the library by tags such as *Nudity* or *Sexual Content*
   and see what's there. Metadata from Steam, IGDB and add-ons all label things differently.
3. If your library has few descriptive tags, look in Playnite's add-on browser for a metadata add-on that imports
   store tags or content descriptors. That gives the filter much more to work with.
4. For anything the filter still misses, select those games in Playnite → right-click → **Edit** → add the tag
   `Screensaver: Hide`. You can edit many games at once.
5. If it hides a game by mistake, tag that game `Screensaver: Show`.

To be safe, run the screensaver in `/w` mode for a while after changing the filter, before you rely on it.

---

## Stage 7: Build release files and install for real

```powershell
./scripts/build-release.ps1 -Version 1.3.0
```

This runs the tests and then creates:

- `dist/PCGameCoverArt.scr`: one self-contained file, about 70 MB, because it bundles .NET so users don't
  need to install it
- `dist/PCGameCoverArtExporter_1.3.0.pext`: the Playnite add-on package, which is a zip file

The `-Version` number is stamped into both files and into the add-on's `extension.yaml`. Keep `<Version>` in both
`.csproj` files, `extension.yaml` and `app.manifest` in step with the latest release too, so everyday builds show the right number.

To install:

1. **Add-on:** remove the development folder from Playnite's *External extensions*, then drag the `.pext` onto
   Playnite.
2. **Screensaver:** open an **Administrator** PowerShell and run
   `./scripts/install-screensaver.ps1`. It copies the `.scr` into `System32` and opens Screen Saver Settings.
   Choose **PCGameCoverArt**, set the wait time, and click **Apply**.
   (Or just right-click the `.scr` → **Install**.)

✅ **Check:** the small preview monitor in the dialog animates, and **Preview** runs full screen.

To install a newer build over an older one, do the same two steps; there's no need to uninstall first. Playnite
offers to update the add-on, and a restart makes it export again. Close Screen Saver Settings before running the
install script: its preview keeps the old `.scr` running, and Windows can't replace a running file. The script
checks for this. When a release changes what the add-on exports (1.2.0 added play time and review scores), say in
the README's *Notes for version x.y.z* that Playnite users must update the add-on.

---

## Stage 8: Publish on GitHub

### 8.1 Personalise

Replace `YOUR-GITHUB-USERNAME` in these three places:

- `src/CoverArtExporter/extension.yaml`
- `src/CoverArtSaver/Windows/SettingsWindow.xaml.cs` (`RepositoryUrl`)
- the clone URL in the steps below

Check the copyright line in `LICENSE` too, and keep `<Copyright>` in `Directory.Build.props` in step with it: that's the copyright Windows shows in each file's Properties → Details and the About tab shows. If you add a library that ships inside the `.scr` or `.pext`, list it in `THIRD-PARTY-NOTICES.md`.

### 8.2 Create the repository and push

```powershell
cd C:\src\pc-game-cover-art-screensaver
gh auth login                      # one-time; choose GitHub.com → HTTPS → browser
git init -b main
git add .
git commit -m "Initial commit: PC Game Cover Art Screensaver"
gh repo create pc-game-cover-art-screensaver --public --source . --push `
   --description "Coverflow-style Windows screensaver using your Playnite game covers"
```

✅ **Check:** the repo page shows your README. The **Actions** tab shows a *Build* run. When it goes green, it has
built both files on a clean Windows machine and run the tests.

### 8.3 Make your first release

Releases come from Git tags. The workflow in `.github/workflows/build.yml` sees a `v*` tag, builds with that
version number, and attaches the files to a GitHub Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

✅ **Check:** after a few minutes, **Releases** shows *v1.0.0* with the `.scr` and `.pext` attached.

For later versions, commit your changes, push, and tag `v1.1.0`. There's nothing to edit by hand.

### 8.4 Make the project look good

- Record a short GIF of the screensaver (for example with ScreenToGif) and save it as `docs/screenshot.gif`.
  Then uncomment the image line near the top of the README.
- On the repo page, click ⚙ next to **About** and add topics: `playnite`, `screensaver`, `coverflow`, `wpf`, `windows`.
- Optional: list the add-on in Playnite's built-in add-on browser. Its submission process is described in the
  [PlayniteAddonDatabase](https://github.com/JosefNemec/PlayniteAddonDatabase) repository.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| "No Playnite library export found" | The add-on isn't loaded. Check Stage 3, then run *Export library for screensaver now*. |
| Black screen, no covers | Check `%LOCALAPPDATA%\PCGameCoverArt\screensaver.log`. Also check whether the settings dialog says all games are filtered out. |
| Screensaver isn't in the Windows list | The `.scr` must be in `C:\Windows\System32`. If you copied it with 32-bit PowerShell, it went to `SysWOW64` instead. |
| SmartScreen warning | Expected for unsigned apps: *More info → Run anyway*. Code-signing certificates cost money. That's optional. |
| Some covers show as title cards | The cover file is missing or unreadable. In Playnite, download metadata for those games again. |
| Wrong size on a second monitor | Report it with your monitor layout and scaling percentages. The fix belongs in `ScreensaverWindow.CoverMonitor`. |

## Ideas for next steps

- Arrow keys to flip covers manually in `/w` mode
- Blurred background art behind the centre cover
- Show only games from selected platforms or sources
- Add a "Most played" order (`GameEntry.PlaytimeSeconds` is already filled in for both Playnite and Steam)
