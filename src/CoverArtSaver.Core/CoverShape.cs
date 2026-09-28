namespace CoverArtSaver.Core;

/// <summary>Which cover art to use, by shape. <see cref="All"/> means no shape filter.</summary>
public enum CoverShape
{
    All,
    Vertical,
    Square,
    Horizontal,
}

public static class CoverShapes
{
    /// <summary>Covers within 10% of 1:1 count as square; anything taller is vertical, anything wider horizontal.</summary>
    public const double SquareMin = 0.9;

    public const double SquareMax = 1 / 0.9;

    /// <param name="aspect">Width ÷ height.</param>
    public static CoverShape Classify(double aspect) =>
        aspect < SquareMin ? CoverShape.Vertical
        : aspect > SquareMax ? CoverShape.Horizontal
        : CoverShape.Square;

    /// <summary>Mosaic tile proportions (width ÷ height) that suit covers of the chosen shape.</summary>
    public static double TileAspect(CoverShape shape) => shape switch
    {
        CoverShape.Square => 1.0,
        CoverShape.Horizontal => 630.0 / 500, // itch.io's cover size, by far the most common landscape cover in Playnite
        _ => MosaicLayout.TileAspect,         // box art (also used for "All", where most covers are box art)
    };
}
