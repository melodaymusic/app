using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Lyrics;

public readonly record struct TimedWord(string Text, double Start, double Hold = 0);

public static class LyricTiming
{
    private const double PerWord = 0.42, Allowance = 0.5;

    private const double SlowestPerWord = 0.7;

    public static double SingingSeconds(int words, double available)
    {
        var natural = words * PerWord + Allowance;
        var filled = Math.Min(available * 0.9, words * SlowestPerWord);
        return Math.Max(0.2, Math.Min(available * 0.92, Math.Max(natural, filled)));
    }

    public static IReadOnlyList<TimedWord> Whole(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => new TimedWord(w, 0)).ToList();

    public static IReadOnlyList<TimedWord> Words(string text, double available)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var span = SingingSeconds(words.Length, available);
        var weights = words.Select(w => Math.Max(1, LyricSyllables.Count(w)) + 0.4).ToArray();
        weights[^1] *= 1.35;
        var pauses = words.Select((w, i) => i < words.Length - 1 && EndsWithPause(w) ? 0.7 : 0).ToArray();
        var total = weights.Sum() + pauses.Sum();
        var timed = new TimedWord[words.Length];
        double so_far = 0;
        for (var i = 0; i < words.Length; i++)
        {
            timed[i] = new TimedWord(words[i], span * so_far / total);
            so_far += weights[i] + pauses[i];
        }
        return timed;
    }

    private static readonly char[] Closers = ['"', '\'', ')', '”', '’'];

    private static bool EndsWithPause(string word)
    {
        var trimmed = word.TrimEnd(Closers);
        return trimmed.Length > 0 && ",.!?;:—".Contains(trimmed[^1]);
    }

    public static double Available(TrackLyrics lyrics, int index)
    {
        var line = lyrics.Lines[index];
        if (line.Start is not { } start) return 4;
        if (index + 1 < lyrics.Lines.Count && lyrics.Lines[index + 1].Start is { } next) return Math.Max(0.3, (next - start).TotalSeconds);
        return line.End is { } end ? Math.Max(0.3, (end - start).TotalSeconds) : 5;
    }
}
