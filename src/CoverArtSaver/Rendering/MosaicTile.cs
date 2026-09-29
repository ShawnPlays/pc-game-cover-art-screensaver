using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CoverArtSaver.Core;

namespace CoverArtSaver.Rendering;

/// <summary>
/// One tile of the mosaic: a single cell, or a square of cells for a large tile. A flip squeezes the tile to a
/// sliver (as if turning edge-on), swaps the cover, and opens it back up, darkening as it turns away so it reads
/// as a 3D card flip.
/// </summary>
internal sealed class MosaicTile
{
    private const double TurnedAwayShade = 0.65;
    private static readonly Brush Placeholder = CreatePlaceholder();

    private readonly Image image = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly Rectangle shade = new() { Fill = Brushes.Black, Opacity = 0 };
    private readonly ScaleTransform turn = new();
    private ImageSource? incoming;
    private bool midFlip;
    private int flipGeneration;

    public Border Element { get; }

    public GameEntry? Game { get; private set; }

    /// <summary>Where on the grid this tile sits, and how many cells it spans.</summary>
    public MosaicPiece Piece { get; set; }

    public MosaicTile()
    {
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.HorizontalAlignment = HorizontalAlignment.Center;
        image.VerticalAlignment = VerticalAlignment.Center;

        Element = new Border
        {
            Background = Placeholder,
            ClipToBounds = true, // "uniform to fill" art overhangs the tile; trim it
            RenderTransform = turn,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Child = new Grid { Children = { image, shade } },
        };
    }

    /// <summary>Shows a game immediately (no flip). Null texture = placeholder until <see cref="ApplyTexture"/>.</summary>
    public void Show(GameEntry game, CoverTexture? texture)
    {
        Game = game;
        midFlip = false;
        SetImage(null);
        if (texture != null)
        {
            ApplyTexture(texture);
        }
    }

    /// <summary>Called when the current game's cover has finished loading.</summary>
    public void ApplyTexture(CoverTexture texture)
    {
        if (midFlip)
        {
            incoming = texture.Front; // swapped in when the tile is edge-on
            return;
        }

        if (image.Source == null)
        {
            image.Source = texture.Front;
            image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)));
        }
        else
        {
            SetImage(texture.Front);
        }
    }

    public void FlipTo(GameEntry game, CoverTexture? texture, double seconds)
    {
        Game = game;
        midFlip = true;
        incoming = texture?.Front;
        var half = TimeSpan.FromSeconds(seconds / 2);
        TurnEdgeOn(half, () =>
        {
            midFlip = false;
            SetImage(incoming);
            turn.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, 1, half) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            shade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(TurnedAwayShade, 0, half));
        });
    }

    /// <summary>
    /// For a tile taking over space other tiles had: starts edge-on and opens up halfway through a flip,
    /// just as those tiles finish turning away.
    /// </summary>
    public void TurnIn(GameEntry game, CoverTexture? texture, double seconds)
    {
        turn.ScaleX = 0;
        shade.Opacity = TurnedAwayShade;
        FlipTo(game, texture, seconds); // the first half turns from edge-on to edge-on, i.e. waits
    }

    /// <summary>The first half of a flip, then <paramref name="gone"/> (to remove the tile) instead of the second.</summary>
    public void TurnAway(double seconds, Action gone)
    {
        midFlip = false;
        TurnEdgeOn(TimeSpan.FromSeconds(seconds / 2), gone);
    }

    private void TurnEdgeOn(TimeSpan duration, Action then)
    {
        var generation = ++flipGeneration;
        var close = new DoubleAnimation(0, duration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
        close.Completed += (_, _) =>
        {
            if (generation == flipGeneration) // otherwise a newer flip took over this tile
            {
                then();
            }
        };

        turn.BeginAnimation(ScaleTransform.ScaleXProperty, close);
        shade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(TurnedAwayShade, duration));
    }

    private void SetImage(ImageSource? source)
    {
        image.BeginAnimation(UIElement.OpacityProperty, null); // drop any fade-in so the local value applies
        image.Source = source;
        image.Opacity = source == null ? 0 : 1;
    }

    private static Brush CreatePlaceholder()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x22, 0x24, 0x28));
        brush.Freeze();
        return brush;
    }
}
