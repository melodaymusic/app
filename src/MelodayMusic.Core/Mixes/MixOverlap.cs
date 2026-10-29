namespace MelodayMusic.Core.Mixes;

public static class MixOverlap
{
    public const int MinArtists = 4;

    public static double Between(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count < MinArtists || b.Count < MinArtists) return 0;
        var other = b.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shared = a.Distinct(StringComparer.OrdinalIgnoreCase).Count(other.Contains);
        return shared / (double)Math.Min(a.Count, b.Count);
    }
}
