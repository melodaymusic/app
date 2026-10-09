namespace HumeMusic.Core.Covers;

/// <summary>
/// How big a cover's text is set: the size it always has, unless the text is too long for its box, in which case it is
/// made just small enough to fit (never smaller than <see cref="Floor"/> of the usual size, so it stays readable).
/// </summary>
public static class CoverFit
{
    /// <summary>The smallest a name or label is set, as a share of its usual size.</summary>
    public const double Floor = 0.4;

    /// <summary>
    /// The usual size when <paramref name="fits"/> says the text fits at it; otherwise the largest size between the floor
    /// and the usual one at which it does (found to within a fraction of a unit), or the floor when nothing does.
    /// </summary>
    public static double Size(double usual, Func<double, bool> fits)
    {
        if (fits(usual)) return usual;
        var low = usual * Floor;
        if (!fits(low)) return low;
        var high = usual;
        for (var i = 0; i < 14; i++)
        {
            var middle = (low + high) / 2;
            if (fits(middle)) low = middle;
            else high = middle;
        }
        return low;
    }
}
