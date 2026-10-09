namespace HumeMusic.Core.Lyrics;

/// <summary>The typeface the lyric videos are set in. Manrope only: the app's own face, one variable font file.</summary>
public enum LyricFont
{
    Manrope,
}

/// <summary>What a typeface is, where its file is, and the weights it has and the lyric videos use (one variable font file, any weight asked for).</summary>
public sealed record LyricType(LyricFont Font, string Family, string File, int LightestWeight, int HeaviestWeight, string SystemFallback)
{
    /// <summary>The file, the family name to ask Windows for and the weight to ask for, to set text at about <paramref name="weight"/>.</summary>
    public (string File, string Family, int Weight) Resolve(int weight) => (File, Family, weight);

    /// <summary>Every file this face needs.</summary>
    public IEnumerable<string> Files => [File];
}

/// <summary>The lyric videos' typeface, and how heavy a line is set: heavier type follows louder music.</summary>
public static class LyricFonts
{
    public static IReadOnlyList<LyricType> All { get; } =
    [
        new(LyricFont.Manrope, "Manrope", "Manrope-Variable.ttf", 400, 800, "Segoe UI Variable Display"),
    ];

    public static LyricType For(LyricFont font) => All[(int)font];

    /// <summary>
    /// How heavy to set a line: from the lightest weight the face uses for quiet music to its heaviest for loud
    /// (<paramref name="energy"/> 0-1), to the nearest hundred.
    /// </summary>
    public static int Weight(LyricFont font, double energy)
    {
        var type = For(font);
        var raw = type.LightestWeight + (type.HeaviestWeight - type.LightestWeight) * Math.Clamp(energy, 0, 1);
        return (int)Math.Round(raw / 100.0) * 100;
    }

    /// <summary>How loud a line is, from the analysed level (typically 0.1 for quiet to 0.6 for loud), as 0-1.</summary>
    public static double Energy(double level) => Math.Clamp((level - 0.1) / 0.45, 0, 1);
}
