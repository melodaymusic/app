namespace MelodayMusic.Core.Home;

public static class HomeLayout
{
    public static IReadOnlyList<string> Arrange(IReadOnlyList<string> available, IReadOnlyList<string> savedOrder)
    {
        var present = new HashSet<string>(available, StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var id in savedOrder)
            if (present.Contains(id) && !result.Contains(id)) result.Add(id);
        foreach (var id in available)
            if (!result.Contains(id)) result.Add(id);
        return result;
    }

    public static bool IsCurated(string id) =>
        id is "spotlight" or "artist.mixes" or "stations" || id.StartsWith("context.", StringComparison.Ordinal);

    public static bool IsLibraryHub(string id) => id != "pinned" && !IsCurated(id);

    private static readonly string[] DefaultOrder =
    [
        "pinned", "artist.mixes", "context.discovery", "stations", "context.time", "context.day", "context.season", "context.repeat",
        "context.new", "context.forgotten", "context.lastyear", "context.decade", "spotlight",
    ];

    public static IReadOnlyList<string> CuratedFirst(IEnumerable<string> available) =>
        available.OrderBy(id => Array.IndexOf(DefaultOrder, id) is var i and >= 0 ? i : IsCurated(id) ? DefaultOrder.Length : DefaultOrder.Length + 1).ToList();

    public static IReadOnlyList<string> Visible(IReadOnlyList<string> available, IReadOnlyList<string> savedOrder, IEnumerable<string> hidden)
    {
        var hide = new HashSet<string>(hidden, StringComparer.Ordinal);
        return Arrange(available, savedOrder).Where(id => !hide.Contains(id)).ToList();
    }
}
