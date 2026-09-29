namespace CoverArtSaver.Core.Steam;

/// <summary>One music file from an installed Steam soundtrack.</summary>
public sealed record SoundtrackTrack(string Album, string Path);

/// <summary>The soundtracks found, plus a one-line summary for the settings dialog.</summary>
public sealed record SoundtrackScan(IReadOnlyList<SoundtrackTrack> Tracks, string Status);

/// <summary>
/// Finds the soundtracks installed through Steam. Steam puts each one in its own folder under
/// steamapps\music in whichever library folder it was installed to. Playnite doesn't keep track of soundtracks,
/// so this always reads Steam, whichever source the cover art comes from.
/// </summary>
public static class SteamSoundtracks
{
    /// <summary>
    /// Formats Windows plays without extra codecs, most preferred first. Many soundtracks come in several formats at
    /// once (MP3 + FLAC + WAV); only one format per album is played so every track isn't heard two or three times.
    /// MP3 comes first because it's always supported and quick to load.
    /// </summary>
    internal static readonly string[] Formats = [".mp3", ".flac", ".m4a", ".wma", ".wav"];

    public static SoundtrackScan Scan(SaverSettings settings)
    {
        var folder = string.IsNullOrWhiteSpace(settings.SteamFolderOverride)
            ? SteamLibrary.FindSteamFolder()
            : settings.SteamFolderOverride;
        if (folder == null || !SteamLibrary.IsSteamFolder(folder))
        {
            return new([], "⚠ Steam wasn't found, so there's no music to play." +
                " If it's installed somewhere unusual, choose its folder on the Library & filters tab.");
        }

        try
        {
            var tracks = Find(folder);
            var albums = tracks.Select(t => t.Album).Distinct().Count();
            return tracks.Count == 0
                ? new([], "⚠ No Steam soundtracks are installed. Soundtracks you own are in your Steam library " +
                          "(choose Soundtracks in the list's filter) and install like games.")
                : new(tracks, $"✔ {tracks.Count} tracks from {albums} soundtrack{(albums == 1 ? "" : "s")}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Couldn't read Steam soundtracks", ex);
            return new([], "⚠ Couldn't read the soundtrack folders: " + ex.Message);
        }
    }

    /// <summary>Every playable track, album by album (A–Z), each album in track order.</summary>
    public static IReadOnlyList<SoundtrackTrack> Find(string steamFolder)
    {
        var albums = SteamLibrary.LibraryFolders(steamFolder)
            .Select(library => Path.Combine(library, "steamapps", "music"))
            .Where(Directory.Exists) // a removable drive that isn't plugged in, or no soundtracks there
            .SelectMany(Directory.EnumerateDirectories)
            .OrderBy(Path.GetFileName, NaturalComparer.Instance);

        return [.. albums.SelectMany(album => AlbumTracks(album).Select(path => new SoundtrackTrack(Path.GetFileName(album), path)))];
    }

    /// <summary>The album's files in its preferred format, in track order (sorted by path, so "Disc 1" comes before "Disc 2").</summary>
    internal static IEnumerable<string> AlbumTracks(string albumFolder)
    {
        var files = Directory.EnumerateFiles(albumFolder, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("._", StringComparison.Ordinal)) // macOS leftovers
            .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());
        var format = Formats.FirstOrDefault(files.ContainsKey);
        return format == null
            ? []
            : files[format].OrderBy(f => Path.GetRelativePath(albumFolder, f), NaturalComparer.Instance);
    }
}

/// <summary>
/// The order a music player uses: it runs through the tracks one after another, forever. Shuffled, it plays every
/// track once in a random order, then reshuffles, never playing the same track twice in a row. Not shuffled, it
/// plays them in album order, starting either at the first track or at a random one.
/// </summary>
public sealed class SoundtrackPlaylist
{
    private readonly IReadOnlyList<SoundtrackTrack> tracks;
    private readonly bool shuffle;
    private readonly Random rng;
    private int[] order;
    private int position;

    public SoundtrackPlaylist(IReadOnlyList<SoundtrackTrack> tracks, bool shuffle, bool startAtRandomTrack, int seed)
    {
        this.tracks = tracks;
        this.shuffle = shuffle;
        rng = new Random(seed);
        order = [.. Enumerable.Range(0, tracks.Count)];
        if (shuffle)
        {
            rng.Shuffle(order);
        }
        else if (startAtRandomTrack && tracks.Count > 0)
        {
            position = rng.Next(tracks.Count);
        }
    }

    public int Count => tracks.Count;

    /// <summary>The track to play next, or null if there are none.</summary>
    public SoundtrackTrack? Next()
    {
        if (tracks.Count == 0)
        {
            return null;
        }

        if (position == order.Length)
        {
            position = 0;
            if (shuffle)
            {
                var last = order[^1];
                rng.Shuffle(order);
                if (order.Length > 1 && order[0] == last)
                {
                    (order[0], order[^1]) = (order[^1], order[0]);
                }
            }
        }

        return tracks[order[position++]];
    }
}

/// <summary>Compares text with runs of digits as numbers, so "Track 2" sorts before "Track 10".</summary>
internal sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x == null || y == null)
        {
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var numberX = x[startX..i].TrimStart('0');
                var numberY = y[startY..j].TrimStart('0');
                var byNumber = numberX.Length != numberY.Length
                    ? numberX.Length.CompareTo(numberY.Length)
                    : string.CompareOrdinal(numberX, numberY);
                if (byNumber != 0)
                {
                    return byNumber;
                }
            }
            else
            {
                var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (byChar != 0)
                {
                    return byChar;
                }

                i++;
                j++;
            }
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
