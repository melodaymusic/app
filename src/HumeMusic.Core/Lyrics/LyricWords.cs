using HumeMusic.Core.Models;

namespace HumeMusic.Core.Lyrics;

/// <summary>Reading the words of a line, with their times, from the spans a lyrics source gives (when it times them).</summary>
public static class LyricWords
{
    /// <summary>
    /// The words of a line with their own times, or null when the spans don't carry them (a single span, or any without a
    /// time): spans are joined into words where the text has no space between them, so a word sung in two parts is one.
    /// </summary>
    public static IReadOnlyList<LyricWord>? From(IReadOnlyList<(string? Text, long? Start)>? spans)
    {
        if (spans is null || spans.Count < 2 || spans.Any(s => s.Start is null || string.IsNullOrEmpty(s.Text))) return null;
        var words = new List<LyricWord>();
        var text = string.Empty;
        long start = 0;
        foreach (var (raw, at) in spans)
        {
            var span = raw!;
            if (text.Length > 0 && char.IsWhiteSpace(span[0]))
            {
                words.Add(new LyricWord(TimeSpan.FromMilliseconds(start), text));
                text = string.Empty;
            }
            if (text.Length == 0) start = at!.Value;
            text += span.Trim();
            if (char.IsWhiteSpace(span[^1]) && text.Length > 0)
            {
                words.Add(new LyricWord(TimeSpan.FromMilliseconds(start), text));
                text = string.Empty;
            }
        }
        if (text.Length > 0) words.Add(new LyricWord(TimeSpan.FromMilliseconds(start), text));
        // Times that don't move forward aren't word timing (some sources give every span the line's time).
        return words.Count >= 2 && words.Zip(words.Skip(1), (a, b) => b.Start > a.Start).All(x => x) ? words : null;
    }
}
