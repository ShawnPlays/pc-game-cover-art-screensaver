using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class FeaturedGamesTests
{
    private static GameEntry Game(long? playtimeHours = 0, int? steam = null, int? community = null, int? critic = null) => new()
    {
        Name = "Game",
        PlaytimeSeconds = playtimeHours * 3600,
        SteamReviewScore = steam,
        CommunityScore = community,
        CriticScore = critic,
    };

    private static FeaturedTileSettings Settings(int hours = 0, RatingLimit rating = RatingLimit.Any) =>
        new() { Enabled = true, MaxPlaytimeHours = hours, MinimumRating = rating };

    [Fact]
    public void ZeroHoursMeansNeverPlayed()
    {
        Assert.True(FeaturedGames.Qualifies(Game(0), Settings(0)));
        Assert.False(FeaturedGames.Qualifies(new GameEntry { PlaytimeSeconds = 60 }, Settings(0)));
    }

    [Fact]
    public void PlaytimeLimitIsInclusive()
    {
        Assert.True(FeaturedGames.Qualifies(Game(5), Settings(5)));
        Assert.False(FeaturedGames.Qualifies(new GameEntry { PlaytimeSeconds = 5 * 3600 + 1 }, Settings(5)));
    }

    [Fact]
    public void RandomModeQualifiesEveryGame()
    {
        var settings = Settings(0, RatingLimit.OverwhelminglyPositive);
        settings.Mode = FeaturedTileMode.Random;
        Assert.True(FeaturedGames.Qualifies(Game(null), settings));              // play time unknown
        Assert.True(FeaturedGames.Qualifies(Game(500, steam: 2), settings));     // played a lot, badly rated
    }

    [Fact]
    public void GamesAddedByHandCanBeLeftOut()
    {
        var manual = new GameEntry { Name = "Emulated", PlaytimeSeconds = 0, AddedManually = true };
        var settings = Settings();
        Assert.True(settings.SkipManuallyAdded); // on by default
        Assert.False(FeaturedGames.Qualifies(manual, settings));

        settings.SkipManuallyAdded = false;
        Assert.True(FeaturedGames.Qualifies(manual, settings));
    }

    [Theory]
    [InlineData("Humble", false)]
    [InlineData("ITCH.IO", false)] // names match ignoring case
    [InlineData("Steam", true)]
    [InlineData(null, true)]       // export from an older add-on: nothing to go on, so it counts
    public void GamesFromChosenLibrariesAreLeftOut(string? library, bool qualifies) =>
        Assert.Equal(qualifies, FeaturedGames.Qualifies(new GameEntry { PlaytimeSeconds = 0, Library = library }, Settings()));

    [Theory]
    [InlineData("Steam")]
    [InlineData("Epic")]
    [InlineData("GOG")]
    [InlineData("Xbox")]
    [InlineData("PlayStation")]
    [InlineData("EA app")]
    public void StoresThatReportPlaytimeAreNotLeftOutByDefault(string library) =>
        Assert.DoesNotContain(library, FeaturedTileSettings.DefaultSkipLibraries);

    [Theory]
    [InlineData("Humble Keys")]
    [InlineData("Amazon Games")]
    [InlineData("Battle.net")]
    [InlineData("Ubisoft Connect")]
    public void StoresThatDontReportPlaytimeAreLeftOutByDefault(string library) =>
        Assert.False(FeaturedGames.Qualifies(new GameEntry { PlaytimeSeconds = 0, Library = library }, Settings()));

    [Fact]
    public void RandomModeIgnoresTheLeaveOutOptions()
    {
        var settings = Settings();
        settings.Mode = FeaturedTileMode.Random;
        Assert.True(FeaturedGames.Qualifies(new GameEntry { AddedManually = true, Library = "Humble" }, settings));
    }

    [Fact]
    public void UnknownPlaytimeNeverQualifies() =>
        Assert.False(FeaturedGames.Qualifies(Game(null), Settings(50)));

    [Theory]
    [InlineData(9, RatingLimit.OverwhelminglyPositive, true)]
    [InlineData(8, RatingLimit.OverwhelminglyPositive, false)]
    [InlineData(8, RatingLimit.VeryPositive, true)]
    [InlineData(7, RatingLimit.VeryPositive, false)]  // "Positive" (80%+ but few reviews)
    [InlineData(7, RatingLimit.Positive, true)]
    [InlineData(6, RatingLimit.MostlyPositive, true)]
    [InlineData(5, RatingLimit.MostlyPositive, false)]
    [InlineData(5, RatingLimit.Mixed, true)]
    [InlineData(4, RatingLimit.Mixed, false)]
    public void SteamGamesUseSteamsRating(int score, RatingLimit limit, bool expected) =>
        Assert.Equal(expected, FeaturedGames.MeetsRating(Game(steam: score, community: 100), limit));

    [Theory]
    [InlineData(95, RatingLimit.OverwhelminglyPositive, true)]
    [InlineData(94, RatingLimit.OverwhelminglyPositive, false)]
    [InlineData(80, RatingLimit.VeryPositive, true)]
    [InlineData(80, RatingLimit.Positive, true)]
    [InlineData(79, RatingLimit.Positive, false)]
    [InlineData(70, RatingLimit.MostlyPositive, true)]
    [InlineData(40, RatingLimit.Mixed, true)]
    [InlineData(39, RatingLimit.Mixed, false)]
    public void PlayniteGamesUseScoreThresholds(int score, RatingLimit limit, bool expected) =>
        Assert.Equal(expected, FeaturedGames.MeetsRating(Game(community: score), limit));

    [Fact]
    public void CriticScoreIsTheFallback()
    {
        Assert.True(FeaturedGames.MeetsRating(Game(critic: 90), RatingLimit.VeryPositive));
        Assert.False(FeaturedGames.MeetsRating(Game(community: 60, critic: 90), RatingLimit.VeryPositive)); // community wins
    }

    [Fact]
    public void UnratedGamesOnlyQualifyWithAnyRating()
    {
        Assert.False(FeaturedGames.MeetsRating(Game(), RatingLimit.Mixed));
        Assert.True(FeaturedGames.MeetsRating(Game(), RatingLimit.Any));
    }

    [Fact]
    public void ReadsPlayniteExportFields()
    {
        var data = LibraryData.Parse("""
            { "SchemaVersion": 1, "Games": [
                { "Id": "a", "Name": "New", "PlaytimeSeconds": 7200, "CommunityScore": 88, "CriticScore": null,
                  "Library": "itch.io", "AddedManually": false },
                { "Id": "b", "Name": "From an older add-on" },
                { "Id": "c", "Name": "Added by hand", "Library": null, "AddedManually": true } ] }
            """);
        Assert.Equal(7200, data.Games[0].PlaytimeSeconds);
        Assert.Equal(88, data.Games[0].CommunityScore);
        Assert.Equal("itch.io", data.Games[0].Library);
        Assert.Null(data.Games[1].PlaytimeSeconds); // unknown, so never gets a large tile
        Assert.False(data.Games[1].AddedManually);  // older add-ons didn't say, so don't assume
        Assert.True(data.Games[2].AddedManually);
    }

    [Fact]
    public void LargeTilesAreOnByDefaultButASavedChoiceIsKept()
    {
        Assert.True(new SaverSettings().MosaicFeatured.Enabled);

        var off = System.Text.Json.JsonSerializer.Deserialize<SaverSettings>(
            """{ "MosaicFeatured": { "Enabled": false } }""")!.Sanitize();
        Assert.False(off.MosaicFeatured.Enabled);
    }

    [Fact]
    public void OlderSettingsFilesKeepBarelyPlayed()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<SaverSettings>(
            """{ "MosaicFeatured": { "Enabled": true, "Size": 3 } }""")!.Sanitize();
        Assert.Equal(FeaturedTileMode.BarelyPlayed, settings.MosaicFeatured.Mode);
        Assert.True(settings.MosaicFeatured.SkipManuallyAdded);
        Assert.Equal(FeaturedTileSettings.DefaultSkipLibraries, settings.MosaicFeatured.SkipLibraries);

        var nullList = System.Text.Json.JsonSerializer.Deserialize<SaverSettings>(
            """{ "MosaicFeatured": { "SkipLibraries": null } }""")!.Sanitize();
        Assert.Empty(nullList.MosaicFeatured.SkipLibraries);
    }

    [Fact]
    public void SettingsAreClampedAndSurviveARoundTrip()
    {
        var settings = new SaverSettings { MosaicFeatured = { Enabled = true, Size = 20, Count = 0, MaxPlaytimeHours = -3 } }.Sanitize();
        Assert.Equal((FeaturedTileSettings.MaxSize, 1, 0), (settings.MosaicFeatured.Size, settings.MosaicFeatured.Count, settings.MosaicFeatured.MaxPlaytimeHours));

        settings.MosaicFeatured.MinimumRating = RatingLimit.MostlyPositive;
        settings.MosaicFeatured.Mode = FeaturedTileMode.Random;
        settings.MosaicFeatured.SkipManuallyAdded = false;
        settings.MosaicFeatured.SkipLibraries = ["Amazon Games"];
        var copy = settings.Clone();
        Assert.False(copy.MosaicFeatured.SkipManuallyAdded);
        Assert.Equal(["Amazon Games"], copy.MosaicFeatured.SkipLibraries);
        Assert.True(copy.MosaicFeatured.Enabled);
        Assert.Equal(FeaturedTileMode.Random, copy.MosaicFeatured.Mode);
        Assert.Equal(RatingLimit.MostlyPositive, copy.MosaicFeatured.MinimumRating);
    }
}
