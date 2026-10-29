namespace MelodayMusic.Core.Lyrics;

public enum LyricFont
{
    Nunito,
}

public sealed record LyricType(LyricFont Font, string Family, string File, int LightestWeight, int HeaviestWeight, string SystemFallback)
{
    private static readonly (int Weight, string File)[] Faces =
    [
        (400, "Nunito.ttf"), (500, "Nunito.ttf"),
        (700, "Nunito.ttf"), (900, "Nunito.ttf"),
    ];

    public (string File, string Family, int Weight) Resolve(int weight)
    {
        var face = Faces.OrderBy(f => Math.Abs(f.Weight - weight)).ThenBy(f => f.Weight).First();
        return (face.File, Family, face.Weight);
    }

    public IEnumerable<string> Files => Faces.Select(f => f.File).Append(File).Distinct();
}

public static class LyricFonts
{
    public static IReadOnlyList<LyricType> All { get; } =
    [
        new(LyricFont.Nunito, "Nunito", "Nunito.ttf", 400, 900, "Segoe UI Variable Display"),
    ];

    public static LyricType For(LyricFont font) => All[(int)font];

    public static int Weight(LyricFont font, double energy)
    {
        var type = For(font);
        var raw = type.LightestWeight + (type.HeaviestWeight - type.LightestWeight) * Math.Clamp(energy, 0, 1);
        return type.Resolve((int)Math.Round(raw)).Weight;
    }

    public static double Energy(double level) => Math.Clamp((level - 0.1) / 0.45, 0, 1);
}
