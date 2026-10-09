using System.Text.Json;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Lyrics;

/// <summary>A word the listener timed in the Lyric Studio.</summary>
public sealed class EditedWord
{
    public string Text { get; set; } = string.Empty;
    public long StartMs { get; set; }

    /// <summary>When the word stopped being held, for a word held on a long note (the space bar held down); null for an ordinary word.</summary>
    public long? EndMs { get; set; }
}

/// <summary>A line the listener timed in the Lyric Studio; <see cref="Words"/> is null when only the line was timed.</summary>
public sealed class EditedLine
{
    public string Text { get; set; } = string.Empty;
    public long StartMs { get; set; }
    public long? EndMs { get; set; }
    public List<EditedWord>? Words { get; set; }
}

/// <summary>
/// What the listener has done to a song's lyrics on this computer: shifted all their timing (<see cref="OffsetMs"/>, for a
/// version of the song the timing wasn't made for) and/or timed them themselves in the Lyric Studio (<see cref="Lines"/>,
/// which then stand in for whatever Plex or LRCLIB had). Never sent anywhere.
/// </summary>
public sealed class LyricEdit
{
    public int OffsetMs { get; set; }
    public List<EditedLine>? Lines { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => OffsetMs == 0 && (Lines is null || Lines.Count == 0);

    /// <summary>The song's lyrics with the edit applied: the listener's own timing if there is any, and then the shift.</summary>
    public static TrackLyrics? Apply(TrackLyrics? original, LyricEdit? edit)
    {
        if (edit is null || edit.IsEmpty) return original;
        var lyrics = original;
        if (edit.Lines is { Count: > 0 } own)
        {
            lyrics = new TrackLyrics(own.Select(l => new LyricLine(TimeSpan.FromMilliseconds(l.StartMs), l.EndMs is { } end ? TimeSpan.FromMilliseconds(end) : null, l.Text)
            {
                Words = l.Words is { Count: > 0 } words
                    ? words.Select(w => new LyricWord(TimeSpan.FromMilliseconds(w.StartMs), w.Text) { End = w.EndMs is { } end ? TimeSpan.FromMilliseconds(end) : null }).ToList()
                    : null,
            }).ToList())
            { Source = LyricsSource.Custom };
        }
        if (lyrics is not { IsSynced: true } || edit.OffsetMs == 0) return lyrics;

        var shift = TimeSpan.FromMilliseconds(edit.OffsetMs);
        TimeSpan? Move(TimeSpan? t) => t is { } at ? TimeSpan.FromTicks(Math.Max(0, (at + shift).Ticks)) : null;
        return new TrackLyrics(lyrics.Lines.Select(l => new LyricLine(Move(l.Start), Move(l.End), l.Text)
        {
            Words = l.Words?.Select(w => new LyricWord(Move(w.Start)!.Value, w.Text) { End = Move(w.End) }).ToList(),
        }).ToList())
        { Source = lyrics.Source, IsInstrumental = lyrics.IsInstrumental };
    }
}

/// <summary>
/// The listener's lyric edits, one small file per song in the app's own folder. "Clearing" an edit writes it empty rather than
/// removing the file, so nothing is ever deleted.
/// </summary>
public sealed class LyricEdits(string folder)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private string PathFor(string key) => Path.Combine(folder, string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')) + ".json");

    /// <summary>The song's edit, or null when it has none.</summary>
    public LyricEdit? Get(string key)
    {
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<LyricEdit>(File.ReadAllText(path), Json) is { IsEmpty: false } edit ? edit : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps the song's edit (an empty one is how an edit is taken away). Returns whether it could be saved.</summary>
    public bool Set(string key, LyricEdit edit)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(PathFor(key), JsonSerializer.Serialize(edit, Json));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool Clear(string key) => Set(key, new LyricEdit());
}
