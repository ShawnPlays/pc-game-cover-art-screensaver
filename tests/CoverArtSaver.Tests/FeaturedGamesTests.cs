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
                { "Id": "a", "Name": "New", "PlaytimeSeconds": 7200, "CommunityScore": 88, "CriticScore": null },
                { "Id": "b", "Name": "From an older add-on" } ] }
            """);
        Assert.Equal(7200, data.Games[0].PlaytimeSeconds);
        Assert.Equal(88, data.Games[0].CommunityScore);
        Assert.Null(data.Games[1].PlaytimeSeconds); // unknown, so never gets a large tile
    }

    [Fact]
    public void SettingsAreClampedAndSurviveARoundTrip()
    {
        var settings = new SaverSettings { MosaicFeatured = { Enabled = true, Size = 20, Count = 0, MaxPlaytimeHours = -3 } }.Sanitize();
        Assert.Equal((FeaturedTileSettings.MaxSize, 1, 0), (settings.MosaicFeatured.Size, settings.MosaicFeatured.Count, settings.MosaicFeatured.MaxPlaytimeHours));

        settings.MosaicFeatured.MinimumRating = RatingLimit.MostlyPositive;
        var copy = settings.Clone();
        Assert.True(copy.MosaicFeatured.Enabled);
        Assert.Equal(RatingLimit.MostlyPositive, copy.MosaicFeatured.MinimumRating);
    }
}
