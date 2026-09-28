<p align="center"><img src="docs/logo.png" width="128" alt="logo"></p>

# PC Game Cover Art Screensaver

A Windows 10/11 screensaver that shows the cover art from your [Playnite](https://playnite.link) library. Choose between
two styles, both inspired by old iTunes screensavers:

- **Coverflow:** covers slide past in 3D, with angled side covers and reflections.
- **Mosaic:** a wall of covers that flip over one at a time, like the iTunes Album Artwork screensaver.

**[Download and install](#install)**

<!-- Add a screenshot or GIF here once you have one: ![screenshot](docs/screenshot.png) -->

## Features

- Smooth 3D coverflow with reflections, rendered with WPF
- **Mosaic style**, like iTunes' Album Artwork screensaver: a wall of covers that flip over one at a time. You choose how many covers go across; the number of rows follows your screen's shape
- Uses your local Playnite library; no internet connection or API keys
- Standard screensaver behavior: full screen on every monitor, live preview in Windows' Screen Saver Settings, and a settings dialog
- **Optional content filter** hides games with nudity or sexual content, based on their tags, genres, features, categories and age ratings. You can edit the word list.
- Optional stricter filter for anything rated Mature/18+
- Per-game override tags you set in Playnite: `Screensaver: Hide` and `Screensaver: Show`
- Order: random, alphabetical, recently played, recently added, release year
- Include or exclude hidden games, installed-only, favorites-only
- Cover shape: use all cover art, or only vertical (box art), only square, or only horizontal covers, so the screensaver looks consistent. In the mosaic, tiles take the chosen shape
- Multi-monitor: mirror, independent, or primary-only. Independent monitors take turns, so only one cover changes at a time across all your screens

## How it works

```
┌────────────── Playnite ───────────────┐          ┌──────── PCGameCoverArt.scr ────────┐
│ PC Game Cover Art Exporter add-on │  writes  │ reads library.json, filters games,    │
│ (runs whenever your library changes)  │ ───────► │ loads cover images from Playnite's    │
└───────────────────────────────────────┘          │ library folder, draws the coverflow   │
          %LOCALAPPDATA%\PCGameCoverArt\library.json  └───────────────────────────────────────┘
```

Playnite keeps its database locked while it runs, so the screensaver doesn't read it directly. A small Playnite
add-on exports a JSON snapshot of your library instead, and the screensaver reads that file.

## Install

You need:

- Windows 10 or 11 (64-bit)
- [Playnite](https://playnite.link) 10. The Playnite 11 beta uses a new add-on system that isn't supported yet.

The screensaver comes in two parts, and you need both:

| File | What it does |
|---|---|
| `PCGameCoverArtExporter_x.y.z.pext` | A Playnite add-on that saves a list of your games and their cover art for the screensaver to read |
| `PCGameCoverArt.scr` | The screensaver itself. It includes everything it needs, so you don't have to install .NET. |

### 1. Download

Go to the **[Releases page](../../releases/latest)** and download both files from the **Assets** list at the
bottom of the latest release.

Your browser may warn that the files aren't commonly downloaded, because they aren't code-signed. In Microsoft Edge,
click **…** next to the download → **Keep** → **Show more** → **Keep anyway**. In Chrome, click **Keep**.

### 2. Install the Playnite add-on

1. Double-click `PCGameCoverArtExporter_x.y.z.pext`, or drag it onto Playnite's window.
2. Click **Yes** when Playnite asks whether to install it.
3. Restart Playnite.

The add-on saves your library right away, and again a few seconds after anything in your library changes. Leave
it installed so the screensaver stays up to date.

To check that it works: in Playnite, open the main menu (☰) → **Extensions** → **PC Game Cover Art Screensaver** →
**Export library for screensaver now**. It tells you how many games it saved.

### 3. Install the screensaver

1. **Unblock the file.** Right-click `PCGameCoverArt.scr` → **Properties**. If there's an **Unblock** checkbox at
   the bottom of the **General** tab, tick it and click **OK**. Otherwise Windows may refuse to run a file downloaded
   from the internet as a screensaver.
2. **Copy it into Windows' screensaver folder.** Open File Explorer, go to `C:\Windows\System32`, and drag
   `PCGameCoverArt.scr` into it. Click **Continue** when Windows asks for administrator permission. This makes
   *PCGameCoverArt* appear in Windows' list of screensavers.

   <details>
   <summary>No administrator rights?</summary>

   Move `PCGameCoverArt.scr` to a folder where it can stay, for example `Documents\Screensavers`. Don't leave it in
   Downloads. Then right-click it → **Install**. This sets it as your screensaver, but it won't stay in Windows' list
   if you switch to another one later.

   </details>

### 4. Turn it on

1. Open Screen Saver Settings: press **Start**, type **screen saver**, and choose **Change screen saver**.
2. Under **Screen saver**, choose **PCGameCoverArt**.
3. Click **Settings…** to pick the style (Coverflow or Mosaic), filters and other options, then **OK**.
4. Set **Wait** to how many idle minutes to wait before it starts. Tick **On resume, display logon screen** if you
   want your PC to lock when you come back.
5. Click **Preview** to try it. Move the mouse or press a key to stop it.
6. Click **OK**.

### Update to a new version

Download the new files from the [Releases page](../../releases/latest). Install the new `.pext` the same way as
before, then unblock the new `.scr` and copy it into `C:\Windows\System32`. When Windows asks, choose **Replace the
file in the destination**. Your settings are kept.

### Uninstall

1. In Screen Saver Settings, choose a different screensaver (or *(None)*) and click **OK**.
2. Delete `C:\Windows\System32\PCGameCoverArt.scr`.
3. In Playnite, open the main menu (☰) → **Add-ons…** → **Installed** → **Generic**. Select **PC Game Cover Art Screensaver
   Exporter**, click **Uninstall**, and restart Playnite.
4. Optional: delete the folder `%LOCALAPPDATA%\PCGameCoverArt`, which holds your settings and the saved library.
   Paste that path into File Explorer's address bar to find it.

### Troubleshooting

| Problem | What to do |
|---|---|
| The screensaver says **"No Playnite library export found"** | The add-on hasn't saved your library yet. Make sure it's installed (step 2), restart Playnite, or use **Export library for screensaver now**. |
| It says **all games were filtered out**, or shows fewer games than you expect | Open **Settings… → Library & filters**. The box at the bottom shows how many games each filter hides. |
| A game you don't want to see still appears | In Playnite, add the tag `Screensaver: Hide` to it. |
| **PCGameCoverArt** isn't in Windows' list | Check that `PCGameCoverArt.scr` is in `C:\Windows\System32`, then reopen Screen Saver Settings. |
| It doesn't start, or closes immediately | Unblock the file (step 3.1) and copy it again. |
| Something else | Look in `%LOCALAPPDATA%\PCGameCoverArt\screensaver.log` and include it when you [open an issue](../../issues). |

## About the content filter

Playnite doesn't flag nudity by itself, so the filter can only use the metadata your games have. With the
default word list, a game is hidden if any of its tags, genres, features, categories or age ratings contains
one of these words (whole-word, not case-sensitive):

`Nudity`, `Sexual`, `Erotic`, `Hentai`, `NSFW`, `Eroge`, `Adults Only`, `ESRB AO`, `Adult Content`, `Porn`

The settings dialog shows how many games each rule hides. If the filter misses a game, add the `Screensaver: Hide` tag
to it in Playnite. If it hides a game by mistake, add `Screensaver: Show`.

## Building from source

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test tests/CoverArtSaver.Tests          # unit tests (run on any OS)
dotnet run --project src/CoverArtSaver -- /w   # try it in a normal window
./scripts/build-release.ps1                     # produces dist/*.scr and dist/*.pext
```

[docs/GUIDE.md](docs/GUIDE.md) is a full walkthrough of the project: the architecture, a tour of the code, testing, and releasing.

| Project | What it is |
|---|---|
| `src/CoverArtExporter` | Playnite add-on (.NET Framework 4.6.2, Playnite SDK) that writes `library.json` |
| `src/CoverArtSaver.Core` | UI-free logic: settings, filtering, ordering, command-line parsing, coverflow math |
| `src/CoverArtSaver` | WPF app that becomes the `.scr`: rendering, windows, settings dialog |
| `tests/CoverArtSaver.Tests` | xUnit tests for the Core project |

## Contributing

Issues and pull requests are welcome. Run `dotnet test` before submitting. By contributing, you agree that your
contributions are licensed under the project's [MIT License](LICENSE).

## License

PC Game Cover Art Screensaver is released under the [MIT License](LICENSE). Copyright (c) 2026 ShawnPlays.

You're free to use, copy, change and share it, including in other projects, as long as you keep the copyright
notice and license text. It comes with no warranty.

- The screensaver includes the .NET runtime, which is also MIT-licensed. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- The license and notices are included in both downloads: in the screensaver's **Settings… → About** tab, and as
  `LICENSE.txt` inside the add-on package.
- Game cover art belongs to its respective owners. This project doesn't include or distribute any.
