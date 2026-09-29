namespace CoverArtSaver.Core;

/// <summary>The mosaic's grid in screen units (DIPs). Tiles are laid edge to edge and exactly fill the screen.</summary>
public readonly record struct MosaicGrid(int Columns, int Rows, double TileWidth, double TileHeight)
{
    public int TileCount => Columns * Rows;
}

/// <summary>
/// One tile on the mosaic: a square of <see cref="Size"/>×<see cref="Size"/> grid cells whose top-left cell is at
/// (<see cref="Column"/>, <see cref="Row"/>), showing the game at <see cref="GameIndex"/>. Normal tiles have size 1.
/// <see cref="Id"/> is unique for the life of the planner, so a tile that flips gets a new id.
/// </summary>
public readonly record struct MosaicPiece(int Id, int Column, int Row, int Size, int GameIndex)
{
    public bool IsLarge => Size > 1;

    public bool SamePlace(MosaicPiece other) => Column == other.Column && Row == other.Row && Size == other.Size;

    public bool Overlaps(int column, int row, int size) => MosaicPlanner.Overlaps(Column, Row, Size, column, row, size);
}

/// <summary>
/// One step of the mosaic: the <see cref="Removed"/> tiles turn away and the <see cref="Added"/> ones turn in.
/// Usually that's one tile flipping to another game; when a large tile moves, several flip at once.
/// </summary>
public sealed record MosaicStep(IReadOnlyList<int> Removed, IReadOnlyList<MosaicPiece> Added);

/// <summary>Which games (indices into the game list) get large tiles, how many cells across each is, and how many to show.</summary>
public sealed record MosaicLargeTiles(IReadOnlyCollection<int> Games, int Size, int Count);

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
/// Decides what the mosaic shows: the starting cover for every tile, then an endless series of steps.
/// Tiles are picked at random (never one that changed recently, so a tile doesn't flip while it's still turning),
/// and new covers come from the game list in order, skipping games already on screen whenever the library is
/// big enough. Seeded, so two mirrored monitors produce exactly the same sequence.
/// <para>
/// Optionally some games are shown as large tiles instead. Those games only ever appear large (unless every game
/// is one of them). When a large tile's turn comes it moves somewhere else: the tiles under its new spot turn
/// away, and the space it leaves fills with normal tiles.
/// </para>
/// </summary>
public sealed class MosaicPlanner
{
    private readonly int columns;
    private readonly int rows;
    private readonly int largeSize;
    private readonly GamePool small;
    private readonly GamePool large;
    private readonly int[] onScreen; // how many tiles currently show each game
    private readonly List<MosaicPiece> pieces = [];
    private readonly long[] lastChanged; // per cell, the step it last changed at
    private readonly int avoidRecentSteps;
    private readonly Random rng;
    private long step;
    private int nextId;

    /// <param name="screen">Start this many screens' worth into the game list (lets Independent monitors differ).</param>
    /// <param name="avoidRecentSteps">Leave a tile alone for this many steps after it changes.</param>
    /// <param name="largeTiles">Games to show as large tiles, or null for none.</param>
    public MosaicPlanner(int columns, int rows, int gameCount, int seed, int screen = 0, int avoidRecentSteps = 4, MosaicLargeTiles? largeTiles = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(gameCount, 1);

        this.columns = columns;
        this.rows = rows;
        this.avoidRecentSteps = Math.Max(0, avoidRecentSteps);
        rng = new Random(seed);
        onScreen = new int[gameCount];
        lastChanged = new long[columns * rows];
        Array.Fill(lastChanged, long.MinValue / 2);

        largeSize = largeTiles?.Size ?? 0;
        var largeGames = largeTiles == null || largeSize < 2 || largeSize > Math.Min(columns, rows)
            ? [] // none asked for, or they wouldn't fit on this screen
            : largeTiles.Games.Where(g => g >= 0 && g < gameCount).Distinct().Order().ToArray();
        var smallGames = Enumerable.Range(0, gameCount).Except(largeGames).ToArray();
        small = new GamePool(smallGames.Length > 0 ? smallGames : [.. Enumerable.Range(0, gameCount)]);
        large = new GamePool(largeGames);

        // Scatter the large tiles first; never more of them than there are games to put on them.
        var largeCount = Math.Min(largeTiles?.Count ?? 0, largeGames.Length);
        var placed = new List<(int Column, int Row)>();
        for (var i = 0; i < largeCount; i++)
        {
            var spots = FreeSpots(placed).ToList();
            if (spots.Count == 0)
            {
                break;
            }

            placed.Add(spots[rng.Next(spots.Count)]);
        }

        // Fill the rest left to right, top to bottom, in list order (repeating if the library is small).
        large.Skip(screen * placed.Count);
        small.Skip(screen * (columns * rows - placed.Count * largeSize * largeSize));
        foreach (var (column, row) in placed)
        {
            pieces.Add(NewPiece(column, row, largeSize, large.TakeNext()));
        }

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!placed.Any(p => Overlaps(p.Column, p.Row, largeSize, column, row, 1)))
                {
                    pieces.Add(NewPiece(column, row, 1, small.TakeNext()));
                }
            }
        }
    }

    /// <summary>Every tile on screen.</summary>
    public IReadOnlyList<MosaicPiece> Pieces => pieces;

    /// <summary>The next step, or null when there's nothing to change (a single-game library).</summary>
    public MosaicStep? Next()
    {
        var changeable = pieces.Where(p => p.IsLarge || small.Count >= 2).ToList();
        if (changeable.Count == 0)
        {
            return null;
        }

        var settled = changeable.Where(p => !IsBusy(p.Column, p.Row, p.Size)).ToList();
        var candidates = settled.Count > 0 ? settled : changeable;
        var piece = candidates[rng.Next(candidates.Count)];
        var result = piece.IsLarge ? MoveLarge(piece) : FlipSmall(piece);
        step++;
        return result;
    }

    private MosaicStep FlipSmall(MosaicPiece piece)
    {
        // Prefer a game that isn't on screen at all; with a small library, settle for one that's at least different.
        var game = small.Find(g => onScreen[g] == 0) ?? small.Find(g => g != piece.GameIndex)!.Value;
        return Replace([piece], [NewPiece(piece.Column, piece.Row, 1, game)]);
    }

    private MosaicStep MoveLarge(MosaicPiece piece)
    {
        var others = pieces.Where(p => p.IsLarge && p.Id != piece.Id).Select(p => (p.Column, p.Row)).ToList();
        var elsewhere = FreeSpots(others).Where(s => s != (piece.Column, piece.Row)).ToList();
        var settled = elsewhere.Where(s => !IsBusy(s.Column, s.Row, largeSize)).ToList();
        var candidates = settled.Count > 0 ? settled : elsewhere;
        var (column, row) = candidates.Count > 0 ? candidates[rng.Next(candidates.Count)] : (piece.Column, piece.Row);

        var game = large.Find(g => onScreen[g] == 0) ?? large.Find(g => g != piece.GameIndex) ?? piece.GameIndex;
        var removed = pieces.Where(p => !p.IsLarge && p.Overlaps(column, row, largeSize)).Prepend(piece).ToList();
        var added = new List<MosaicPiece> { NewPiece(column, row, largeSize, game) };

        // Normal tiles take over the cells the large tile leaves behind.
        for (var r = piece.Row; r < piece.Row + piece.Size; r++)
        {
            for (var c = piece.Column; c < piece.Column + piece.Size; c++)
            {
                if (!Overlaps(column, row, largeSize, c, r, 1))
                {
                    added.Add(NewPiece(c, r, 1, small.Find(g => onScreen[g] == 0) ?? small.TakeNext()));
                }
            }
        }

        return Replace(removed, added);
    }

    /// <summary>Swaps tiles on the board. The added ones were already counted as on screen by <see cref="NewPiece"/>.</summary>
    private MosaicStep Replace(List<MosaicPiece> removed, List<MosaicPiece> added)
    {
        foreach (var piece in removed)
        {
            onScreen[piece.GameIndex]--;
            pieces.Remove(piece);
        }

        foreach (var piece in added)
        {
            pieces.Add(piece);
            for (var r = piece.Row; r < piece.Row + piece.Size; r++)
            {
                for (var c = piece.Column; c < piece.Column + piece.Size; c++)
                {
                    lastChanged[r * columns + c] = step;
                }
            }
        }

        return new MosaicStep([.. removed.Select(p => p.Id)], added);
    }

    private MosaicPiece NewPiece(int column, int row, int size, int game)
    {
        onScreen[game]++;
        return new MosaicPiece(nextId++, column, row, size, game);
    }

    /// <summary>Top-left cells where a large tile fits without overlapping the given large tiles.</summary>
    private IEnumerable<(int Column, int Row)> FreeSpots(List<(int Column, int Row)> taken)
    {
        for (var row = 0; row <= rows - largeSize; row++)
        {
            for (var column = 0; column <= columns - largeSize; column++)
            {
                if (!taken.Any(t => Overlaps(t.Column, t.Row, largeSize, column, row, largeSize)))
                {
                    yield return (column, row);
                }
            }
        }
    }

    /// <summary>Whether two squares of cells overlap.</summary>
    internal static bool Overlaps(int columnA, int rowA, int sizeA, int columnB, int rowB, int sizeB) =>
        columnA < columnB + sizeB && columnB < columnA + sizeA && rowA < rowB + sizeB && rowB < rowA + sizeA;

    /// <summary>Whether any cell in the square changed within the last few steps (and may still be turning).</summary>
    private bool IsBusy(int column, int row, int size)
    {
        for (var r = row; r < row + size; r++)
        {
            for (var c = column; c < column + size; c++)
            {
                if (step - lastChanged[r * columns + c] <= avoidRecentSteps)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Part of the game list, walked through in order from where the last pick left off.</summary>
    private sealed class GamePool(int[] games)
    {
        private int next;

        public int Count => games.Length;

        public void Skip(int count)
        {
            if (games.Length > 0)
            {
                next = CoverflowMath.WrapIndex(count, games.Length);
            }
        }

        public int TakeNext()
        {
            var game = games[next];
            next = (next + 1) % games.Length;
            return game;
        }

        public int? Find(Func<int, bool> acceptable)
        {
            for (var i = 0; i < games.Length; i++)
            {
                var game = games[(next + i) % games.Length];
                if (acceptable(game))
                {
                    next = (next + i + 1) % games.Length;
                    return game;
                }
            }

            return null;
        }
    }
}
