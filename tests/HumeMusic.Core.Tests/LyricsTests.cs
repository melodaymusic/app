using HumeMusic.Core.Models;

namespace HumeMusic.Core.Tests;

public class LyricsTests
{
    private static LyricLine Line(double? start, string text) => new(start is { } s ? TimeSpan.FromSeconds(s) : null, null, text);

    [Fact]
    public void The_line_being_sung_is_the_last_one_started()
    {
        var lyrics = new TrackLyrics([Line(5, "one"), Line(9, "two"), Line(14, "three")]);
        Assert.True(lyrics.IsSynced);
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, lyrics.IndexAt(TimeSpan.FromSeconds(13.9)));
        Assert.Equal(2, lyrics.IndexAt(TimeSpan.FromSeconds(300)));
    }

    [Fact]
    public void Plain_lyrics_never_follow_the_song()
    {
        var lyrics = new TrackLyrics([Line(null, "one"), Line(null, "two")]);
        Assert.False(lyrics.IsSynced);
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(10)));
    }
}
