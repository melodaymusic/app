namespace HumeMusic.Core.Layout;

/// <summary>
/// The column grid horizontal rows share: a width is split into an even number of equal columns
/// (so two-column cards divide it exactly) as close as possible to a target column width.
/// </summary>
public static class RowColumns
{
    /// <summary>Number of columns: even, at least two, closest to the target width (by ratio).</summary>
    public static int Count(double span, double target, double gap)
    {
        if (span <= 0 || target <= 0) return 2;
        var raw = (span + gap) / (target + gap);
        var lower = Math.Max(2, 2 * (int)Math.Floor(raw / 2));
        var upper = lower + 2;
        // Compare in ratio terms so "20% too big" and "20% too small" weigh the same.
        var errorLower = Math.Abs(Math.Log(Width(span, lower, gap) / target));
        var upperWidth = Width(span, upper, gap);
        var errorUpper = upperWidth > 0 ? Math.Abs(Math.Log(upperWidth / target)) : double.MaxValue;
        return errorUpper < errorLower ? upper : lower;
    }

    /// <summary>Column width for the chosen number of columns.</summary>
    public static double Width(double span, double target, double gap) => Width(span, Count(span, target, gap), gap);

    private static double Width(double span, int count, double gap) => (span - (count - 1) * gap) / count;
}
