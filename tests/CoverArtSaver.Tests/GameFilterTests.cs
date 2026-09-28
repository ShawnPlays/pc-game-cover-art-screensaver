using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class GameFilterTests
{
    private static GameEntry Game(string name, params string[] tags) => new()
    {
        Id = name,
        Name = name,
        CoverPath = $@"C:\covers\{name}.jpg",
        Tags = [.. tags],
    };

    private static GameFilter Filter(FilterSettings? settings = null) =>
        new(settings ?? new FilterSettings(), fileExists: _ => true);

    [Theory]
    [InlineData("Nudity")]
    [InlineData("Partial Nudity")]
    [InlineData("Sexual Content")]
    [InlineData("Some Nudity or Sexual Content")]
    [InlineData("Adult Only Sexual Content")]
    [InlineData("erotic")]
    [InlineData("NSFW")]
    [InlineData("ESRB AO")]
    public void AdultTermsAreExcludedByDefault(string term)
    {
        Assert.Equal(ExclusionReason.AdultContent, Filter().Evaluate(Game("x", term)));
    }

    [Theory]
    [InlineData("Asexual Protagonist")]  // whole-word matching: "Sexual" must not match inside "Asexual"
    [InlineData("Violence")]
    [InlineData("ESRB M")]
    [InlineData("Adventure")]
    public void OrdinaryTermsAreKept(string term)
    {
        Assert.Equal(ExclusionReason.None, Filter().Evaluate(Game("x", term)));
    }

    [Fact]
    public void AgeRatingsAreScannedToo()
    {
        var game = Game("x");
        game.AgeRatings = ["ESRB AO"];
        Assert.Equal(ExclusionReason.AdultContent, Filter().Evaluate(game));
    }

    [Fact]
    public void AdultFilterCanBeTurnedOff()
    {
        var settings = new FilterSettings { ExcludeAdultContent = false };
        Assert.Equal(ExclusionReason.None, Filter(settings).Evaluate(Game("x", "Nudity")));
    }

    [Fact]
    public void MatureFilterIsOptInAndSeparate()
    {
        var game = Game("x");
        game.AgeRatings = ["PEGI 18"];
        Assert.Equal(ExclusionReason.None, Filter().Evaluate(game));

        var strict = new FilterSettings { ExcludeMatureRatings = true };
        Assert.Equal(ExclusionReason.MatureRating, Filter(strict).Evaluate(game));
    }

    [Fact]
    public void KeywordsWithSymbolsMatch()
    {
        var settings = new FilterSettings { ExcludeMatureRatings = true, MatureKeywords = ["ACB R18+"] };
        var game = Game("x");
        game.AgeRatings = ["ACB R18+"];
        Assert.Equal(ExclusionReason.MatureRating, Filter(settings).Evaluate(game));
    }

    [Fact]
    public void ShowTagOverridesContentFilter()
    {
        Assert.Equal(ExclusionReason.None, Filter().Evaluate(Game("x", "Nudity", "screensaver: show")));
    }

    [Fact]
    public void HideTagAlwaysWins()
    {
        Assert.Equal(ExclusionReason.HideTag, Filter().Evaluate(Game("x", "Screensaver: Show", "Screensaver: Hide")));
    }

    [Fact]
    public void HiddenGamesExcludedUnlessIncluded()
    {
        var game = Game("x");
        game.Hidden = true;
        Assert.Equal(ExclusionReason.Hidden, Filter().Evaluate(game));
        Assert.Equal(ExclusionReason.None, Filter(new FilterSettings { IncludeHidden = true }).Evaluate(game));
    }

    [Fact]
    public void MissingCoversAreSkippedByDefault()
    {
        var filter = new GameFilter(new FilterSettings(), fileExists: _ => false);
        Assert.Equal(ExclusionReason.NoCover, filter.Evaluate(Game("x")));

        var keep = new GameFilter(new FilterSettings { SkipGamesWithoutCover = false }, fileExists: _ => false);
        Assert.Equal(ExclusionReason.None, keep.Evaluate(Game("x")));
    }

    [Fact]
    public void ApplyCountsReasons()
    {
        var result = Filter().Apply([Game("a"), Game("b", "Nudity"), Game("c", "Hentai")]);
        Assert.Single(result.Included);
        Assert.Equal(2, result.ExcludedCounts[ExclusionReason.AdultContent]);
    }
}
