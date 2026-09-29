using System.Text.Json;
using Microsoft.Win32;

namespace CoverArtSaver.Core.Steam;

/// <summary>What was read from Steam, plus who it belongs to (for the settings dialog).</summary>
public sealed record SteamLibraryResult(LibraryData Library, string SteamFolder, string? AccountName);

/// <summary>
/// Builds the game list straight from Steam's own files, with no add-on, login or internet connection:
/// <list type="bullet">
/// <item>games: everything Steam has cached library artwork for, plus installed games, keeping only apps whose
/// type is "game" (so no DLC, tools, soundtracks or demos)</item>
/// <item>details (name, genres, store tags, content descriptors, release year, user review rating): appcache\appinfo.vdf</item>
/// <item>cover art: appcache\librarycache, with any custom artwork from userdata\&lt;id&gt;\config\grid taking priority</item>
/// <item>installed: steamapps\appmanifest_*.acf in every library folder</item>
/// <item>hidden, favorites and collections: userdata\&lt;id&gt;\config\cloudstorage</item>
/// <item>last played and play time: userdata\&lt;id&gt;\config\localconfig.vdf</item>
/// </list>
/// Collections become tags, so a Steam collection called "Screensaver: Hide" works like the Playnite tag.
/// </summary>
public static class SteamLibrary
{
    /// <summary>SteamID64 of account 0; userdata folders are named by (SteamID64 − this).</summary>
    private const long SteamId64Base = 76561197960265728;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Steam's genre ids (they're stored as numbers).</summary>
    private static readonly Dictionary<string, string> Genres = new()
    {
        ["1"] = "Action", ["2"] = "Strategy", ["3"] = "RPG", ["4"] = "Casual", ["9"] = "Racing", ["18"] = "Sports",
        ["23"] = "Indie", ["25"] = "Adventure", ["28"] = "Simulation", ["29"] = "Massively Multiplayer",
        ["37"] = "Free to Play", ["70"] = "Early Access", ["71"] = "Sexual Content", ["72"] = "Nudity",
        ["73"] = "Violent", ["74"] = "Gore",
    };

    /// <summary>
    /// Steam's content descriptors: the publisher's own mature-content survey answers. Their names contain the
    /// words the default content filter looks for ("Nudity", "Sexual", "Mature").
    /// </summary>
    private static readonly Dictionary<string, string> ContentDescriptors = new()
    {
        ["1"] = "Some Nudity or Sexual Content",
        ["2"] = "Frequent Violence or Gore",
        ["3"] = "Adult Only Sexual Content",
        ["4"] = "Frequent Nudity or Sexual Content",
        ["5"] = "General Mature Content",
    };

    /// <summary>Where Steam is installed, from the registry, or null if it can't be found.</summary>
    public static string? FindSteamFolder()
    {
        var candidates = new List<string?>();
        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
            candidates.Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => Path.GetFullPath(c!.Replace('/', '\\')))
            .FirstOrDefault(IsSteamFolder);
    }

    public static bool IsSteamFolder(string folder) =>
        File.Exists(Path.Combine(folder, "appcache", "appinfo.vdf")) || File.Exists(Path.Combine(folder, "steam.exe"));

    /// <param name="preferredShape">Which of Steam's cover images to use: its vertical "library" cover or its horizontal header.</param>
    public static SteamLibraryResult Read(string steamFolder, CoverShape preferredShape)
    {
        var (userFolder, accountName) = FindAccount(steamFolder);
        var installed = ReadInstalledApps(steamFolder);
        var cacheFolder = Path.Combine(steamFolder, "appcache", "librarycache");
        var gridFolder = userFolder == null ? null : Path.Combine(userFolder, "config", "grid");
        var collections = userFolder == null ? new Collections() : ReadCollections(userFolder);
        var played = userFolder == null ? [] : ReadPlayStats(userFolder);

        var candidates = new HashSet<uint>(installed.Keys);
        candidates.UnionWith(CachedAppIds(cacheFolder));

        var appInfoPath = Path.Combine(steamFolder, "appcache", "appinfo.vdf");
        var appInfo = File.Exists(appInfoPath) ? SteamAppInfo.ReadCommon(appInfoPath, candidates) : [];

        var games = new List<GameEntry>();
        foreach (var appId in candidates)
        {
            var isInstalled = installed.TryGetValue(appId, out var manifestName);
            var known = appInfo.TryGetValue(appId, out var common);
            common ??= VdfNode.Empty;

            // Keep games only (not DLC, tools, soundtracks...). If Steam has no details for an app, keep it only if installed.
            if (known ? !string.Equals(common["type"].Value, "game", StringComparison.OrdinalIgnoreCase) : !isInstalled)
            {
                continue;
            }

            var id = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var name = common["name"].Value ?? manifestName ?? $"Steam app {id}";
            var stats = played.GetValueOrDefault(appId);
            games.Add(new GameEntry
            {
                Id = "steam:" + id,
                Name = name,
                SortingName = common["sortas"].Value,
                CoverPath = FindCover(id, preferredShape, cacheFolder, gridFolder),
                Hidden = collections.Hidden.Contains(appId),
                Favorite = collections.Favorites.Contains(appId),
                IsInstalled = isInstalled,
                LastActivity = stats.LastPlayed,
                PlaytimeSeconds = stats.Minutes * 60, // Steam lists every game you've played; not listed = never played
                SteamReviewScore = common["review_score"].AsLong() is long score and >= 1 and <= 9 ? (int)score : null,
                ReleaseYear = ReleaseYear(common),
                Source = "Steam",
                Library = "Steam",
                Platforms = ["Steam"],
                Genres = [.. common["genres"].Children.Select(g => Genres.GetValueOrDefault(g.Value.Value ?? "")).OfType<string>()],
                Tags =
                [
                    .. common["store_tags"].Children
                        .Select(t => int.TryParse(t.Value.Value, out var tag) ? SteamTags.Names.GetValueOrDefault(tag) : null)
                        .OfType<string>(),
                    .. collections.Named.GetValueOrDefault(appId) ?? [],
                ],
                AgeRatings =
                [
                    .. common["content_descriptors"].Children.Concat(common["content_descriptors_including_dlc"].Children)
                        .Select(d => ContentDescriptors.GetValueOrDefault(d.Value.Value ?? ""))
                        .OfType<string>()
                        .Distinct(),
                ],
            });
        }

        var library = new LibraryData
        {
            SchemaVersion = LibraryData.SupportedSchemaVersion,
            ExportedAt = DateTime.UtcNow,
            Games = games,
        };
        return new SteamLibraryResult(library, steamFolder, accountName);
    }

    /// <summary>The most recently signed-in account (Steam supports several on one PC).</summary>
    private static (string? UserFolder, string? AccountName) FindAccount(string steamFolder)
    {
        var userdata = Path.Combine(steamFolder, "userdata");
        var loginUsersPath = Path.Combine(steamFolder, "config", "loginusers.vdf");
        var users = File.Exists(loginUsersPath) ? Vdf.Load(loginUsersPath)["users"].Children.ToList() : [];
        var recent = users.FirstOrDefault(u => u.Value["MostRecent"].Value == "1");
        if (recent.Key == null && users.Count > 0)
        {
            recent = users.MaxBy(u => u.Value["Timestamp"].AsLong() ?? 0);
        }

        if (recent.Key != null && long.TryParse(recent.Key, out var steamId64))
        {
            var folder = Path.Combine(userdata, (steamId64 - SteamId64Base).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Directory.Exists(folder))
            {
                return (folder, recent.Value["PersonaName"].Value);
            }
        }

        // No sign-in record: fall back to whichever account used Steam last.
        var latest = Directory.Exists(userdata)
            ? new DirectoryInfo(userdata).GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, "config", "localconfig.vdf")))
                .MaxBy(d => File.GetLastWriteTimeUtc(Path.Combine(d.FullName, "config", "localconfig.vdf")))
            : null;
        return (latest?.FullName, null);
    }

    /// <summary>Every folder Steam installs into: its own, plus any added under Settings → Storage.</summary>
    internal static List<string> LibraryFolders(string steamFolder)
    {
        var libraryFolders = new List<string> { steamFolder };
        var foldersFile = Path.Combine(steamFolder, "steamapps", "libraryfolders.vdf");
        if (File.Exists(foldersFile))
        {
            libraryFolders.AddRange(Vdf.Load(foldersFile)["libraryfolders"].Children
                .Select(f => f.Value["path"].Value)
                .OfType<string>());
        }

        return [.. libraryFolders.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>App id → name for every installed app, across all Steam library folders.</summary>
    private static Dictionary<uint, string?> ReadInstalledApps(string steamFolder)
    {
        var installed = new Dictionary<uint, string?>();
        foreach (var folder in LibraryFolders(steamFolder))
        {
            var steamApps = Path.Combine(folder, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue; // e.g. a removable drive that isn't plugged in
            }

            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                try
                {
                    var state = Vdf.Load(manifest)["AppState"];
                    // StateFlags bit 4 = fully installed (not just queued or partly downloaded).
                    if (uint.TryParse(state["appid"].Value, out var appId) && ((state["StateFlags"].AsLong() ?? 0) & 4) != 0)
                    {
                        installed[appId] = state["name"].Value;
                    }
                }
                catch (IOException)
                {
                    // Steam is rewriting it right now; skip.
                }
            }
        }

        return installed;
    }

    /// <summary>App ids with cached artwork, in either Steam's current per-app folders or its older flat file names.</summary>
    private static IEnumerable<uint> CachedAppIds(string cacheFolder)
    {
        if (!Directory.Exists(cacheFolder))
        {
            return [];
        }

        var fromFolders = Directory.EnumerateDirectories(cacheFolder).Select(Path.GetFileName);
        var fromFiles = Directory.EnumerateFiles(cacheFolder, "*_*.jpg").Select(f => Path.GetFileName(f).Split('_')[0]);
        return fromFolders.Concat(fromFiles)
            .Select(s => uint.TryParse(s, out var id) ? id : 0)
            .Where(id => id != 0)
            .Distinct();
    }

    private static string? FindCover(string appId, CoverShape preferredShape, string cacheFolder, string? gridFolder)
    {
        // Steam has used two naming schemes: library_600x900/header, and (in hashed subfolders) library_capsule/library_header.
        var vertical = FindCustomArt(gridFolder, appId + "p")
            ?? FindCachedArt(cacheFolder, appId, "library_600x900")
            ?? FindCachedArt(cacheFolder, appId, "library_capsule");
        var horizontal = FindCustomArt(gridFolder, appId)
            ?? FindCachedArt(cacheFolder, appId, "header")
            ?? FindCachedArt(cacheFolder, appId, "library_header");
        return preferredShape == CoverShape.Horizontal ? horizontal ?? vertical : vertical ?? horizontal;
    }

    /// <summary>Artwork the user set in Steam (Properties → Set custom artwork), saved as grid\&lt;appid&gt;p.png etc.</summary>
    private static string? FindCustomArt(string? gridFolder, string baseName) =>
        gridFolder == null
            ? null
            : ImageExtensions.Select(ext => Path.Combine(gridFolder, baseName + ext)).FirstOrDefault(File.Exists);

    private static string? FindCachedArt(string cacheFolder, string appId, string name)
    {
        var appFolder = Path.Combine(cacheFolder, appId);
        if (Directory.Exists(appFolder))
        {
            var direct = Path.Combine(appFolder, name + ".jpg");
            if (File.Exists(direct))
            {
                return direct;
            }

            // Some Steam versions keep assets in hashed subfolders, or add a suffix such as _2x.
            var nested = Directory.EnumerateFiles(appFolder, name + "*.jpg", SearchOption.AllDirectories).FirstOrDefault();
            if (nested != null)
            {
                return nested;
            }
        }

        var flat = Path.Combine(cacheFolder, $"{appId}_{name}.jpg"); // older Steam: one flat folder
        return File.Exists(flat) ? flat : null;
    }

    private static int? ReleaseYear(VdfNode common)
    {
        var seconds = common["steam_release_date"].AsLong() ?? common["original_release_date"].AsLong();
        return seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value).Year : null;
    }

    private readonly record struct PlayStats(DateTime? LastPlayed, long Minutes);

    private static Dictionary<uint, PlayStats> ReadPlayStats(string userFolder)
    {
        var path = Path.Combine(userFolder, "config", "localconfig.vdf");
        if (!File.Exists(path))
        {
            return [];
        }

        var apps = Vdf.Load(path)["UserLocalConfigStore"]["Software"]["Valve"]["Steam"]["apps"];
        var result = new Dictionary<uint, PlayStats>();
        foreach (var (key, app) in apps.Children)
        {
            if (uint.TryParse(key, out var appId))
            {
                DateTime? lastPlayed = app["LastPlayed"].AsLong() is long seconds and > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                    : null;
                result[appId] = new PlayStats(lastPlayed, Math.Max(0, app["Playtime"].AsLong() ?? 0)); // minutes
            }
        }

        return result;
    }

    internal sealed class Collections
    {
        public HashSet<uint> Hidden { get; } = [];

        public HashSet<uint> Favorites { get; } = [];

        /// <summary>App id → names of the user's own collections it's in.</summary>
        public Dictionary<uint, List<string>> Named { get; } = [];
    }

    /// <summary>
    /// Steam keeps collections in a JSON file of [key, {value, is_deleted}] pairs; each value is itself JSON like
    /// {"name": "...", "added": [appids]}. Dynamic collections (built from filters) have no fixed list, so they're skipped.
    /// </summary>
    internal static Collections ReadCollections(string userFolder)
    {
        var result = new Collections();
        var path = Path.Combine(userFolder, "config", "cloudstorage", "cloud-storage-namespace-1.json");
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var pair in document.RootElement.EnumerateArray())
            {
                if (pair.GetArrayLength() < 2 || pair[0].GetString() is not { } key || !key.StartsWith("user-collections.", StringComparison.Ordinal))
                {
                    continue;
                }

                var entry = pair[1];
                if (entry.TryGetProperty("is_deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True
                    || !entry.TryGetProperty("value", out var valueText) || valueText.GetString() is not { } json)
                {
                    continue;
                }

                using var collection = JsonDocument.Parse(json);
                var root = collection.RootElement;
                if (!root.TryGetProperty("added", out var added) || added.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var ids = added.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Number).Select(a => a.GetUInt32()).ToList();
                var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                if (id == "hidden")
                {
                    result.Hidden.UnionWith(ids);
                }
                else if (id == "favorite")
                {
                    result.Favorites.UnionWith(ids);
                }
                else if (root.TryGetProperty("name", out var nameElement) && nameElement.GetString() is { Length: > 0 } name)
                {
                    foreach (var appId in ids)
                    {
                        (result.Named.TryGetValue(appId, out var names) ? names : result.Named[appId] = []).Add(name);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            Log.Error("Couldn't read Steam collections; hidden games, favorites and collections won't be used.", ex);
        }

        return result;
    }
}
