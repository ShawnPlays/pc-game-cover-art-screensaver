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
/// which tile flips to which game (and where large tiles move to); this class draws it.
/// </summary>
internal sealed class MosaicView : Grid
{
    private const double FlipSeconds = 0.8;

    /// <summary>Steps planned ahead of time so their covers are loaded before they're needed.</summary>
    private const int Lookahead = 8;

    private readonly IReadOnlyList<GameEntry> games;
    private readonly SaverSettings settings;
    private readonly Stopwatch clock;
    private readonly int seed;
    private readonly int startScreen;
    private readonly MonitorTurn turn;
    private readonly bool isPreview;
    private readonly int[] featured; // indices of games shown as large tiles

    private readonly Canvas canvas = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly DispatcherTimer timer;
    private readonly Dictionary<int, MosaicTile> tiles = []; // by piece id
    private readonly Queue<MosaicStep> upcoming = new();

    private MosaicGrid layout;
    private MosaicPlanner? planner;
    private CoverTextureCache? textures;
    private CoverTextureCache? largeTextures; // decoded bigger, for large tiles
    private long stepsSeen; // steps of the shared schedule looked at so far, ours or another monitor's

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
        featured = settings.MosaicFeatured.Enabled
            ? [.. Enumerable.Range(0, games.Count).Where(i => FeaturedGames.Qualifies(games[i], settings.MosaicFeatured))]
            : [];
        if (settings.MosaicFeatured.Mode == FeaturedTileMode.Random)
        {
            // Large tiles pick games at random whatever the order setting; seeded so mirrored monitors match.
            new Random(seed).Shuffle(featured);
        }

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

        var fitted = MosaicLayout.Fit(settings.MosaicColumns, ActualWidth, ActualHeight, CoverShapes.TileAspect(settings.Filter.CoverShape, settings.Source));
        if (planner == null || fitted.Columns != layout.Columns || fitted.Rows != layout.Rows)
        {
            layout = fitted;
            BuildTiles();
        }

        layout = fitted;
        foreach (var tile in tiles.Values)
        {
            Place(tile);
        }
    }

    private void Place(MosaicTile tile)
    {
        // Round each edge (not each size) so neighbouring tiles meet exactly, with no hairline gaps.
        var piece = tile.Piece;
        double left = Math.Round(piece.Column * layout.TileWidth), right = Math.Round((piece.Column + piece.Size) * layout.TileWidth);
        double top = Math.Round(piece.Row * layout.TileHeight), bottom = Math.Round((piece.Row + piece.Size) * layout.TileHeight);
        Canvas.SetLeft(tile.Element, left);
        Canvas.SetTop(tile.Element, top);
        tile.Element.Width = right - left;
        tile.Element.Height = bottom - top;
    }

    private void BuildTiles()
    {
        // Decode covers at about the size they're drawn: a 10-column wall doesn't need 600 px textures.
        var tilePixels = layout.TileHeight * VisualTreeHelper.GetDpi(this).DpiScaleY;
        var maxHeight = isPreview ? 200 : settings.TextureHeight;
        int DecodeHeight(double pixels) => (int)Math.Clamp(Math.Ceiling(pixels / 32) * 32, 64, maxHeight);

        textures = NewTextureCache(DecodeHeight(tilePixels), large: false);
        largeTextures = NewTextureCache(DecodeHeight(tilePixels * settings.MosaicFeatured.Size), large: true);

        // Leave a tile alone while it (or a neighbour flip just before it) is still turning.
        var avoidRecent = (int)Math.Ceiling(FlipSeconds / settings.MosaicFlipSeconds) + 2;
        var large = featured.Length > 0
            ? new MosaicLargeTiles(featured, settings.MosaicFeatured.Size, settings.MosaicFeatured.Count)
            : null;
        planner = new MosaicPlanner(layout.Columns, layout.Rows, games.Count, seed, startScreen, avoidRecent, large);

        canvas.Children.Clear();
        tiles.Clear();
        upcoming.Clear();
        foreach (var piece in planner.Pieces)
        {
            var game = games[piece.GameIndex];
            var tile = AddTile(piece);
            tile.Show(game, TexturesFor(piece).Request(game));
        }

        PlanAhead();

        // Counting flips from time zero (not from now) keeps mirrored monitors in lockstep even if one lays out a bit later.
        stepsSeen = 0;
    }

    private CoverTextureCache TexturesFor(MosaicPiece piece) => piece.IsLarge ? largeTextures! : textures!;

    private MosaicTile AddTile(MosaicPiece piece)
    {
        var tile = new MosaicTile { Piece = piece };
        Place(tile);
        canvas.Children.Add(tile.Element);
        tiles[piece.Id] = tile;
        return tile;
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
        if (!upcoming.TryDequeue(out var step))
        {
            return; // single-game library: nothing to flip to
        }

        PlanAhead();
        var leaving = step.Removed.Select(id => tiles[id]).ToList();
        foreach (var id in step.Removed)
        {
            tiles.Remove(id);
        }

        foreach (var piece in step.Added)
        {
            var game = games[piece.GameIndex];
            var texture = TexturesFor(piece).Request(game);
            if (leaving.FirstOrDefault(t => t.Piece.SamePlace(piece)) is { } same)
            {
                // Same spot: flip the existing tile over.
                leaving.Remove(same);
                same.Piece = piece;
                tiles[piece.Id] = same;
                same.FlipTo(game, texture, FlipSeconds);
            }
            else
            {
                AddTile(piece).TurnIn(game, texture, FlipSeconds);
            }
        }

        foreach (var tile in leaving)
        {
            tile.TurnAway(FlipSeconds, () => canvas.Children.Remove(tile.Element));
        }

        TrimTextures(textures!, large: false);
        TrimTextures(largeTextures!, large: true);
    }

    private void TrimTextures(CoverTextureCache cache, bool large)
    {
        var keep = tiles.Values.Select(t => t.Piece)
            .Concat(upcoming.SelectMany(s => s.Added))
            .Where(p => p.IsLarge == large)
            .Select(p => games[p.GameIndex].Id)
            .ToHashSet();
        cache.Trim(keep, capacity: (large ? settings.MosaicFeatured.Count : tiles.Count) + Lookahead + 16);
    }

    private void PlanAhead()
    {
        while (upcoming.Count < Lookahead && planner!.Next() is MosaicStep step)
        {
            upcoming.Enqueue(step);
            foreach (var piece in step.Added)
            {
                TexturesFor(piece).Request(games[piece.GameIndex]); // start loading now so it's ready when the tile turns
            }
        }
    }

    /// <summary>Each cache decodes at its own size, so it only hands textures to tiles of that size.</summary>
    private CoverTextureCache NewTextureCache(int decodeHeight, bool large)
    {
        var cache = new CoverTextureCache(decodeHeight, makeReflections: false, Dispatcher);
        cache.TextureReady += (gameId, texture) =>
        {
            if (cache != (large ? largeTextures : textures))
            {
                return; // replaced since (the screen was resized)
            }

            foreach (var tile in tiles.Values.Where(t => t.Piece.IsLarge == large && t.Game?.Id == gameId))
            {
                tile.ApplyTexture(texture);
            }
        };
        return cache;
    }
}
