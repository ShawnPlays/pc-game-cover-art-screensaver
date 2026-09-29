<p align="center"><img src="docs/logo.png" width="128" alt="logo"></p>

# PC Game Cover Art Screensaver

A Windows 10/11 screensaver that shows the cover art from your [Playnite](https://playnite.link) or
[Steam](https://store.steampowered.com) library. Choose between two styles, both inspired by old iTunes screensavers:

- **Coverflow:** covers slide past in 3D, with angled side covers and reflections.
- **Mosaic:** a wall of covers that flip over one at a time, like the iTunes Album Artwork screensaver.

**[Download and install](#install)**

<!-- Add a screenshot or GIF here once you have one: ![screenshot](docs/screenshot.png) -->

## Features

- Smooth 3D coverflow with reflections, rendered with WPF
- **Mosaic style**, like iTunes' Album Artwork screensaver: a wall of covers that flip over one at a time. You choose how many covers go across; the number of rows follows your screen's shape
- **Backlog spotlight** (mosaic, optional): games you've barely played get large tiles, 2×2 up to 6×6, that move around the wall as they flip. You choose how many (1–4), the most hours played that still counts (0 = never played), and a minimum review rating in Steam's terms (Mixed up to Overwhelmingly Positive). For Playnite that's the Community Score, or the Critic Score if there's none
- Works with **Playnite** (all your launchers in one library, through a small add-on) or **Steam** (read directly from
  Steam's own files, with nothing extra to install). No internet connection, account login or API keys
- Standard screensaver behavior: full screen on every monitor, live preview in Windows' Screen Saver Settings, and a settings dialog
- **Optional content filter** hides games with nudity or sexual content, based on their tags, genres, features, categories and age ratings (for Steam: its store tags and content descriptors). You can edit the word list.
- Optional stricter filter for anything rated Mature/18+
- Per-game overrides: `Screensaver: Hide` and `Screensaver: Show`, as tags in Playnite or as collections in Steam
- Order: random, alphabetical, recently played, recently added, release year
- Include or exclude hidden games, installed-only, favorites-only
- Cover shape: use all cover art, or only vertical (box art), only square, or only horizontal covers, so the screensaver looks consistent. In the mosaic, tiles take the chosen shape
- Multi-monitor: mirror, independent, or primary-only. Independent monitors take turns, so only one cover changes at a time across all your screens

## How it works

The screensaver can get your games from either Playnite or Steam. You choose which in its settings.

```
 Playnite ─── PC Game Cover Art Exporter add-on ───► library.json ──┐
                                                                    ├──► PCGameCoverArt.scr
 Steam ─────── Steam's own files (read directly) ───────────────────┘      filters games, draws the covers
```

- **Playnite** keeps its database locked while it runs, so a small Playnite add-on exports a snapshot of your library
  to `%LOCALAPPDATA%\PCGameCoverArt\library.json`, and the screensaver reads that file.
- **Steam** needs no add-on. The screensaver reads the cover art Steam has already downloaded for your library, plus
  Steam's own records of game details, installed games, collections and when you last played. Steam doesn't need to
  be running.

## Install

Already have an earlier version? Follow [Upgrade from an earlier version](#upgrade-from-an-earlier-version) instead.

You need:

- Windows 10 or 11 (64-bit)
- **Either** [Playnite](https://playnite.link) 10 (the Playnite 11 beta uses a new add-on system that isn't supported
  yet) **or** [Steam](https://store.steampowered.com)

The download has two files:

| File | What it does | Who needs it |
|---|---|---|
| `PCGameCoverArt.scr` | The screensaver itself. It includes everything it needs, so you don't have to install .NET. | Everyone |
| `PCGameCoverArtExporter_x.y.z.pext` | A Playnite add-on that saves a list of your games and their cover art for the screensaver to read | Playnite users only |

### 1. Download

Go to the **[Releases page](../../releases/latest)** and download the files you need from the **Assets** list at the
bottom of the latest release.

Your browser may warn that the files aren't commonly downloaded, because they aren't code-signed. In Microsoft Edge,
click **…** next to the download → **Keep** → **Show more** → **Keep anyway**. In Chrome, click **Keep**.

### 2. Install the Playnite add-on (Playnite only)

**Using Steam?** Skip this step. Steam needs nothing extra, but open your library in Steam at least once so it has
downloaded your games' cover art.

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
3. Click **Settings…**. On the **Library & filters** tab, set **Get games from** to **Playnite** or **Steam**. Then pick
   the style (Coverflow or Mosaic), filters and other options, and click **OK**. With Mosaic, you can also turn on
   large tiles for games you've barely played, on the **Display** tab.
4. Set **Wait** to how many idle minutes to wait before it starts. Tick **On resume, display logon screen** if you
   want your PC to lock when you come back.
5. Click **Preview** to try it. Move the mouse or press a key to stop it.
6. Click **OK**.

### Upgrade from an earlier version

You don't need to uninstall anything first. Your settings are kept, and any new options start switched off.

1. **Download** the new files from the [Releases page](../../releases/latest), and unblock the new `.scr` as in
   step 3.1.
2. **Update the Playnite add-on first** (Playnite only; Steam users skip this step).
   1. Double-click the new `PCGameCoverArtExporter_x.y.z.pext`, or drag it onto Playnite's window.
   2. Playnite says the add-on is already installed and asks whether to update it. Click **Yes**.
   3. Restart Playnite. The new add-on saves your library again on startup. To be sure it has, use main menu (☰) →
      **Extensions** → **PC Game Cover Art Screensaver** → **Export library for screensaver now**.
3. **Close Screen Saver Settings** if it's open. Its little preview runs the screensaver, and Windows won't replace a
   file while it's running.
4. **Replace the screensaver file.** Copy the new `PCGameCoverArt.scr` into `C:\Windows\System32`. When Windows asks,
   choose **Replace the file in the destination**, then click **Continue** for administrator permission.
   If you installed it without administrator rights, copy the new file over the old one in the folder you chose,
   then right-click it → **Install**.
5. **Check the version.** Open Screen Saver Settings → **Settings…** → **About**. In Playnite, the add-on's version
   is under main menu (☰) → **Add-ons…** → **Installed** → **Generic**.

#### Notes for version 1.2.0

- New in 1.2.0: Mosaic can show games you've barely played as large tiles. Turn it on under
  **Settings… → Display → Mosaic**.
- **Playnite users must update the add-on** (step 2) to use large tiles. Older add-ons don't save play time or review
  scores, so until the new add-on has saved your library, no Playnite game counts as barely played.
- Steam users don't need to do anything extra: play time and review ratings are read from Steam's own files.

### Uninstall

1. In Screen Saver Settings, choose a different screensaver (or *(None)*) and click **OK**.
2. Delete `C:\Windows\System32\PCGameCoverArt.scr`.
3. If you use Playnite: open the main menu (☰) → **Add-ons…** → **Installed** → **Generic**. Select **PC Game Cover Art
   Exporter**, click **Uninstall**, and restart Playnite.
4. Optional: delete the folder `%LOCALAPPDATA%\PCGameCoverArt`, which holds your settings and the saved library.
   Paste that path into File Explorer's address bar to find it.

### Troubleshooting

| Problem | What to do |
|---|---|
| The screensaver says **"No Playnite library export found"** | The add-on hasn't saved your library yet. Make sure it's installed (step 2), restart Playnite, or use **Export library for screensaver now**. |
| It says **all games were filtered out**, or shows fewer games than you expect | Open **Settings… → Library & filters**. The box at the bottom shows how many games each filter hides. |
| A game you don't want to see still appears | In Playnite, add the tag `Screensaver: Hide` to it. In Steam, add it to a collection named `Screensaver: Hide`. |
| **Steam wasn't found** | In **Settings… → Library & filters**, click **Browse…** next to the Steam folder and choose the folder Steam is installed in. |
| A Steam game is missing, or shows a title card instead of its cover | Steam hasn't downloaded its artwork yet. Open your library in Steam and scroll past the game, then start the screensaver again. |
| The wrong Steam account's games appear | The screensaver uses the account that signed in to Steam most recently. Sign in to Steam with the account you want once. |
| Mosaic shows no large tiles | Under **Settings… → Display → Mosaic**, the line below the large tile options says how many games qualify. If it's 0, raise **Played for at most** or lower **Review rating**. Playnite users: update the add-on (see [Upgrade](#upgrade-from-an-earlier-version)) and use **Export library for screensaver now**. Playnite games also need a Community or Critic Score, which comes from downloading metadata. |
| Windows says the file is in use when you replace `PCGameCoverArt.scr` | Close Screen Saver Settings (its preview is running the old version) and try again. |
| **PCGameCoverArt** isn't in Windows' list | Check that `PCGameCoverArt.scr` is in `C:\Windows\System32`, then reopen Screen Saver Settings. |
| It doesn't start, or closes immediately | Unblock the file (step 3.1) and copy it again. |
| Something else | Look in `%LOCALAPPDATA%\PCGameCoverArt\screensaver.log` and include it when you [open an issue](../../issues). |

## About the content filter

Playnite doesn't flag nudity by itself, so the filter can only use the metadata your games have. With the
default word list, a game is hidden if any of its tags, genres, features, categories or age ratings contains
one of these words (whole-word, not case-sensitive):

`Nudity`, `Sexual`, `Erotic`, `Hentai`, `NSFW`, `Eroge`, `Adults Only`, `ESRB AO`, `Adult Content`, `Porn`

For **Steam** games, the filter checks their store tags (such as *Nudity* and *Sexual Content*) and the content
descriptors publishers fill in for Steam (such as *Some Nudity or Sexual Content* and *Adult Only Sexual Content*).
These contain the same words, so the default list catches them too.

The settings dialog shows how many games each rule hides. If the filter misses a game, add the `Screensaver: Hide` tag
to it in Playnite, or add it to a Steam collection with that name. If it hides a game by mistake, use
`Screensaver: Show` the same way.

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
