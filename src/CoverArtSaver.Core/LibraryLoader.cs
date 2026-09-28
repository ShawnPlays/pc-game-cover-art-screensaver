namespace CoverArtSaver.Core;

/// <summary>Result of loading + filtering the library. <see cref="Problem"/> is a user-facing message when something's wrong.</summary>
public sealed record LoadedLibrary(LibraryData? Data, FilterResult? Filter, string? Problem)
{
    public IReadOnlyList<GameEntry> Games => Filter?.Included ?? [];
}

public static class LibraryLoader
{
    public static LoadedLibrary Load(SaverSettings settings, Func<string, bool>? fileExists = null, Func<string, double?>? coverAspect = null)
    {
        var path = settings.LibraryFile;
        if (!File.Exists(path))
        {
            return new(null, null,
                "No Playnite library export found.\n\n" +
                "Install the \"PC Game Cover Art Exporter\" add-on in Playnite, then restart Playnite\n" +
                "(or use Extensions → PC Game Cover Art Screensaver → Export library for screensaver now).\n\n" +
                $"Expected file: {path}");
        }

        LibraryData data;
        try
        {
            data = LibraryData.Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read library file {path}", ex);
            return new(null, null, $"Couldn't read the library export:\n{ex.Message}");
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
