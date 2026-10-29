namespace MelodayMusic.Core.Layout;

public static class RowColumns
{
    public static int Count(double span, double target, double gap)
    {
        if (span <= 0 || target <= 0) return 2;
        var raw = (span + gap) / (target + gap);
        var lower = Math.Max(2, 2 * (int)Math.Floor(raw / 2));
        var upper = lower + 2;
        var errorLower = Math.Abs(Math.Log(Width(span, lower, gap) / target));
        var upperWidth = Width(span, upper, gap);
        var errorUpper = upperWidth > 0 ? Math.Abs(Math.Log(upperWidth / target)) : double.MaxValue;
        return errorUpper < errorLower ? upper : lower;
    }

    public static double Width(double span, double target, double gap) => Width(span, Count(span, target, gap), gap);

    private static double Width(double span, int count, double gap) => (span - (count - 1) * gap) / count;
}
