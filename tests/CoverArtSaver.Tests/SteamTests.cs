using System.Text;
using CoverArtSaver.Core;
using CoverArtSaver.Core.Steam;

namespace CoverArtSaver.Tests;

public class VdfTests
{
    [Fact]
    public void ParsesNestedKeysCaseInsensitively()
    {
        var root = Vdf.Parse("""
            "AppState"
            {
                "appid"     "620980"
                "name"      "Beat \"Saber\""   // comment
                "UserConfig" { "language" "english" }
            }
            """);
        Assert.Equal("620980", root["appstate"]["APPID"].Value);
        Assert.Equal("Beat \"Saber\"", root["AppState"]["name"].Value);
        Assert.Equal("english", root["AppState"]["UserConfig"]["language"].Value);
        Assert.Null(root["AppState"]["missing"]["deeper"].Value); // missing keys never throw
    }

    [Fact]
    public void ToleratesTruncatedFiles()
    {
        var root = Vdf.Parse("\"a\" { \"b\" \"1\" \"c\" {");
        Assert.Equal("1", root["a"]["b"].Value);
    }
}

public class SteamAppInfoTests
{
    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    public void ReadsCommonSectionOfWantedAppsOnly(int version)
    {
        var bytes = FakeSteam.AppInfo(version,
            (10, new() { ["name"] = "Counter-Strike", ["type"] = "game", ["steam_release_date"] = 973065600, ["genres"] = new Kv { ["0"] = "1" } }),
            (20, new() { ["name"] = "Some DLC", ["type"] = "DLC" }));

        var result = SteamAppInfo.ReadCommon(new MemoryStream(bytes), new HashSet<uint> { 10 });

        var app = Assert.Single(result);
        Assert.Equal(10u, app.Key);
        Assert.Equal("Counter-Strike", app.Value["name"].Value);
        Assert.Equal("973065600", app.Value["steam_release_date"].Value);
        Assert.Equal("1", app.Value["genres"]["0"].Value);
    }

    [Fact]
    public void RejectsUnknownFormats() =>
        Assert.Throws<InvalidDataException>(() => SteamAppInfo.ReadCommon(new MemoryStream(new byte[16]), new HashSet<uint>()));
}

public class SteamLibraryTests : IDisposable
{
    private readonly FakeSteam steam = new();

    public void Dispose() => steam.Dispose();

    [Fact]
    public void ReadsGamesWithDetailsArtAndUserData()
    {
        steam.AddGame(620980, "Beat Saber", installed: true, newLayout: true, lastPlayed: 1712964079,
            tags: [6650], descriptors: ["1"], genres: ["1"], year: 1558483200);
        steam.AddGame(10, "Counter-Strike");
        steam.AddApp(20, "Some DLC", type: "DLC");
        steam.AddCollection("hidden", "Hidden", 10);
        steam.AddCollection("favorite", "Favorites", 620980);
        steam.AddCollection("uc-abc", "Screensaver: Hide", 620980);
        steam.Write();

        var games = SteamLibrary.Read(steam.Folder, CoverShape.All).Library.Games.ToDictionary(g => g.Id);

        Assert.Equal(["steam:10", "steam:620980"], games.Keys.Order());  // the DLC is left out
        var beatSaber = games["steam:620980"];
        Assert.Equal("Beat Saber", beatSaber.Name);
        Assert.True(beatSaber.IsInstalled);
        Assert.True(beatSaber.Favorite);
        Assert.Equal(2019, beatSaber.ReleaseYear);
        Assert.Equal(new DateTime(2024, 4, 12, 23, 21, 19, DateTimeKind.Utc), beatSaber.LastActivity);
        Assert.Contains("Nudity", beatSaber.Tags);             // store tag id 6650, by name
        Assert.Contains("Screensaver: Hide", beatSaber.Tags);  // user collections become tags
        Assert.Equal(["Some Nudity or Sexual Content"], beatSaber.AgeRatings);
        Assert.Equal(["Action"], beatSaber.Genres);
        Assert.EndsWith("library_capsule.jpg", beatSaber.CoverPath);  // newer Steam's layout
        Assert.True(games["steam:10"].Hidden);
        Assert.False(games["steam:10"].IsInstalled);
        Assert.EndsWith(Path.Combine("10", "library_600x900.jpg"), games["steam:10"].CoverPath);
    }

    [Fact]
    public void PicksArtByShapeAndPrefersCustomArt()
    {
        steam.AddGame(10, "Counter-Strike");
        steam.AddGame(70, "Half-Life", newLayout: true);
        steam.AddCustomArt("10p.png");
        steam.Write();

        var vertical = SteamLibrary.Read(steam.Folder, CoverShape.Vertical).Library.Games.ToDictionary(g => g.Id);
        var horizontal = SteamLibrary.Read(steam.Folder, CoverShape.Horizontal).Library.Games.ToDictionary(g => g.Id);

        Assert.EndsWith(Path.Combine("grid", "10p.png"), vertical["steam:10"].CoverPath);
        Assert.EndsWith("header.jpg", horizontal["steam:10"].CoverPath);
        Assert.EndsWith("library_header.jpg", horizontal["steam:70"].CoverPath);
    }

    [Fact]
    public void ContentFilterCatchesSteamAdultGames()
    {
        steam.AddGame(1, "Adult Game", tags: [12095], descriptors: ["3"]);
        steam.AddGame(2, "Family Game", tags: [19]);
        steam.Write();

        var games = SteamLibrary.Read(steam.Folder, CoverShape.All).Library.Games;
        var result = new GameFilter(new FilterSettings()).Apply(games);

        Assert.Equal(["Family Game"], result.Included.Select(g => g.Name));
        Assert.Equal(1, result.ExcludedCounts[ExclusionReason.AdultContent]);
    }

    [Fact]
    public void ReadsPlaytimeAndReviewRating()
    {
        steam.AddGame(1, "Played", playtimeMinutes: 90, reviewScore: 9);
        steam.AddGame(2, "Never played", reviewScore: 6);
        steam.AddGame(3, "Unrated", lastPlayed: 1712964079);
        steam.Write();

        var games = SteamLibrary.Read(steam.Folder, CoverShape.All).Library.Games.ToDictionary(g => g.Name);

        Assert.Equal(90 * 60, games["Played"].PlaytimeSeconds);
        Assert.Equal(9, games["Played"].SteamReviewScore);
        Assert.Equal(0, games["Never played"].PlaytimeSeconds); // not in localconfig.vdf at all
        Assert.Equal(6, games["Never played"].SteamReviewScore);
        Assert.Equal(0, games["Unrated"].PlaytimeSeconds);
        Assert.Null(games["Unrated"].SteamReviewScore);
        Assert.NotNull(games["Unrated"].LastActivity);
    }

    [Fact]
    public void InstalledAppsWithoutDetailsAreStillShown()
    {
        steam.AddApp(99, null, installed: true, manifestName: "Old Game"); // not in appinfo.vdf
        steam.Write();

        var game = Assert.Single(SteamLibrary.Read(steam.Folder, CoverShape.All).Library.Games);
        Assert.Equal("Old Game", game.Name);
    }

    [Fact]
    public void SourcesReportMissingSteam()
    {
        var result = LibrarySources.Read(new SaverSettings { Source = LibrarySource.Steam, SteamFolderOverride = steam.Folder });
        Assert.Null(result.Data);
        Assert.Contains("Steam wasn't found", result.Problem);
    }
}

/// <summary>Nested KeyValues for building test appinfo.vdf files: values are string, int or Kv.</summary>
public sealed class Kv : Dictionary<string, object>;

/// <summary>A minimal Steam folder in a temp directory, with the files SteamLibrary reads.</summary>
public sealed class FakeSteam : IDisposable
{
    private const long SteamId64 = 76561198025002750; // userdata folder 64737022
    private readonly List<(uint Id, Kv? Common)> apps = [];
    private readonly List<(string Id, string Name, uint[] AppIds)> collections = [];

    public string Folder { get; } = Directory.CreateTempSubdirectory("steam").FullName;

    private string UserConfig => Path.Combine(Folder, "userdata", "64737022", "config");

    private readonly Dictionary<uint, long> lastPlayed = [];

    private readonly Dictionary<uint, long> playtime = [];

    public void AddGame(uint id, string name, bool installed = false, bool newLayout = false, long lastPlayed = 0,
        int[]? tags = null, string[]? descriptors = null, string[]? genres = null, long year = 0,
        long playtimeMinutes = 0, int reviewScore = 0)
    {
        var common = new Kv { ["name"] = name, ["type"] = "game" };
        if (reviewScore != 0) common["review_score"] = reviewScore;
        if (playtimeMinutes != 0) playtime[id] = playtimeMinutes;
        if (tags != null) common["store_tags"] = Indexed(tags.Select(t => (object)t));
        if (descriptors != null) common["content_descriptors"] = Indexed(descriptors);
        if (genres != null) common["genres"] = Indexed(genres);
        if (year != 0) common["steam_release_date"] = (int)year;
        AddApp(id, common, installed, name);

        var cache = Path.Combine(Folder, "appcache", "librarycache", id.ToString());
        if (newLayout)
        {
            WriteFile(Path.Combine(cache, "0123abcd", "library_capsule.jpg"));
            WriteFile(Path.Combine(cache, "4567ef01", "library_header.jpg"));
        }
        else
        {
            WriteFile(Path.Combine(cache, "library_600x900.jpg"));
            WriteFile(Path.Combine(cache, "header.jpg"));
        }

        if (lastPlayed != 0)
        {
            this.lastPlayed[id] = lastPlayed;
        }
    }

    public void AddApp(uint id, string? name, string type = "game", bool installed = false, string? manifestName = null) =>
        AddApp(id, name == null ? null : new Kv { ["name"] = name, ["type"] = type }, installed, manifestName ?? name);

    private void AddApp(uint id, Kv? common, bool installed, string? manifestName)
    {
        apps.Add((id, common));
        if (installed)
        {
            WriteFile(Path.Combine(Folder, "steamapps", $"appmanifest_{id}.acf"),
                $"\"AppState\"\n{{\n\t\"appid\"\t\"{id}\"\n\t\"name\"\t\"{manifestName}\"\n\t\"StateFlags\"\t\"4\"\n}}\n");
        }
    }

    public void AddCollection(string id, string name, params uint[] appIds) => collections.Add((id, name, appIds));

    public void AddCustomArt(string fileName) => WriteFile(Path.Combine(UserConfig, "grid", fileName));

    public void Write()
    {
        Directory.CreateDirectory(Path.Combine(Folder, "appcache"));
        File.WriteAllBytes(Path.Combine(Folder, "appcache", "appinfo.vdf"),
            AppInfo(29, [.. apps.Where(a => a.Common != null).Select(a => (a.Id, a.Common!))]));
        WriteFile(Path.Combine(Folder, "config", "loginusers.vdf"),
            $"\"users\"\n{{\n\t\"{SteamId64}\"\n\t{{\n\t\t\"PersonaName\"\t\"Tester\"\n\t\t\"MostRecent\"\t\"1\"\n\t}}\n}}\n");

        var played = string.Join("\n", lastPlayed.Keys.Union(playtime.Keys).Select(id =>
            $"\"{id}\" {{ \"LastPlayed\" \"{lastPlayed.GetValueOrDefault(id)}\" \"Playtime\" \"{playtime.GetValueOrDefault(id)}\" }}"));
        WriteFile(Path.Combine(UserConfig, "localconfig.vdf"),
            $"\"UserLocalConfigStore\" {{ \"Software\" {{ \"Valve\" {{ \"Steam\" {{ \"apps\" {{ {played} }} }} }} }} }}");

        var entries = collections.Select(c =>
        {
            var value = System.Text.Json.JsonSerializer.Serialize(new { id = c.Id, name = c.Name, added = c.AppIds, removed = Array.Empty<uint>() });
            return new object[] { "user-collections." + c.Id, new { key = "user-collections." + c.Id, timestamp = 1, value } };
        });
        WriteFile(Path.Combine(UserConfig, "cloudstorage", "cloud-storage-namespace-1.json"),
            System.Text.Json.JsonSerializer.Serialize(entries));
    }

    public void Dispose() => Directory.Delete(Folder, recursive: true);

    private static Kv Indexed(IEnumerable<object> values)
    {
        var kv = new Kv();
        foreach (var (value, i) in values.Select((v, i) => (v, i)))
        {
            kv[i.ToString()] = value;
        }

        return kv;
    }

    private static void WriteFile(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Builds an appinfo.vdf in format 28 (inline keys) or 29 (string table).</summary>
    public static byte[] AppInfo(int version, params (uint Id, Kv Common)[] entries)
    {
        var strings = new List<string>();
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        foreach (var (id, common) in entries)
        {
            var kv = new MemoryStream();
            var kw = new BinaryWriter(kv);
            WriteMap(kw, new Kv { ["appinfo"] = new Kv { ["appid"] = (int)id, ["common"] = common } }, version, strings);
            kw.Write((byte)8);

            w.Write(id);
            w.Write((uint)(60 + kv.Length));
            w.Write(new byte[60]); // state, last updated, token, hashes, change number
            w.Write(kv.ToArray());
        }

        w.Write(0u); // end marker

        var file = new MemoryStream();
        var fw = new BinaryWriter(file);
        fw.Write(version == 29 ? 0x07564429u : 0x07564428u);
        fw.Write(1u);
        if (version == 29)
        {
            fw.Write(16L + body.Length); // string table right after the entries
        }

        fw.Write(body.ToArray());
        if (version == 29)
        {
            fw.Write((uint)strings.Count);
            foreach (var s in strings)
            {
                fw.Write(Encoding.UTF8.GetBytes(s));
                fw.Write((byte)0);
            }
        }

        return file.ToArray();
    }

    private static void WriteMap(BinaryWriter w, Kv map, int version, List<string> strings)
    {
        foreach (var (key, value) in map)
        {
            w.Write(value switch { Kv => (byte)0, string => (byte)1, _ => (byte)2 });
            if (version == 29)
            {
                var index = strings.IndexOf(key);
                if (index < 0)
                {
                    strings.Add(key);
                    index = strings.Count - 1;
                }

                w.Write(index);
            }
            else
            {
                w.Write(Encoding.UTF8.GetBytes(key));
                w.Write((byte)0);
            }

            switch (value)
            {
                case Kv child:
                    WriteMap(w, child, version, strings);
                    w.Write((byte)8);
                    break;
                case string s:
                    w.Write(Encoding.UTF8.GetBytes(s));
                    w.Write((byte)0);
                    break;
                default:
                    w.Write(Convert.ToInt32(value));
                    break;
            }
        }
    }
}
