namespace MelodayMusic.Core.Meloday;

public sealed record SunRing(double Size, uint Colour, double Opacity, double From, double To, double Seconds, double Delay);

public sealed record MelodaySunStyle(uint Background, double X, double Y, IReadOnlyList<SunRing> Rings)
{
    public const double SunSize = 0.8;
    public static readonly (double X1, double Y1, double X2, double Y2) Ease = (0.37, 0, 0.63, 1);
}

public static class MelodaySun
{
    private static readonly (double Size, double From, double To, double Seconds, double Delay)[] Breaths =
    [
        (1.00, 0.960, 1.040, 8.8, 1.4),
        (0.75, 0.972, 1.034, 7.6, 3.1),
        (0.50, 0.982, 1.030, 9.5, 5.2),
        (0.25, 0.990, 1.024, 7.9, 2.6),
    ];

    private static readonly double[] Opacities = [0.30, 0.40, 0.65, 1.0];

    private static MelodaySunStyle Sun(uint background, uint sun, double x, double y) =>
        new(background, x, y, Breaths.Select((b, i) => new SunRing(b.Size, sun, Opacities[i], b.From, b.To, b.Seconds, b.Delay)).ToList());

    private static readonly Dictionary<string, MelodaySunStyle> Styles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dawn"] = Sun(0x2D2350, 0xFF6E4B, 0.5, 1.15),
        ["Early Morning"] = new(0xA77ECD, 0.5, 0.9,
        [
            new(1.00, 0xBC85B2, 1, 0.960, 1.040, 8.8, 1.4),
            new(0.75, 0xD39193, 1, 0.972, 1.034, 7.6, 3.1),
            new(0.50, 0xE29A6F, 1, 0.982, 1.030, 9.5, 5.2),
            new(0.25, 0xEDA158, 1, 0.990, 1.024, 7.9, 2.6),
        ]),
        ["Morning"] = Sun(0x3CA5E1, 0xFFC800, 0.5, 0.5),
        ["Afternoon"] = Sun(0x1E5ADC, 0xFFC800, 0.5, 0.1),
        ["Evening"] = Sun(0x873769, 0xFF6E4B, 0.5, 0.9),
        ["Night"] = Sun(0x4B4178, 0xA0E1FF, 0.5, 0.236),
        ["Late Night"] = Sun(0x0A3228, 0x739BF5, 0.799, 0.212),
    };

    public static MelodaySunStyle For(string? period) =>
        period is not null && Styles.TryGetValue(period, out var style) ? style : Styles["Morning"];
}
