namespace CoverArtSaver.Core;

public static class GameOrdering
{
    public static List<GameEntry> Order(IEnumerable<GameEntry> games, CoverOrder order, int randomSeed)
    {
        var list = games.ToList();
        switch (order)
        {
            case CoverOrder.Alphabetical:
                return [.. list.OrderBy(g => g.SortingName ?? g.Name, StringComparer.CurrentCultureIgnoreCase)];
            case CoverOrder.RecentlyPlayed:
                return [.. list.OrderByDescending(g => g.LastActivity ?? DateTime.MinValue).ThenBy(g => g.Name)];
            case CoverOrder.RecentlyAdded:
                return [.. list.OrderByDescending(g => g.Added ?? DateTime.MinValue).ThenBy(g => g.Name)];
            case CoverOrder.ReleaseYear:
                return [.. list.OrderBy(g => g.ReleaseYear ?? int.MaxValue).ThenBy(g => g.SortingName ?? g.Name)];
            default:
                // Fisher–Yates shuffle with a seed, so "Mirror" monitors can share the same order.
                var rng = new Random(randomSeed);
                for (var i = list.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }

                return list;
        }
    }
}
