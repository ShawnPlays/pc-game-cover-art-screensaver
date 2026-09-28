using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using CoverArtSaver.Core;

namespace CoverArtSaver.Rendering;

/// <summary>
/// The coverflow itself: a WPF Viewport3D with one textured rectangle ("quad") per visible cover,
/// plus a second quad underneath for its reflection. Every frame we ask <see cref="CoverflowMath"/>
/// where each cover should be and update its transform.
/// </summary>
internal sealed class CoverflowView : Grid
{
    private readonly IReadOnlyList<GameEntry> games;
    private readonly SaverSettings settings;
    private readonly CoverflowMath math;
    private readonly Stopwatch clock;
    private readonly double startOffset;
    private readonly double timeOffset;
    private readonly CoverTextureCache textures;

    private readonly Viewport3D viewport = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly ModelVisual3D lights = new() { Content = new AmbientLight(Colors.White) };
    private readonly Dictionary<long, CoverSlot> slots = [];
    private readonly TextBlock title = new();
    private readonly TextBlock details = new();

    private long currentCenter = long.MinValue;
    private bool running;

    /// <param name="clock">Shared between monitors in Mirror mode so they stay in sync.</param>
    /// <param name="startOffset">Which cover to start on (lets Independent monitors differ).</param>
    /// <param name="timeOffset">Seconds this monitor runs ahead of the clock, so monitors taking turns slide at different moments.</param>
    public CoverflowView(IReadOnlyList<GameEntry> games, SaverSettings settings, Stopwatch clock, double startOffset, double timeOffset, bool isPreview)
    {
        this.games = games;
        this.settings = settings;
        this.clock = clock;
        this.startOffset = startOffset;
        this.timeOffset = timeOffset;

        Background = Brushes.Black;
        ClipToBounds = true;

        math = new CoverflowMath
        {
            SideCovers = settings.SideCovers,
            SideAngle = settings.SideAngle,
            SecondsPerCover = settings.SecondsPerCover,
            TransitionSeconds = settings.TransitionSeconds,
        };

        // Preview window is tiny; small textures load faster and look identical there.
        textures = new CoverTextureCache(isPreview ? 200 : settings.TextureHeight, settings.ShowReflection, Dispatcher);
        textures.TextureReady += OnTextureReady;

        // Camera sits slightly above the covers' mid-height and looks a touch downward.
        // Note: WPF's FieldOfView is HORIZONTAL, so wider screens show more side covers.
        viewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0.45, 3.1),
            LookDirection = new Vector3D(0, -0.06, -1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 60,
        };
        viewport.Children.Add(lights);
        Children.Add(viewport);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
        };
        title.Foreground = Brushes.White;
        title.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        title.FontWeight = FontWeights.SemiBold;
        title.TextAlignment = TextAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        details.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA4, 0xAC));
        details.FontFamily = title.FontFamily;
        details.TextAlignment = TextAlignment.Center;
        textPanel.Children.Add(title);
        textPanel.Children.Add(details);
        Children.Add(textPanel);

        SizeChanged += (_, _) =>
        {
            // Scale text with the screen so it looks the same on a 1080p TV and the tiny preview.
            title.FontSize = Math.Max(6, ActualHeight * 0.034);
            details.FontSize = Math.Max(5, ActualHeight * 0.022);
            textPanel.Margin = new Thickness(ActualWidth * 0.05, 0, ActualWidth * 0.05, ActualHeight * 0.05);
        };

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>A black view with a centred message (e.g. "no library found").</summary>
    public static Grid CreateMessageView(string message) => new()
    {
        Background = Brushes.Black,
        Children =
        {
            new TextBlock
            {
                Text = message,
                Foreground = Brushes.Gray,
                FontSize = 18,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40),
            },
        },
    };

    private void Start()
    {
        if (running || games.Count == 0)
        {
            return;
        }

        running = true;
        CompositionTarget.Rendering += OnFrame; // fires once per display refresh
    }

    private void Stop()
    {
        running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var position = startOffset + math.PositionAt(clock.Elapsed.TotalSeconds + timeOffset);
        var center = (long)Math.Round(position);

        // Keep one extra slot on each side so covers slide in from off-screen.
        var sideCount = settings.SideCovers + 1;
        var first = (long)Math.Floor(position) - sideCount;
        var last = (long)Math.Ceiling(position) + sideCount;

        var changed = false;
        foreach (var index in slots.Keys.Where(i => i < first || i > last).ToList())
        {
            viewport.Children.Remove(slots[index].Visual);
            slots.Remove(index);
            changed = true;
        }

        for (var i = first; i <= last; i++)
        {
            if (!slots.ContainsKey(i))
            {
                var game = games[CoverflowMath.WrapIndex(i, games.Count)];
                var slot = new CoverSlot(game, settings.ShowReflection);
                var texture = textures.Request(game);
                if (texture != null)
                {
                    slot.ApplyTexture(texture);
                }

                slots[i] = slot;
                changed = true;
            }
        }

        foreach (var (index, slot) in slots)
        {
            slot.ApplyPose(math.PoseFor(index - position));
        }

        if (changed || center != currentCenter)
        {
            SortForDepth(position);
        }

        if (center != currentCenter)
        {
            currentCenter = center;
            OnCenterChanged(center);
        }
    }

    /// <summary>
    /// WPF draws 3D children in collection order. Drawing far covers first (painter's algorithm)
    /// keeps the fading edge covers blending correctly with the ones in front of them.
    /// </summary>
    private void SortForDepth(double position)
    {
        viewport.Children.Clear();
        viewport.Children.Add(lights);
        foreach (var slot in slots.OrderByDescending(kv => Math.Abs(kv.Key - position)).Select(kv => kv.Value))
        {
            viewport.Children.Add(slot.Visual);
        }
    }

    private void OnCenterChanged(long center)
    {
        var game = games[CoverflowMath.WrapIndex(center, games.Count)];
        if (settings.ShowTitle)
        {
            title.Text = game.Name;
            details.Text = settings.ShowDetails ? DescribeGame(game) : "";
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4));
            title.BeginAnimation(OpacityProperty, fadeIn);
            details.BeginAnimation(OpacityProperty, fadeIn);
        }

        // Prefetch the covers that are about to slide in from the right.
        var keep = new HashSet<string>();
        for (var i = center - settings.SideCovers - 2; i <= center + settings.SideCovers + 8; i++)
        {
            var upcoming = games[CoverflowMath.WrapIndex(i, games.Count)];
            keep.Add(upcoming.Id);
            if (i > center + settings.SideCovers)
            {
                textures.Request(upcoming);
            }
        }

        textures.Trim(keep, capacity: Math.Max(48, keep.Count * 2));
    }

    private static string DescribeGame(GameEntry game)
    {
        var parts = new List<string>();
        if (game.Platforms.Count > 0)
        {
            parts.Add(string.Join(", ", game.Platforms.Take(2)));
        }

        if (game.ReleaseYear is int year)
        {
            parts.Add(year.ToString());
        }

        return string.Join("  ·  ", parts);
    }

    private void OnTextureReady(string gameId, CoverTexture texture)
    {
        foreach (var slot in slots.Values.Where(s => s.Game.Id == gameId))
        {
            slot.ApplyTexture(texture);
        }
    }
}
