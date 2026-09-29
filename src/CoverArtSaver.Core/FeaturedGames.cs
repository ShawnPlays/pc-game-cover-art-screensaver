namespace CoverArtSaver.Core;

/// <summary>The lowest review rating a game needs to get a large tile, named after Steam's rating tiers.</summary>
public enum RatingLimit
{
    /// <summary>Don't check ratings; unrated games qualify too.</summary>
    Any,
    Mixed,
    MostlyPositive,
    Positive,
    VeryPositive,
    OverwhelminglyPositive,
}

/// <summary>Which games get the mosaic's large tiles.</summary>
public enum FeaturedTileMode
{
    /// <summary>Games with little or no play time (and, optionally, a good enough rating).</summary>
    BarelyPlayed,
    /// <summary>Any game; large tiles pick games at random, and every game also appears as a normal tile.</summary>
    Random,
}

/// <summary>Mosaic option: some games are shown as large tiles spanning several cells.</summary>
public sealed class FeaturedTileSettings
{
    public bool Enabled { get; set; }

    public FeaturedTileMode Mode { get; set; } = FeaturedTileMode.BarelyPlayed;

    /// <summary>A large tile is this many cells wide and this many tall.</summary>
    public int Size { get; set; } = 3;

    /// <summary>How many large tiles are on screen at once (fewer if they don't fit).</summary>
    public int Count { get; set; } = 1;

    /// <summary>Games played for at most this many hours qualify; 0 means only games with no play time recorded.</summary>
    public int MaxPlaytimeHours { get; set; }

    public RatingLimit MinimumRating { get; set; } = RatingLimit.VeryPositive;

    public const int MinSize = 2, MaxSize = 6, MaxCount = 4, MaxHours = 50;

    internal void Sanitize()
    {
        Size = Math.Clamp(Size, MinSize, MaxSize);
        Count = Math.Clamp(Count, 1, MaxCount);
        MaxPlaytimeHours = Math.Clamp(MaxPlaytimeHours, 0, MaxHours);
    }
}

/// <summary>Decides which games get a large tile in the mosaic.</summary>
public static class FeaturedGames
{
    /// <summary>In <see cref="FeaturedTileMode.Random"/> mode every game qualifies; play time and rating aren't checked.</summary>
    public static bool Qualifies(GameEntry game, FeaturedTileSettings settings) =>
        settings.Mode == FeaturedTileMode.Random
        || (game.PlaytimeSeconds is long seconds
            && (settings.MaxPlaytimeHours == 0 ? seconds == 0 : seconds <= settings.MaxPlaytimeHours * 3600L)
            && MeetsRating(game, settings.MinimumRating));

    /// <summary>
    /// Steam games use Steam's own rating. Playnite games use the Community Score (or the Critic Score if there's
    /// none), with Steam's percentage thresholds: Overwhelmingly Positive 95+, (Very) Positive 80+, Mostly Positive 70+,
    /// Mixed 40+. Steam's "Positive" is 80%+ from fewer reviews, so a percentage can't tell it from Very Positive.
    /// </summary>
    public static bool MeetsRating(GameEntry game, RatingLimit limit)
    {
        if (limit == RatingLimit.Any)
        {
            return true;
        }

        if (game.SteamReviewScore is int steamScore)
        {
            return steamScore >= limit switch
            {
                RatingLimit.Mixed => 5,
                RatingLimit.MostlyPositive => 6,
                RatingLimit.Positive => 7,
                RatingLimit.VeryPositive => 8,
                _ => 9,
            };
        }

        return (game.CommunityScore ?? game.CriticScore) is int percent && percent >= limit switch
        {
            RatingLimit.Mixed => 40,
            RatingLimit.MostlyPositive => 70,
            RatingLimit.Positive or RatingLimit.VeryPositive => 80,
            _ => 95,
        };
    }
}
