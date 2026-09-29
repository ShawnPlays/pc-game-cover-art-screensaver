using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class MosaicLayoutTests
{
    [Theory]
    [InlineData(10, 1920, 1080, 4)]  // 192 × 288 ideal → 3.75 rows → 4
    [InlineData(10, 2560, 1440, 4)]  // same shape, same rows
    [InlineData(10, 1080, 1920, 12)] // portrait monitor: many more rows
    [InlineData(20, 1920, 1080, 8)]
    [InlineData(4, 1920, 1080, 2)]
    public void RowsFollowScreenShape(int columns, double width, double height, int expectedRows)
    {
        var grid = MosaicLayout.Fit(columns, width, height);
        Assert.Equal(columns, grid.Columns);
        Assert.Equal(expectedRows, grid.Rows);
    }

    [Fact]
    public void TilesExactlyFillTheScreen()
    {
        var grid = MosaicLayout.Fit(10, 1920, 1080);
        Assert.Equal(1920, grid.TileWidth * grid.Columns, 6);
        Assert.Equal(1080, grid.TileHeight * grid.Rows, 6);
    }

    [Fact]
    public void AlwaysAtLeastOneRow()
    {
        Assert.Equal(1, MosaicLayout.Fit(2, 3000, 100).Rows);
        Assert.Equal(1, MosaicLayout.Fit(10, 0, 0).Rows);
    }
}

public class MosaicPlannerTests
{
    /// <summary>Game index on each cell, in row-major order.</summary>
    private static int[] Cells(MosaicPlanner planner, int columns, int rows)
    {
        var cells = Enumerable.Repeat(-1, columns * rows).ToArray();
        foreach (var piece in planner.Pieces)
        {
            for (var r = piece.Row; r < piece.Row + piece.Size; r++)
            {
                for (var c = piece.Column; c < piece.Column + piece.Size; c++)
                {
                    Assert.Equal(-1, cells[r * columns + c]); // no overlaps
                    cells[r * columns + c] = piece.GameIndex;
                }
            }
        }

        Assert.DoesNotContain(-1, cells); // no gaps
        return cells;
    }

    private static MosaicPiece SingleFlip(MosaicStep? step)
    {
        Assert.NotNull(step);
        Assert.Single(step.Removed);
        return Assert.Single(step.Added);
    }

    [Fact]
    public void InitialFillFollowsListOrder()
    {
        var planner = new MosaicPlanner(columns: 2, rows: 2, gameCount: 10, seed: 1, screen: 1);
        Assert.Equal(new[] { 4, 5, 6, 7 }, Cells(planner, 2, 2)); // one screen's worth (4 tiles) in
    }

    [Fact]
    public void SmallLibraryRepeatsToFillGrid()
    {
        var planner = new MosaicPlanner(columns: 5, rows: 1, gameCount: 2, seed: 1);
        Assert.Equal(new[] { 0, 1, 0, 1, 0 }, Cells(planner, 5, 1));
    }

    [Fact]
    public void NeverShowsTheSameGameTwiceWhenLibraryIsBigEnough()
    {
        var planner = new MosaicPlanner(columns: 4, rows: 3, gameCount: 15, seed: 7);
        for (var i = 0; i < 500; i++)
        {
            planner.Next();
            var cells = Cells(planner, 4, 3);
            Assert.Equal(cells.Length, cells.Distinct().Count());
        }
    }

    [Fact]
    public void FlipsBringInGamesInListOrder()
    {
        var planner = new MosaicPlanner(columns: 3, rows: 1, gameCount: 10, seed: 7);
        var flipped = Enumerable.Range(0, 5).Select(_ => SingleFlip(planner.Next()).GameIndex);
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, flipped);
    }

    [Fact]
    public void FlipAlwaysChangesTheTile()
    {
        var planner = new MosaicPlanner(columns: 3, rows: 3, gameCount: 3, seed: 3);
        for (var i = 0; i < 200; i++)
        {
            var before = Cells(planner, 3, 3);
            var added = SingleFlip(planner.Next());
            var cell = added.Row * 3 + added.Column;
            Assert.NotEqual(before[cell], added.GameIndex);
            Assert.Equal(added.GameIndex, Cells(planner, 3, 3)[cell]);
        }
    }

    [Fact]
    public void RecentlyFlippedTilesAreLeftAlone()
    {
        var planner = new MosaicPlanner(columns: 5, rows: 4, gameCount: 100, seed: 11, avoidRecentSteps: 5);
        var history = new List<(int, int)>();
        for (var i = 0; i < 1000; i++)
        {
            var added = SingleFlip(planner.Next());
            Assert.DoesNotContain((added.Column, added.Row), history.TakeLast(5));
            history.Add((added.Column, added.Row));
        }
    }

    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var large = new MosaicLargeTiles([3, 9, 27, 81, 150], Size: 2, Count: 2);
        var a = new MosaicPlanner(6, 5, 200, seed: 42, largeTiles: large);
        var b = new MosaicPlanner(6, 5, 200, seed: 42, largeTiles: large);
        Assert.Equal(a.Pieces, b.Pieces);
        for (var i = 0; i < 300; i++)
        {
            var stepA = a.Next()!;
            var stepB = b.Next()!;
            Assert.Equal(stepA.Removed, stepB.Removed);
            Assert.Equal(stepA.Added, stepB.Added);
        }
    }

    [Fact]
    public void SingleGameNeverFlips()
    {
        var planner = new MosaicPlanner(columns: 3, rows: 2, gameCount: 1, seed: 1);
        Assert.Null(planner.Next());
    }

    [Fact]
    public void SingleTileStillFlips()
    {
        var planner = new MosaicPlanner(columns: 1, rows: 1, gameCount: 3, seed: 1, avoidRecentSteps: 4);
        Assert.Equal(1, SingleFlip(planner.Next()).GameIndex);
        Assert.Equal(2, SingleFlip(planner.Next()).GameIndex);
    }
}

public class MosaicLargeTileTests
{
    private static readonly int[] Featured = [5, 10, 15, 20, 25];

    private static MosaicPlanner Planner(int columns = 10, int rows = 6, int gameCount = 60, int size = 3, int count = 2, int[]? featured = null) =>
        new(columns, rows, gameCount, seed: 5, largeTiles: new MosaicLargeTiles(featured ?? Featured, size, count));

    [Fact]
    public void FeaturedGamesAreShownLargeAndOnlyLarge()
    {
        var planner = Planner();
        var large = planner.Pieces.Where(p => p.IsLarge).ToList();
        Assert.Equal(2, large.Count);
        Assert.All(large, p => Assert.Equal(3, p.Size));
        Assert.All(large, p => Assert.Contains(p.GameIndex, Featured));
        Assert.All(planner.Pieces.Where(p => !p.IsLarge), p => Assert.DoesNotContain(p.GameIndex, Featured));
    }

    [Fact]
    public void StepsKeepTheWallCoveredAsLargeTilesMove()
    {
        const int columns = 10, rows = 6;
        var planner = Planner(columns, rows);
        var board = planner.Pieces.ToDictionary(p => p.Id);
        var largeSpots = new HashSet<(int, int)>();

        for (var i = 0; i < 3000; i++)
        {
            var step = planner.Next()!;
            foreach (var id in step.Removed)
            {
                Assert.True(board.Remove(id), "removed a tile that wasn't on screen");
            }

            foreach (var piece in step.Added)
            {
                board.Add(piece.Id, piece);
            }

            // What the renderer builds from the steps matches the planner, with every cell covered exactly once.
            Assert.Equal(planner.Pieces.OrderBy(p => p.Id), board.Values.OrderBy(p => p.Id));
            var covered = board.Values.Sum(p => p.Size * p.Size);
            Assert.Equal(columns * rows, covered);
            Assert.Equal(columns * rows, board.Values
                .SelectMany(p => from r in Enumerable.Range(p.Row, p.Size) from c in Enumerable.Range(p.Column, p.Size) select (c, r))
                .Distinct().Count());

            Assert.Equal(2, board.Values.Count(p => p.IsLarge));
            Assert.All(board.Values, p => Assert.Equal(p.IsLarge, Featured.Contains(p.GameIndex)));
            foreach (var p in board.Values.Where(p => p.IsLarge))
            {
                largeSpots.Add((p.Column, p.Row));
            }
        }

        Assert.True(largeSpots.Count > 5, "large tiles should move around the wall");
    }

    [Fact]
    public void MovingALargeTileBringsInAnotherFeaturedGame()
    {
        var planner = Planner();
        for (var i = 0; i < 500; i++)
        {
            var step = planner.Next()!;
            if (step.Added.FirstOrDefault(p => p.IsLarge) is { Size: 3 } moved)
            {
                // Only 2 of the 5 featured games are on screen, so the moved tile shows one that wasn't.
                Assert.Equal(1, planner.Pieces.Count(p => p.GameIndex == moved.GameIndex));
                Assert.True(step.Removed.Count > 1); // the large tile and the ones under its new spot flip together
                return;
            }
        }

        Assert.Fail("no large tile moved");
    }

    [Fact]
    public void LargeTilesAreLeftOutWhenTheyDontFit()
    {
        var planner = Planner(columns: 4, rows: 2, size: 3);
        Assert.DoesNotContain(planner.Pieces, p => p.IsLarge);
        Assert.Equal(8, planner.Pieces.Count);
    }

    [Fact]
    public void NoMoreLargeTilesThanFeaturedGames()
    {
        var planner = Planner(count: 4, featured: [7]);
        var large = Assert.Single(planner.Pieces, p => p.IsLarge);
        Assert.Equal(7, large.GameIndex);

        // With one featured game it still moves around.
        for (var i = 0; i < 200; i++)
        {
            planner.Next();
        }

        Assert.Equal(7, Assert.Single(planner.Pieces, p => p.IsLarge).GameIndex);
    }

    [Fact]
    public void FewerLargeTilesWhenTheScreenIsFull()
    {
        var planner = Planner(columns: 4, rows: 4, size: 2, count: 4, featured: [1, 2, 3, 4, 5]);
        Assert.InRange(planner.Pieces.Count(p => p.IsLarge), 1, 4);
        for (var i = 0; i < 300; i++)
        {
            Assert.NotNull(planner.Next());
        }
    }

    [Fact]
    public void RandomLargeTilesUseTheGivenOrderAndNeverDuplicateTheWall()
    {
        // Random mode: every game can be large, handed over in shuffled order.
        int[] shuffled = [.. Enumerable.Range(0, 100).OrderBy(i => (i * 37) % 100)];
        var planner = Planner(gameCount: 100, featured: shuffled, count: 2);

        var large = planner.Pieces.Where(p => p.IsLarge).Select(p => p.GameIndex).ToList();
        Assert.Equal(shuffled.Take(2), large);

        var seenLarge = new HashSet<int>(large);
        var seenSmall = new HashSet<int>();
        for (var i = 0; i < 2000; i++)
        {
            var onScreen = planner.Pieces.Select(p => p.GameIndex).ToList();
            Assert.Equal(onScreen.Count, onScreen.Distinct().Count()); // no game shown twice, large or small
            planner.Next();
            seenLarge.UnionWith(planner.Pieces.Where(p => p.IsLarge).Select(p => p.GameIndex));
            seenSmall.UnionWith(planner.Pieces.Where(p => !p.IsLarge).Select(p => p.GameIndex));
        }

        Assert.True(seenLarge.Count > 20, "large tiles should cycle through many games");
        Assert.NotEmpty(seenLarge.Intersect(seenSmall)); // the same game can show up either way
    }

    [Fact]
    public void WhenEveryGameIsFeaturedSmallTilesUseThemToo()
    {
        var planner = Planner(gameCount: 5, featured: [0, 1, 2, 3, 4], count: 1);
        Assert.Single(planner.Pieces, p => p.IsLarge);
        for (var i = 0; i < 200; i++)
        {
            Assert.NotNull(planner.Next());
        }
    }
}
