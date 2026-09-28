using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CoverArtSaver.Core;

namespace CoverArtSaver.Rendering;

/// <summary>
/// The "album art" style: the screen is tiled with covers and every few seconds one tile flips over to
/// show another game, like iTunes' old Album Artwork screensaver. <see cref="MosaicPlanner"/> decides
/// which tile flips to which game; this class draws it.
/// </summary>
internal sealed class MosaicView : Grid
{
    private const double FlipSeconds = 0.8;

    /// <summary>Flips planned ahead of time so their covers are loaded before they're needed.</summary>
    private const int Lookahead = 8;

    private readonly IReadOnlyList<GameEntry> games;
    private readonly SaverSettings settings;
    private readonly Stopwatch clock;
    private readonly int seed;
    private readonly int startScreen;
    private readonly MonitorTurn turn;
    private readonly bool isPreview;

    private readonly Canvas canvas = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly DispatcherTimer timer;
    private readonly List<MosaicTile> tiles = [];
    private readonly Queue<MosaicFlip> upcoming = new();

    private MosaicGrid layout;
    private MosaicPlanner? planner;
    private CoverTextureCache? textures;
    private long stepsSeen; // steps of the shared flip schedule looked at so far, ours or another monitor's

    /// <param name="clock">Shared between monitors in Mirror mode so they flip in sync.</param>
    /// <param name="seed">Same seed on two monitors = same flips.</param>
    /// <param name="startScreen">Start this many screens' worth into the game list (lets Independent monitors differ).</param>
    /// <param name="turn">Which flips are this monitor's when monitors take turns.</param>
    public MosaicView(IReadOnlyList<GameEntry> games, SaverSettings settings, Stopwatch clock, int seed, int startScreen, MonitorTurn turn, bool isPreview)
    {
        this.games = games;
        this.settings = settings;
        this.clock = clock;
        this.seed = seed;
        this.startScreen = startScreen;
        this.turn = turn;
        this.isPreview = isPreview;

        Background = Brushes.Black;
        ClipToBounds = true;
        Children.Add(canvas);

        // The flips themselves are WPF animations; this timer only decides when to start the next one.
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => OnTick();

        SizeChanged += (_, _) => ArrangeTiles();
        Loaded += (_, _) => timer.Start();
        Unloaded += (_, _) => timer.Stop();
    }

    private long FlipsDue() => (long)(clock.Elapsed.TotalSeconds / settings.MosaicFlipSeconds);

    private void ArrangeTiles()
    {
        if (games.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var fitted = MosaicLayout.Fit(settings.MosaicColumns, ActualWidth, ActualHeight, CoverShapes.TileAspect(settings.Filter.CoverShape));
        if (planner == null || fitted.Columns != layout.Columns || fitted.Rows != layout.Rows)
        {
            layout = fitted;
            BuildTiles();
        }

        layout = fitted;
        for (var i = 0; i < tiles.Count; i++)
        {
            // Round each edge (not each size) so neighbouring tiles meet exactly, with no hairline gaps.
            int column = i % layout.Columns, row = i / layout.Columns;
            double left = Math.Round(column * layout.TileWidth), right = Math.Round((column + 1) * layout.TileWidth);
            double top = Math.Round(row * layout.TileHeight), bottom = Math.Round((row + 1) * layout.TileHeight);
            var element = tiles[i].Element;
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            element.Width = right - left;
            element.Height = bottom - top;
        }
    }

    private void BuildTiles()
    {
        // Decode covers at about the size they're drawn: a 10-column wall doesn't need 600 px textures.
        var tilePixels = layout.TileHeight * VisualTreeHelper.GetDpi(this).DpiScaleY;
        var maxHeight = isPreview ? 200 : settings.TextureHeight;
        var decodeHeight = (int)Math.Clamp(Math.Ceiling(tilePixels / 32) * 32, 64, maxHeight);

        if (textures != null)
        {
            textures.TextureReady -= OnTextureReady;
        }

        textures = new CoverTextureCache(decodeHeight, makeReflections: false, Dispatcher);
        textures.TextureReady += OnTextureReady;

        // Leave a tile alone while it (or a neighbour flip just before it) is still turning.
        var avoidRecent = (int)Math.Ceiling(FlipSeconds / settings.MosaicFlipSeconds) + 2;
        planner = new MosaicPlanner(layout.TileCount, games.Count, seed, startScreen * layout.TileCount, avoidRecent);

        canvas.Children.Clear();
        tiles.Clear();
        upcoming.Clear();
        foreach (var gameIndex in planner.Tiles)
        {
            var game = games[gameIndex];
            var tile = new MosaicTile();
            tile.Show(game, textures.Request(game));
            tiles.Add(tile);
            canvas.Children.Add(tile.Element);
        }

        PlanAhead();

        // Counting flips from time zero (not from now) keeps mirrored monitors in lockstep even if one lays out a bit later.
        stepsSeen = 0;
    }

    private void OnTick()
    {
        if (planner == null)
        {
            return;
        }

        var due = FlipsDue();
        if (due - stepsSeen > Lookahead * turn.Count)
        {
            // We fell far behind (the window was just resized, or the PC was busy): don't flip half the wall at once.
            stepsSeen = due - 1;
        }

        while (stepsSeen < due)
        {
            if (turn.Owns(stepsSeen++))
            {
                FlipNext();
            }
        }
    }

    private void FlipNext()
    {
        if (!upcoming.TryDequeue(out var flip))
        {
            return; // single-game library: nothing to flip to
        }

        PlanAhead();
        var game = games[flip.GameIndex];
        tiles[flip.Tile].FlipTo(game, textures!.Request(game), FlipSeconds);

        var keep = tiles.Select(t => t.Game!.Id).Concat(upcoming.Select(f => games[f.GameIndex].Id)).ToHashSet();
        textures.Trim(keep, capacity: tiles.Count + Lookahead + 16);
    }

    private void PlanAhead()
    {
        while (upcoming.Count < Lookahead && planner!.Next() is MosaicFlip flip)
        {
            upcoming.Enqueue(flip);
            textures!.Request(games[flip.GameIndex]); // start loading now so it's ready when the tile turns
        }
    }

    private void OnTextureReady(string gameId, CoverTexture texture)
    {
        foreach (var tile in tiles.Where(t => t.Game?.Id == gameId))
        {
            tile.ApplyTexture(texture);
        }
    }
}
