namespace HumeMusic.Core.Mixes;

/// <summary>How much two mixes are the same mix: the share of their leading artists they have in common.</summary>
public static class MixOverlap
{
    /// <summary>Fewer artists than this and there isn't enough to say two mixes are alike.</summary>
    public const int MinArtists = 4;

    /// <summary>The share of the smaller set that the other also has (0 to 1; 0 when either is too small to compare).</summary>
    public static double Between(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count < MinArtists || b.Count < MinArtists) return 0;
        var other = b.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shared = a.Distinct(StringComparer.OrdinalIgnoreCase).Count(other.Contains);
        return shared / (double)Math.Min(a.Count, b.Count);
    }
}
