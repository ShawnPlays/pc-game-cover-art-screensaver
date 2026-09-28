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
        var settings = new FilterSettings { AdultContent = ContentFilterMode.Off };
        Assert.Equal(ExclusionReason.None, Filter(settings).Evaluate(Game("x", "Nudity")));
    }

    [Fact]
    public void MatureFilterIsOptInAndSeparate()
    {
        var game = Game("x");
        game.AgeRatings = ["PEGI 18"];
        Assert.Equal(ExclusionReason.None, Filter().Evaluate(game));

        var strict = new FilterSettings { MatureRatings = ContentFilterMode.Hide };
        Assert.Equal(ExclusionReason.MatureRating, Filter(strict).Evaluate(game));
    }

    [Fact]
    public void KeywordsWithSymbolsMatch()
    {
        var settings = new FilterSettings { MatureRatings = ContentFilterMode.Hide, MatureKeywords = ["ACB R18+"] };
        var game = Game("x");
        game.AgeRatings = ["ACB R18+"];
        Assert.Equal(ExclusionReason.MatureRating, Filter(settings).Evaluate(game));
    }

    [Fact]
    public void AdultOnlyShowsJustAdultGames()
    {
        var filter = Filter(new FilterSettings { AdultContent = ContentFilterMode.Only });
        Assert.Equal(ExclusionReason.None, filter.Evaluate(Game("x", "Nudity")));
        Assert.Equal(ExclusionReason.NotSelectedContent, filter.Evaluate(Game("y", "Adventure")));
    }

    [Fact]
    public void MatureOnlyShowsJustMatureGames()
    {
        var filter = Filter(new FilterSettings { AdultContent = ContentFilterMode.Off, MatureRatings = ContentFilterMode.Only });
        var mature = Game("x");
        mature.AgeRatings = ["PEGI 18"];
        Assert.Equal(ExclusionReason.None, filter.Evaluate(mature));
        Assert.Equal(ExclusionReason.NotSelectedContent, filter.Evaluate(Game("y", "Nudity")));
    }

    [Fact]
    public void BothOnlyShowsGamesMatchingEither()
    {
        var filter = Filter(new FilterSettings { AdultContent = ContentFilterMode.Only, MatureRatings = ContentFilterMode.Only });
        var mature = Game("m");
        mature.AgeRatings = ["ESRB M"];
        Assert.Equal(ExclusionReason.None, filter.Evaluate(Game("a", "Nudity")));
        Assert.Equal(ExclusionReason.None, filter.Evaluate(mature));
        Assert.Equal(ExclusionReason.NotSelectedContent, filter.Evaluate(Game("c", "Adventure")));
    }

    [Fact]
    public void HideAndOnlyCombine()
    {
        // Mature games, but none with nudity or sexual content.
        var filter = Filter(new FilterSettings { AdultContent = ContentFilterMode.Hide, MatureRatings = ContentFilterMode.Only });
        var violent = Game("v");
        violent.AgeRatings = ["ESRB M"];
        var explicitGame = Game("e", "Nudity");
        explicitGame.AgeRatings = ["ESRB M"];
        Assert.Equal(ExclusionReason.None, filter.Evaluate(violent));
        Assert.Equal(ExclusionReason.AdultContent, filter.Evaluate(explicitGame));
        Assert.Equal(ExclusionReason.NotSelectedContent, filter.Evaluate(Game("c", "Adventure")));
    }

    [Fact]
    public void ShowTagOverridesOnlyFilter()
    {
        var filter = Filter(new FilterSettings { AdultContent = ContentFilterMode.Only });
        Assert.Equal(ExclusionReason.None, filter.Evaluate(Game("x", "Adventure", "Screensaver: Show")));
    }

    [Theory]
    [InlineData("""{"Filter":{"ExcludeAdultContent":false,"ExcludeMatureRatings":true}}""", ContentFilterMode.Off, ContentFilterMode.Hide)]
    [InlineData("""{"Filter":{"ExcludeAdultContent":true}}""", ContentFilterMode.Hide, ContentFilterMode.Off)]
    [InlineData("""{"Filter":{"AdultContent":"Only","MatureRatings":"Only"}}""", ContentFilterMode.Only, ContentFilterMode.Only)]
    public void ContentModesLoadFromOldAndNewSettingsFiles(string json, ContentFilterMode adult, ContentFilterMode mature)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var filter = SettingsStore.Load(path).Filter;
            Assert.Equal(adult, filter.AdultContent);
            Assert.Equal(mature, filter.MatureRatings);
        }
        finally
        {
            File.Delete(path);
        }
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
