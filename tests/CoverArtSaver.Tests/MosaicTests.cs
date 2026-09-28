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
    [Fact]
    public void InitialFillFollowsListOrder()
    {
        var planner = new MosaicPlanner(tileCount: 4, gameCount: 10, seed: 1, firstGame: 3);
        Assert.Equal(new[] { 3, 4, 5, 6 }, planner.Tiles);
    }

    [Fact]
    public void SmallLibraryRepeatsToFillGrid()
    {
        var planner = new MosaicPlanner(tileCount: 5, gameCount: 2, seed: 1);
        Assert.Equal(new[] { 0, 1, 0, 1, 0 }, planner.Tiles);
    }

    [Fact]
    public void NeverShowsTheSameGameTwiceWhenLibraryIsBigEnough()
    {
        var planner = new MosaicPlanner(tileCount: 12, gameCount: 15, seed: 7);
        for (var i = 0; i < 500; i++)
        {
            planner.Next();
            Assert.Equal(planner.Tiles.Count, planner.Tiles.Distinct().Count());
        }
    }

    [Fact]
    public void FlipsBringInGamesInListOrder()
    {
        var planner = new MosaicPlanner(tileCount: 3, gameCount: 10, seed: 7);
        var flipped = Enumerable.Range(0, 5).Select(_ => planner.Next()!.Value.GameIndex);
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, flipped);
    }

    [Fact]
    public void FlipAlwaysChangesTheTile()
    {
        var planner = new MosaicPlanner(tileCount: 9, gameCount: 3, seed: 3);
        for (var i = 0; i < 200; i++)
        {
            var before = planner.Tiles.ToArray();
            var flip = planner.Next()!.Value;
            Assert.NotEqual(before[flip.Tile], flip.GameIndex);
            Assert.Equal(flip.GameIndex, planner.Tiles[flip.Tile]);
        }
    }

    [Fact]
    public void RecentlyFlippedTilesAreLeftAlone()
    {
        var planner = new MosaicPlanner(tileCount: 20, gameCount: 100, seed: 11, avoidRecentTiles: 5);
        var history = new List<int>();
        for (var i = 0; i < 1000; i++)
        {
            var tile = planner.Next()!.Value.Tile;
            Assert.DoesNotContain(tile, history.TakeLast(5));
            history.Add(tile);
        }
    }

    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = new MosaicPlanner(30, 200, seed: 42);
        var b = new MosaicPlanner(30, 200, seed: 42);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.Next(), b.Next());
        }
    }

    [Fact]
    public void SingleGameNeverFlips()
    {
        var planner = new MosaicPlanner(tileCount: 6, gameCount: 1, seed: 1);
        Assert.Null(planner.Next());
    }

    [Fact]
    public void SingleTileStillFlips()
    {
        var planner = new MosaicPlanner(tileCount: 1, gameCount: 3, seed: 1, avoidRecentTiles: 4);
        Assert.Equal(new MosaicFlip(0, 1), planner.Next());
        Assert.Equal(new MosaicFlip(0, 2), planner.Next());
    }
}
