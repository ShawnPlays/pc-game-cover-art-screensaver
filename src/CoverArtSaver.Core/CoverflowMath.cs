namespace CoverArtSaver.Core;

/// <summary>Where a single cover sits in 3D space. Units are "cover heights" (a cover is 1 unit tall).</summary>
public readonly record struct CoverPose(double X, double Z, double AngleDegrees, double Opacity);

/// <summary>
/// The whole coverflow effect boils down to two small functions:
///  1. <see cref="PositionAt"/>: which cover is centred at time t (a fractional number while sliding).
///  2. <see cref="PoseFor"/>: given a cover's distance from the centre, where it sits and how it's turned.
/// Both are pure functions, which keeps the renderer simple and makes them unit-testable.
/// </summary>
public sealed class CoverflowMath
{
    /// <summary>Gap between the centred cover and the first side cover.</summary>
    public double SideOffset { get; init; } = 0.62;

    /// <summary>Gap between neighbouring side covers (they overlap heavily, like iTunes).</summary>
    public double SideSpacing { get; init; } = 0.24;

    /// <summary>How far side covers are pushed back from the centre cover.</summary>
    public double SideDepth { get; init; } = 0.55;

    public double SideAngle { get; init; } = 70;

    public int SideCovers { get; init; } = 6;

    public double SecondsPerCover { get; init; } = 4;

    public double TransitionSeconds { get; init; } = 0.7;

    /// <summary>
    /// Holds on a cover for (SecondsPerCover - TransitionSeconds), then eases to the next one.
    /// Because it's a pure function of elapsed time, two monitors started together stay in lockstep.
    /// </summary>
    public double PositionAt(double elapsedSeconds)
    {
        var period = Math.Max(SecondsPerCover, 0.1);
        var transition = Math.Clamp(TransitionSeconds, 0.01, period);
        var step = Math.Floor(elapsedSeconds / period);
        var intoStep = elapsedSeconds - step * period;
        var hold = period - transition;
        if (intoStep <= hold)
        {
            return step;
        }

        return step + EaseInOutCubic((intoStep - hold) / transition);
    }

    public CoverPose PoseFor(double offsetFromCenter)
    {
        var distance = Math.Abs(offsetFromCenter);
        var side = Math.Sign(offsetFromCenter);
        double x, z, angle;
        if (distance < 1)
        {
            // Moving between the centre slot and the first side slot: interpolate everything.
            x = side * distance * SideOffset;
            z = -distance * SideDepth;
            angle = -side * distance * SideAngle;
        }
        else
        {
            x = side * (SideOffset + (distance - 1) * SideSpacing);
            z = -SideDepth;
            angle = -side * SideAngle;
        }

        // Fade the outermost cover in/out so covers never pop into view.
        var opacity = Math.Clamp(SideCovers + 1 - distance, 0, 1);
        return new CoverPose(x, z, angle, opacity);
    }

    /// <summary>Wraps a (possibly negative) virtual slot index onto the game list.</summary>
    public static int WrapIndex(long virtualIndex, int count) =>
        count <= 0 ? 0 : (int)(((virtualIndex % count) + count) % count);

    public static double EaseInOutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
}
