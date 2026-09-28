using System.Text.Json;

namespace CoverArtSaver.Core;

/// <summary>Mirror of the exporter's ExportedLibrary (src/CoverArtExporter/ExportModels.cs).</summary>
public sealed class LibraryData
{
    public const int SupportedSchemaVersion = 1;

    public int SchemaVersion { get; set; }
    public DateTime ExportedAt { get; set; }
    public string? ExporterVersion { get; set; }
    public List<GameEntry> Games { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LibraryData Parse(string json)
    {
        var data = JsonSerializer.Deserialize<LibraryData>(json, JsonOptions)
                   ?? throw new InvalidDataException("Library file is empty.");
        if (data.SchemaVersion > SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Library file uses schema v{data.SchemaVersion}; this screensaver understands up to v{SupportedSchemaVersion}. Please update the screensaver.");
        }

        data.Games ??= [];
        foreach (var g in data.Games)
        {
            g.Normalize();
        }

        return data;
    }

    public static LibraryData Load(string path) => Parse(File.ReadAllText(path));
}

public sealed class GameEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? SortingName { get; set; }
    public string? CoverPath { get; set; }
    public bool Hidden { get; set; }
    public bool Favorite { get; set; }
    public bool IsInstalled { get; set; }
    public DateTime? Added { get; set; }
    public DateTime? LastActivity { get; set; }
    public int? ReleaseYear { get; set; }
    public string? Source { get; set; }
    public List<string> Platforms { get; set; } = [];
    public List<string> Genres { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<string> Features { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public List<string> AgeRatings { get; set; } = [];

    /// <summary>Every descriptive term attached to the game; what the content filter scans.</summary>
    public IEnumerable<string> AllTerms => Tags.Concat(Genres).Concat(Features).Concat(Categories).Concat(AgeRatings);

    /// <summary>Guards against nulls in hand-edited or older JSON.</summary>
    internal void Normalize()
    {
        Name ??= "";
        Platforms ??= [];
        Genres ??= [];
        Tags ??= [];
        Features ??= [];
        Categories ??= [];
        AgeRatings ??= [];
    }
}
