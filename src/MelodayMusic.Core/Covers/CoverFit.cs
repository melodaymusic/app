namespace MelodayMusic.Core.Covers;

public static class CoverFit
{
    public const double Floor = 0.4;

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
