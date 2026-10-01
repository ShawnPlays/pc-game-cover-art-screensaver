using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoverArtSaver.Core;

public enum CoverOrder
{
    Random,
    Alphabetical,
    RecentlyPlayed,
    RecentlyAdded,
    ReleaseYear,
}

/// <summary>Where the list of games and their cover art comes from.</summary>
public enum LibrarySource
{
    /// <summary>A snapshot written by the PC Game Cover Art Exporter add-on inside Playnite.</summary>
    Playnite,
    /// <summary>Read directly from Steam's own files; no add-on needed.</summary>
    Steam,
}

public enum SaverLayout
{
    /// <summary>The classic sliding 3D coverflow.</summary>
    Coverflow,
    /// <summary>A wall of covers that flip over one at a time, like iTunes' Album Artwork screensaver.</summary>
    Mosaic,
}

public enum MultiMonitorMode
{
    /// <summary>Every monitor shows the same covers in sync.</summary>
    Mirror,
    /// <summary>Each monitor shuffles its own order.</summary>
    Independent,
    /// <summary>Coverflow on the primary monitor, other monitors go black.</summary>
    PrimaryOnly,
}

/// <summary>What the content filter does with the games one of its word lists matches.</summary>
public enum ContentFilterMode
{
    /// <summary>The word list is ignored.</summary>
    Off,
    /// <summary>Matching games are left out.</summary>
    Hide,
    /// <summary>Only matching games are shown.</summary>
    Only,
}

/// <summary>All user-configurable options. Saved as JSON in %LOCALAPPDATA%\PCGameCoverArt\settings.json.</summary>
public sealed class SaverSettings
{
    // ---- Display ----
    public SaverLayout Layout { get; set; } = SaverLayout.Coverflow;
    public double SecondsPerCover { get; set; } = 4.0;
    public double TransitionSeconds { get; set; } = 0.7;
    public CoverOrder Order { get; set; } = CoverOrder.Random;
    public int SideCovers { get; set; } = 6;
    public double SideAngle { get; set; } = 70;
    public bool ShowReflection { get; set; } = true;
    public bool ShowTitle { get; set; } = true;
    public bool ShowDetails { get; set; } = true;
    public int TextureHeight { get; set; } = 600;
    public MultiMonitorMode MultiMonitor { get; set; } = MultiMonitorMode.Mirror;

    /// <summary>With Independent monitors, only one monitor changes a cover at a time instead of all at once.</summary>
    public bool MonitorsTakeTurns { get; set; } = true;

    // ---- Mosaic ----
    /// <summary>Covers across the screen; the number of rows follows from the screen's shape.</summary>
    public int MosaicColumns { get; set; } = 10;

    /// <summary>Time between one tile flipping and the next.</summary>
    public double MosaicFlipSeconds { get; set; } = 1.5;

    /// <summary>Show games you've barely played as large tiles.</summary>
    public FeaturedTileSettings MosaicFeatured { get; set; } = new();

    // ---- Music ----
    /// <summary>Plays installed Steam soundtracks; always from Steam, whichever source the cover art comes from.</summary>
    public MusicSettings Music { get; set; } = new();

    /// <summary>The settings window asks GitHub whether there's a newer version. The screensaver itself never goes online.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>How far (in pixels) the mouse must move before the screensaver exits.</summary>
    public int MouseMoveThreshold { get; set; } = 12;

    // ---- Library ----
    public LibrarySource Source { get; set; } = LibrarySource.Playnite;

    /// <summary>Playnite only. Leave empty to use the add-on's default export location.</summary>
    public string? LibraryFileOverride { get; set; }

    /// <summary>Steam only. Leave empty to find Steam automatically.</summary>
    public string? SteamFolderOverride { get; set; }

    public FilterSettings Filter { get; set; } = new();

    [JsonIgnore]
    public string LibraryFile =>
        string.IsNullOrWhiteSpace(LibraryFileOverride) ? AppPaths.DefaultLibraryFile : LibraryFileOverride!;

    /// <summary>Clamp values so a hand-edited settings file can't break the renderer.</summary>
    public SaverSettings Sanitize()
    {
        SecondsPerCover = Math.Clamp(SecondsPerCover, 1, 120);
        TransitionSeconds = Math.Clamp(TransitionSeconds, 0.1, Math.Min(5, SecondsPerCover));
        SideCovers = Math.Clamp(SideCovers, 1, 12);
        SideAngle = Math.Clamp(SideAngle, 0, 85);
        TextureHeight = Math.Clamp(TextureHeight, 128, 2048);
        MosaicColumns = Math.Clamp(MosaicColumns, 2, 30);
        MosaicFlipSeconds = Math.Clamp(MosaicFlipSeconds, 0.2, 60);
        MouseMoveThreshold = Math.Clamp(MouseMoveThreshold, 0, 500);
        MosaicFeatured ??= new FeaturedTileSettings();
        MosaicFeatured.Sanitize();
        Music ??= new MusicSettings();
        Music.Volume = Math.Clamp(Music.Volume, 0, 100);
        if (string.IsNullOrWhiteSpace(Music.OutputDeviceId))
        {
            Music.OutputDeviceId = null;
            Music.OutputDeviceName = null;
        }
        Filter ??= new FilterSettings();
        Filter.AdultKeywords ??= [];
        Filter.MatureKeywords ??= [];
        return this;
    }

    public SaverSettings Clone() =>
        JsonSerializer.Deserialize<SaverSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

public sealed class MusicSettings
{
    public bool Enabled { get; set; }

    /// <summary>Random order. Off: album by album, in track order.</summary>
    public bool Shuffle { get; set; } = true;

    /// <summary>Without shuffle, start somewhere random in the album order instead of at the first track.</summary>
    public bool StartAtRandomTrack { get; set; }

    /// <summary>0–100.</summary>
    public int Volume { get; set; } = 40;

    /// <summary>Windows' ID for the speakers or headphones to play through. Empty: whatever Windows' default output is.</summary>
    public string? OutputDeviceId { get; set; }

    /// <summary>That device's name when it was chosen, so the settings can still show it while it's unplugged.</summary>
    public string? OutputDeviceName { get; set; }
}

public sealed class FilterSettings
{
    public bool IncludeHidden { get; set; }
    public bool InstalledOnly { get; set; }
    public bool FavoritesOnly { get; set; }

    /// <summary>When off, games with no cover image get a generated title card instead.</summary>
    public bool SkipGamesWithoutCover { get; set; } = true;

    /// <summary>Only show games whose cover art has this shape (see <see cref="CoverShapes.Classify"/>).</summary>
    public CoverShape CoverShape { get; set; } = CoverShape.All;

    /// <summary>Older settings files had a yes/no "vertical covers only" switch; read it so upgrading keeps the choice.</summary>
    [JsonInclude]
    [JsonPropertyName("VerticalCoversOnly")]
    private bool LegacyVerticalCoversOnly
    {
        set
        {
            if (value)
            {
                CoverShape = CoverShape.Vertical;
            }
        }
    }

    /// <summary>Hide (the default) or show only games with nudity or sexual content.</summary>
    public ContentFilterMode AdultContent { get; set; } = ContentFilterMode.Hide;

    /// <summary>Older settings files had a yes/no switch for hiding adult content; read it so upgrading keeps the choice.</summary>
    [JsonInclude]
    [JsonPropertyName("ExcludeAdultContent")]
    private bool LegacyExcludeAdultContent
    {
        set => AdultContent = value ? ContentFilterMode.Hide : ContentFilterMode.Off;
    }

    /// <summary>
    /// Matched as whole words, case-insensitively, against tags, genres, features, categories and
    /// age ratings. "Nudity" matches "Partial Nudity" and "Some Nudity or Sexual Content",
    /// but "Sexual" won't match "Asexual".
    /// </summary>
    public List<string> AdultKeywords { get; set; } = [.. DefaultAdultKeywords];

    /// <summary>Optional: hide, or show only, anything rated for adults (M / 18+), violence included.</summary>
    public ContentFilterMode MatureRatings { get; set; } = ContentFilterMode.Off;

    [JsonInclude]
    [JsonPropertyName("ExcludeMatureRatings")]
    private bool LegacyExcludeMatureRatings
    {
        set => MatureRatings = value ? ContentFilterMode.Hide : ContentFilterMode.Off;
    }

    public List<string> MatureKeywords { get; set; } = [.. DefaultMatureKeywords];

    /// <summary>Add this tag to a game in Playnite to always hide it from the screensaver.</summary>
    public string HideTag { get; set; } = "Screensaver: Hide";

    /// <summary>Add this tag in Playnite to force-show a game the content filter caught by mistake.</summary>
    public string ShowTag { get; set; } = "Screensaver: Show";

    public static readonly string[] DefaultAdultKeywords =
    [
        "Nudity",           // ESRB "Nudity"/"Partial Nudity", Steam "Nudity", "Some Nudity or Sexual Content"
        "Sexual",           // "Sexual Content", "Strong Sexual Content", "Sexual Themes", "Adult Only Sexual Content"
        "Erotic",           // IGDB theme
        "Hentai",
        "NSFW",
        "Eroge",
        "Adults Only",      // ESRB AO spelled out
        "ESRB AO",
        "Adult Content",
        "Porn",
    ];

    public static readonly string[] DefaultMatureKeywords =
    [
        "ESRB M",
        "PEGI 18",
        "USK 18",
        "CERO Z",
        "ACB R18+",
        "GRAC 19",
        "Mature",
    ];
}

public static class SettingsStore
{
    public static SaverSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<SaverSettings>(File.ReadAllText(path), SaverSettings.JsonOptions);
                if (s != null)
                {
                    return s.Sanitize();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings; using defaults.", ex);
        }

        return new SaverSettings().Sanitize();
    }

    public static void Save(SaverSettings settings, string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings.Sanitize(), SaverSettings.JsonOptions));
    }
}
