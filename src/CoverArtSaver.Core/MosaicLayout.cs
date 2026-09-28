namespace CoverArtSaver.Core;

/// <summary>The mosaic's grid in screen units (DIPs). Tiles are laid edge to edge and exactly fill the screen.</summary>
public readonly record struct MosaicGrid(int Columns, int Rows, double TileWidth, double TileHeight)
{
    public int TileCount => Columns * Rows;
}

/// <summary>One step of the mosaic: <see cref="Tile"/> flips over to show the game at <see cref="GameIndex"/>.</summary>
public readonly record struct MosaicFlip(int Tile, int GameIndex);

public static class MosaicLayout
{
    /// <summary>Width ÷ height of a tile. Playnite's standard cover size is 600×900.</summary>
    public const double TileAspect = 2.0 / 3.0;

    /// <summary>
    /// The user picks the number of columns; the number of rows follows from the screen's shape so tiles keep
    /// (roughly) box-art proportions. Tile height is then stretched slightly so the rows exactly fill the
    /// screen with no half-visible row; covers are drawn "uniform to fill", so that only trims a sliver of art.
    /// </summary>
    public static MosaicGrid Fit(int columns, double width, double height, double tileAspect = TileAspect)
    {
        columns = Math.Max(1, columns);
        if (width <= 0 || height <= 0)
        {
            return new MosaicGrid(columns, 1, 0, 0);
        }

        var tileWidth = width / columns;
        var idealHeight = tileWidth / tileAspect;
        var rows = Math.Max(1, (int)Math.Round(height / idealHeight, MidpointRounding.AwayFromZero));
        return new MosaicGrid(columns, rows, tileWidth, height / rows);
    }
}

/// <summary>
/// Decides what the mosaic shows: the starting cover for every tile, then an endless series of flips.
/// Tiles are picked at random (never one that flipped recently, so a tile doesn't flip while it's still turning),
/// and new covers come from the game list in order, skipping games already on screen whenever the library is
/// big enough. Seeded, so two mirrored monitors produce exactly the same sequence.
/// </summary>
public sealed class MosaicPlanner
{
    private readonly int gameCount;
    private readonly int[] tiles;
    private readonly int[] onScreen; // how many tiles currently show each game
    private readonly Random rng;
    private readonly Queue<int> recent = new();
    private readonly HashSet<int> recentSet = [];
    private readonly int recentLimit;
    private int nextGame;

    /// <param name="firstGame">Index into the game list for the top-left tile.</param>
    /// <param name="avoidRecentTiles">How many of the most recently flipped tiles to leave alone.</param>
    public MosaicPlanner(int tileCount, int gameCount, int seed, int firstGame = 0, int avoidRecentTiles = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tileCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(gameCount, 1);

        this.gameCount = gameCount;
        tiles = new int[tileCount];
        onScreen = new int[gameCount];
        rng = new Random(seed);
        recentLimit = Math.Clamp(avoidRecentTiles, 0, tileCount - 1);
        nextGame = CoverflowMath.WrapIndex(firstGame, gameCount);

        // Fill the grid left to right, top to bottom, in list order (repeating if the library is small).
        for (var i = 0; i < tileCount; i++)
        {
            tiles[i] = nextGame;
            onScreen[nextGame]++;
            nextGame = (nextGame + 1) % gameCount;
        }
    }

    /// <summary>Game index shown on each tile, in row-major order.</summary>
    public IReadOnlyList<int> Tiles => tiles;

    /// <summary>The next flip, or null when there's nothing to change to (a single-game library).</summary>
    public MosaicFlip? Next()
    {
        if (gameCount < 2)
        {
            return null;
        }

        var tile = PickTile();
        var current = tiles[tile];

        // Prefer a game that isn't on screen at all; with a small library, settle for one that's at least different.
        var game = FindGame(g => onScreen[g] == 0) ?? FindGame(g => g != current)!.Value;

        onScreen[current]--;
        onScreen[game]++;
        tiles[tile] = game;
        return new MosaicFlip(tile, game);
    }

    private int PickTile()
    {
        int tile;
        do
        {
            tile = rng.Next(tiles.Length);
        }
        while (recentSet.Contains(tile)); // terminates: recentLimit < tile count

        if (recentLimit > 0)
        {
            recent.Enqueue(tile);
            recentSet.Add(tile);
            if (recent.Count > recentLimit)
            {
                recentSet.Remove(recent.Dequeue());
            }
        }

        return tile;
    }

    /// <summary>Walks forward through the game list from where the last flip left off.</summary>
    private int? FindGame(Func<int, bool> acceptable)
    {
        for (var i = 0; i < gameCount; i++)
        {
            var g = (nextGame + i) % gameCount;
            if (acceptable(g))
            {
                nextGame = (g + 1) % gameCount;
                return g;
            }
        }

        return null;
    }
}
