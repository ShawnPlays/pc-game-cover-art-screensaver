using System.Text.RegularExpressions;

namespace CoverArtSaver.Core;

public enum ExclusionReason
{
    None,
    Hidden,
    NotInstalled,
    NotFavorite,
    HideTag,
    NoCover,
    AdultContent,
    MatureRating,
    WrongShape,
}

public sealed record FilterResult(IReadOnlyList<GameEntry> Included, IReadOnlyDictionary<ExclusionReason, int> ExcludedCounts)
{
    public int TotalExcluded => ExcludedCounts.Values.Sum();
}

/// <summary>Decides which games appear in the screensaver.</summary>
public sealed class GameFilter
{
    private readonly FilterSettings settings;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, double?> coverAspect;
    private readonly Regex? adultRegex;
    private readonly Regex? matureRegex;

    /// <param name="fileExists">Injected so tests don't need real files. Defaults to File.Exists.</param>
    /// <param name="coverAspect">
    /// Width ÷ height of a cover image, or null if unknown. Pass <see cref="CoverSizeCache.GetAspect"/> for big
    /// libraries; the default reads each image's header every time.
    /// </param>
    public GameFilter(FilterSettings settings, Func<string, bool>? fileExists = null, Func<string, double?>? coverAspect = null)
    {
        this.settings = settings;
        this.fileExists = fileExists ?? File.Exists;
        this.coverAspect = coverAspect ?? (path => ImageHeader.ReadSize(path)?.Aspect);
        adultRegex = settings.ExcludeAdultContent ? BuildWholeWordRegex(settings.AdultKeywords) : null;
        matureRegex = settings.ExcludeMatureRatings ? BuildWholeWordRegex(settings.MatureKeywords) : null;
    }

    public FilterResult Apply(IEnumerable<GameEntry> games)
    {
        var included = new List<GameEntry>();
        var counts = new Dictionary<ExclusionReason, int>();
        foreach (var game in games)
        {
            var reason = Evaluate(game);
            if (reason == ExclusionReason.None)
            {
                included.Add(game);
            }
            else
            {
                counts[reason] = counts.GetValueOrDefault(reason) + 1;
            }
        }

        return new FilterResult(included, counts);
    }

    /// <summary>Returns why a game is excluded, or <see cref="ExclusionReason.None"/> if it's shown.</summary>
    public ExclusionReason Evaluate(GameEntry game)
    {
        // Explicit user intent always wins, so these two tag checks come first.
        if (HasTag(game, settings.HideTag))
        {
            return ExclusionReason.HideTag;
        }

        var forceShow = HasTag(game, settings.ShowTag);

        if (game.Hidden && !settings.IncludeHidden)
        {
            return ExclusionReason.Hidden;
        }

        if (settings.InstalledOnly && !game.IsInstalled)
        {
            return ExclusionReason.NotInstalled;
        }

        if (settings.FavoritesOnly && !game.Favorite)
        {
            return ExclusionReason.NotFavorite;
        }

        if (settings.SkipGamesWithoutCover && (string.IsNullOrWhiteSpace(game.CoverPath) || !fileExists(game.CoverPath)))
        {
            return ExclusionReason.NoCover;
        }

        if (!forceShow)
        {
            if (adultRegex != null && game.AllTerms.Any(adultRegex.IsMatch))
            {
                return ExclusionReason.AdultContent;
            }

            if (matureRegex != null && game.AllTerms.Any(matureRegex.IsMatch))
            {
                return ExclusionReason.MatureRating;
            }
        }

        // Checked last because it's the only rule that reads the image file.
        if (settings.CoverShape != CoverShape.All && ShapeOf(game) != settings.CoverShape)
        {
            return ExclusionReason.WrongShape;
        }

        return ExclusionReason.None;
    }

    /// <summary>
    /// Games with no art (or art we can't measure) are drawn as a title card, which is vertical,
    /// so that's the shape they count as.
    /// </summary>
    private CoverShape ShapeOf(GameEntry game) =>
        !string.IsNullOrWhiteSpace(game.CoverPath) && coverAspect(game.CoverPath) is double aspect
            ? CoverShapes.Classify(aspect)
            : CoverShape.Vertical;

    /// <summary>
    /// Builds one case-insensitive regex like (?&lt;!\w)(?:Nudity|Sexual|ESRB\ AO)(?!\w).
    /// Look-arounds are used instead of \b so keywords ending in symbols (e.g. "R18+") still work.
    /// </summary>
    internal static Regex? BuildWholeWordRegex(IEnumerable<string> keywords)
    {
        var parts = keywords
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Regex.Escape)
            .ToList();

        return parts.Count == 0
            ? null
            : new Regex($@"(?<!\w)(?:{string.Join("|", parts)})(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static bool HasTag(GameEntry game, string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && game.Tags.Any(t => string.Equals(t.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase));
}
