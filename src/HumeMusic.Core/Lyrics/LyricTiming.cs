using HumeMusic.Core.Models;

namespace HumeMusic.Core.Lyrics;

/// <summary>A word of a sung line, when (seconds into the line) it is sung and, for a word held on a long note, how long it is held.</summary>
public readonly record struct TimedWord(string Text, double Start, double Hold = 0);

/// <summary>
/// When each word of a line is sung, for the lyric visualizers. Lyrics are timed by the line, not the word, so a
/// line's words are spread over the time it takes to sing it, each word's share following its syllables (the last word
/// of a line is held a little, and a comma or full stop leaves a pause); a line that sits in a long gap (an instrumental
/// break before the next) isn't stretched across the gap, since singing takes about as long as saying it, a little
/// slower, but a line in a slow song does fill the time it has, as a ballad's lines do.
/// </summary>
public static class LyricTiming
{
    /// <summary>The longest a line is taken to be sung in, per word and as a starting allowance (seconds).</summary>
    private const double PerWord = 0.42, Allowance = 0.5;

    /// <summary>The slowest a song's words are taken to be sung, per word (seconds): a line with more time than this fills its gap only up to here.</summary>
    private const double SlowestPerWord = 0.7;

    /// <summary>How long a line takes to sing, given the time it has before the next line starts.</summary>
    public static double SingingSeconds(int words, double available)
    {
        var natural = words * PerWord + Allowance;
        // In a slow song a line fills the time it has (up to the slowest pace words are sung at); a break isn't filled.
        var filled = Math.Min(available * 0.9, words * SlowestPerWord);
        return Math.Max(0.2, Math.Min(available * 0.92, Math.Max(natural, filled)));
    }

    /// <summary>The words of a line all beginning together (for lyrics with no timing for each word: the line arrives whole).</summary>
    public static IReadOnlyList<TimedWord> Whole(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => new TimedWord(w, 0)).ToList();

    /// <summary>The words of a line, each with the time it begins, over <paramref name="available"/> seconds (an estimate, kept for tests and callers that want one).</summary>
    public static IReadOnlyList<TimedWord> Words(string text, double available)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var span = SingingSeconds(words.Length, available);
        // A word takes time in proportion to its syllables (and a little for itself, so "I" and "a" aren't instant); the last
        // is held, and a word that ends in a comma or full stop is followed by a pause.
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

    /// <summary>A word that ends a phrase: a comma, full stop, question or exclamation mark, semicolon, colon or dash.</summary>
    private static bool EndsWithPause(string word)
    {
        var trimmed = word.TrimEnd(Closers);
        return trimmed.Length > 0 && ",.!?;:—".Contains(trimmed[^1]);
    }

    /// <summary>The seconds a line has: until the next line starts, or its own end, or (for the last) a few seconds.</summary>
    public static double Available(TrackLyrics lyrics, int index)
    {
        var line = lyrics.Lines[index];
        if (line.Start is not { } start) return 4;
        if (index + 1 < lyrics.Lines.Count && lyrics.Lines[index + 1].Start is { } next) return Math.Max(0.3, (next - start).TotalSeconds);
        return line.End is { } end ? Math.Max(0.3, (end - start).TotalSeconds) : 5;
    }
}
