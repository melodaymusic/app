using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Lyrics;

public static class LyricWords
{
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
        return words.Count >= 2 && words.Zip(words.Skip(1), (a, b) => b.Start > a.Start).All(x => x) ? words : null;
    }
}
