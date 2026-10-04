namespace HumeMusic.Core.Home;

/// <summary>
/// The listener's own arrangement of Home, independent of the order Plex returns its hubs in.
/// </summary>
public static class HomeLayout
{
    /// <summary>
    /// Orders the sections available now: those the listener has arranged come first, in their
    /// order; sections they have never arranged (such as a hub Plex added later) follow in their
    /// default order. Sections that no longer exist are simply skipped.
    /// </summary>
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

    /// <summary>The arranged sections without the ones the listener has hidden.</summary>
    public static IReadOnlyList<string> Visible(IReadOnlyList<string> available, IReadOnlyList<string> savedOrder, IEnumerable<string> hidden)
    {
        var hide = new HashSet<string>(hidden, StringComparer.Ordinal);
        return Arrange(available, savedOrder).Where(id => !hide.Contains(id)).ToList();
    }
}
