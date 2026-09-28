using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class MonitorTurnTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void ExactlyOneMonitorOwnsEachStep(int count)
    {
        var monitors = Enumerable.Range(0, count).Select(i => new MonitorTurn(i, count, Seed: 99)).ToList();
        for (long step = 0; step < 1000; step++)
        {
            Assert.Single(monitors, m => m.Owns(step));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void EveryMonitorGetsOneTurnPerRound(int count)
    {
        for (long round = 0; round < 200; round++)
        {
            var owners = Enumerable.Range(0, count).Select(p => MonitorTurn.OwnerOf(round * count + p, count, seed: 7));
            Assert.Equal(Enumerable.Range(0, count), owners.Order());
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SameMonitorNeverGoesTwiceInARow(int count)
    {
        foreach (var seed in new[] { 0, 1, 42, -5, int.MaxValue })
        {
            var previous = -1;
            for (long step = 0; step < 2000; step++)
            {
                var owner = MonitorTurn.OwnerOf(step, count, seed);
                Assert.NotEqual(previous, owner);
                previous = owner;
            }
        }
    }

    [Fact]
    public void OrderIsShuffledNotFixed()
    {
        var rounds = Enumerable.Range(0, 50)
            .Select(r => string.Join(",", Enumerable.Range(0, 3).Select(p => MonitorTurn.OwnerOf(r * 3 + p, 3, seed: 1))))
            .Distinct();
        Assert.True(rounds.Count() > 2, "rounds should vary, not always go left-middle-right");
    }

    [Fact]
    public void SoloOwnsEverything()
    {
        Assert.True(Enumerable.Range(0, 100).All(s => MonitorTurn.Solo.Owns(s)));
        Assert.True(new MonitorTurn(0, 1, 5).Owns(12));
    }

    [Fact]
    public void OnByDefault() => Assert.True(new SaverSettings().MonitorsTakeTurns);
}
