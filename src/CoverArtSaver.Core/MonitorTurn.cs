namespace CoverArtSaver.Core;

/// <summary>
/// Lets several monitors share one stream of changes so only one cover changes at a time across all of them.
/// Step n of the shared schedule belongs to exactly one monitor. Monitors take turns in a shuffled order:
/// every monitor gets one step per round, and the same monitor never goes twice in a row.
/// Every monitor computes the same answer from the same seed, so they need no communication.
/// </summary>
public readonly record struct MonitorTurn(int Index, int Count, int Seed)
{
    /// <summary>A single monitor (or monitors not taking turns): every step is its own.</summary>
    public static readonly MonitorTurn Solo = new(0, 1, 0);

    public bool Owns(long step) => Count <= 1 || OwnerOf(step, Count, Seed) == Index;

    public static int OwnerOf(long step, int count, int seed)
    {
        if (count <= 1)
        {
            return 0;
        }

        var round = step / count;
        var position = (int)(step % count);
        if (count == 2)
        {
            return (int)(step % 2) ^ (seed & 1); // with two, strict alternation is the only way to never repeat
        }

        var order = RoundOrder(round, count, seed);

        // Don't let a round start with the monitor that ended the previous one. Only the first two entries are
        // swapped, so the last entry (what the next round checks against) never changes; with 3+ monitors that
        // keeps this a pure function of the step.
        if (round > 0 && order[0] == RoundOrder(round - 1, count, seed)[count - 1])
        {
            (order[0], order[1]) = (order[1], order[0]);
        }

        return order[position];
    }

    private static int[] RoundOrder(long round, int count, int seed)
    {
        var order = Enumerable.Range(0, count).ToArray();
        var rng = new Random(unchecked(seed * 486187739 + (int)round * 16777619 + (int)(round >> 32)));
        for (var i = count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }
}
