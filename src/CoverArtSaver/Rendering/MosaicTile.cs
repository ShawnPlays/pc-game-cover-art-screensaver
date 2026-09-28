using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CoverArtSaver.Core;

namespace CoverArtSaver.Rendering;

/// <summary>
/// One cell of the mosaic. A flip squeezes the tile to a sliver (as if turning edge-on), swaps the cover,
/// and opens it back up, darkening as it turns away so it reads as a 3D card flip.
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
        var generation = ++flipGeneration;
        var half = TimeSpan.FromSeconds(seconds / 2);

        var close = new DoubleAnimation(0, half) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
        close.Completed += (_, _) =>
        {
            if (generation != flipGeneration)
            {
                return; // a newer flip took over this tile
            }

            midFlip = false;
            SetImage(incoming);
            turn.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, 1, half) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            shade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(TurnedAwayShade, 0, half));
        };

        turn.BeginAnimation(ScaleTransform.ScaleXProperty, close);
        shade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(TurnedAwayShade, half));
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
