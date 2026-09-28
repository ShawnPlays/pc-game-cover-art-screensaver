using CoverArtSaver.Core.Steam;

namespace CoverArtSaver.Core;

/// <summary>Result of loading + filtering the library. <see cref="Problem"/> is a user-facing message when something's wrong.</summary>
public sealed record LoadedLibrary(LibraryData? Data, FilterResult? Filter, string? Problem)
{
    public IReadOnlyList<GameEntry> Games => Filter?.Included ?? [];
}

/// <summary>
/// The unfiltered game list from the chosen source. <see cref="Problem"/> explains (for the screensaver) why there's
/// no data; <see cref="Status"/> is a one-line summary for the settings dialog.
/// </summary>
public sealed record LibraryReadResult(LibraryData? Data, string? Problem, string Status);

/// <summary>Reads the game list from Playnite (via the add-on's export) or straight from Steam.</summary>
public static class LibrarySources
{
    public static LibraryReadResult Read(SaverSettings settings) =>
        settings.Source == LibrarySource.Steam ? ReadSteam(settings) : ReadPlaynite(settings);

    private static LibraryReadResult ReadPlaynite(SaverSettings settings)
    {
        var path = settings.LibraryFile;
        if (!File.Exists(path))
        {
            return new(null,
                "No Playnite library export found.\n\n" +
                "Install the \"PC Game Cover Art Exporter\" add-on in Playnite, then restart Playnite\n" +
                "(or use Extensions → PC Game Cover Art Screensaver → Export library for screensaver now).\n\n" +
                $"Expected file: {path}",
                $"⚠ No export found at {path}.\nInstall the \"PC Game Cover Art Exporter\" add-on in Playnite and restart Playnite.");
        }

        try
        {
            var data = LibraryData.Load(path);
            return new(data, null, $"✔ {data.Games.Count} games, exported {DescribeAge(DateTime.UtcNow - data.ExportedAt)} ago.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read library file {path}", ex);
            return new(null, $"Couldn't read the library export:\n{ex.Message}", "⚠ Couldn't read the export: " + ex.Message);
        }
    }

    private static LibraryReadResult ReadSteam(SaverSettings settings)
    {
        var folder = string.IsNullOrWhiteSpace(settings.SteamFolderOverride)
            ? SteamLibrary.FindSteamFolder()
            : settings.SteamFolderOverride;
        if (folder == null || !SteamLibrary.IsSteamFolder(folder))
        {
            var where = folder == null ? "Steam doesn't seem to be installed." : $"Steam wasn't found in {folder}.";
            return new(null,
                where + "\n\nInstall Steam, or open the screensaver settings and choose where Steam is installed.",
                "⚠ " + where + " Choose the folder Steam is installed in.");
        }

        try
        {
            var result = SteamLibrary.Read(folder, settings.Filter.CoverShape);
            var count = result.Library.Games.Count;
            if (count == 0)
            {
                return new(null,
                    "No Steam games were found.\n\nOpen your library in Steam once so it downloads the cover art, then try again.",
                    $"⚠ No games found in {folder}. Open your library in Steam once so it downloads the cover art.");
            }

            var account = result.AccountName == null ? "" : $" for {result.AccountName}";
            return new(result.Library, null, $"✔ {count} Steam games{account}, read from {folder}.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read the Steam library in {folder}", ex);
            return new(null, $"Couldn't read the Steam library:\n{ex.Message}", "⚠ Couldn't read Steam's files: " + ex.Message);
        }
    }

    private static string DescribeAge(TimeSpan age) =>
        age.TotalMinutes < 1 ? "just now"
        : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min"
        : age.TotalDays < 1 ? $"{(int)age.TotalHours} h"
        : $"{(int)age.TotalDays} days";
}

public static class LibraryLoader
{
    public static LoadedLibrary Load(SaverSettings settings, Func<string, bool>? fileExists = null, Func<string, double?>? coverAspect = null)
    {
        var read = LibrarySources.Read(settings);
        if (read.Data is not { } data)
        {
            return new(null, null, read.Problem);
        }

        if (settings.Filter.CoverShape != CoverShape.All && coverAspect == null)
        {
            // Measure all covers up front, in parallel, and remember them for next time.
            var sizes = CoverSizeCache.LoadDefault();
            sizes.Measure(data.Games.Select(g => g.CoverPath));
            sizes.Save();
            coverAspect = sizes.GetAspect;
        }

        var filter = new GameFilter(settings.Filter, fileExists, coverAspect).Apply(data.Games);
        if (filter.Included.Count == 0)
        {
            return new(data, filter,
                $"All {data.Games.Count} games were filtered out.\nOpen the screensaver settings to loosen the filters.");
        }

        return new(data, filter, null);
    }
}
