using MelodayMusic.Core.Lyrics;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class LyricStudioTests
{
    [Theory]
    [InlineData("I", 1)]
    [InlineData("love", 1)]
    [InlineData("loved", 1)]
    [InlineData("wanted", 2)]
    [InlineData("little", 2)]
    [InlineData("morning", 2)]
    [InlineData("beautiful", 3)]
    [InlineData("forever", 3)]
    [InlineData("dancing", 2)]
    [InlineData("2", 1)]
    [InlineData("don't", 1)]
    public void Syllables_are_counted_roughly(string word, int expected) => Assert.Equal(expected, LyricSyllables.Count(word));

    [Fact]
    public void A_long_word_takes_longer_to_sing_than_a_short_one_of_the_same_letters()
    {
        var words = LyricTiming.Words("strengths a beautiful night", 6);
        var gaps = words.Select((w, i) => (i < words.Count - 1 ? words[i + 1].Start : 6) - w.Start).ToArray();
        Assert.True(gaps[2] > gaps[1] * 1.8);
    }

    [Fact]
    public void A_comma_leaves_a_pause_and_the_last_word_is_held()
    {
        var plain = LyricTiming.Words("hold me tight tonight", 4);
        var comma = LyricTiming.Words("hold me, tight tonight", 4);
        Assert.True(comma[2].Start > plain[2].Start);
        var held = plain.Select((w, i) => (i < plain.Count - 1 ? plain[i + 1].Start : LyricTiming.SingingSeconds(4, 4)) - w.Start).ToArray();
        Assert.True(held[3] > held[1]);
    }

    [Fact]
    public void A_line_in_a_slow_song_fills_the_time_it_has_but_a_break_is_not_filled()
    {
        Assert.InRange(LyricTiming.SingingSeconds(6, 5), 4, 4.6);
        Assert.InRange(LyricTiming.SingingSeconds(4, 20), 1.5, 3);
        Assert.True(LyricTiming.SingingSeconds(14, 3) <= 3);
    }

    private static TrackLyrics Synced() => new(
    [
        new LyricLine(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), "One two"),
        new LyricLine(TimeSpan.FromSeconds(14), null, "Three four")
        {
            Words = [new LyricWord(TimeSpan.FromSeconds(14), "Three"), new LyricWord(TimeSpan.FromSeconds(15), "four")],
        },
    ])
    { Source = LyricsSource.LrcLib };

    [Fact]
    public void An_offset_shifts_every_line_and_word_and_never_goes_before_the_start()
    {
        var shifted = LyricEdit.Apply(Synced(), new LyricEdit { OffsetMs = 500 })!;
        Assert.Equal(TimeSpan.FromSeconds(10.5), shifted.Lines[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(14.5), shifted.Lines[0].End);
        Assert.Equal(TimeSpan.FromSeconds(15.5), shifted.Lines[1].Words![1].Start);
        Assert.Equal(LyricsSource.LrcLib, shifted.Source);
        Assert.True(shifted.IsSynced);

        var early = LyricEdit.Apply(Synced(), new LyricEdit { OffsetMs = -12000 })!;
        Assert.Equal(TimeSpan.Zero, early.Lines[0].Start);
    }

    [Fact]
    public void No_edit_or_an_empty_one_leaves_the_lyrics_as_they_are()
    {
        var lyrics = Synced();
        Assert.Same(lyrics, LyricEdit.Apply(lyrics, null));
        Assert.Same(lyrics, LyricEdit.Apply(lyrics, new LyricEdit()));
        Assert.Null(LyricEdit.Apply(null, new LyricEdit { OffsetMs = 100 }));
    }

    [Fact]
    public void Lyrics_timed_by_the_listener_stand_in_for_the_originals_and_still_take_the_offset()
    {
        var edit = new LyricEdit
        {
            OffsetMs = 100,
            Lines = [new EditedLine { Text = "Hello world", StartMs = 2000, EndMs = 5000, Words = [new EditedWord { Text = "Hello", StartMs = 2000 }, new EditedWord { Text = "world", StartMs = 2600 }] }],
        };
        var result = LyricEdit.Apply(Synced(), edit)!;
        Assert.Equal(LyricsSource.Custom, result.Source);
        Assert.Single(result.Lines);
        Assert.Equal(TimeSpan.FromMilliseconds(2100), result.Lines[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(2700), result.Lines[0].Words![1].Start);
        Assert.True(result.IsSynced);
    }

    [Fact]
    public void Edits_are_kept_per_song_and_clearing_one_empties_it()
    {
        var folder = Path.Combine(Path.GetTempPath(), "meloday-lyric-edits-" + Guid.NewGuid().ToString("N"));
        var store = new LyricEdits(folder);
        Assert.Null(store.Get("abc-123"));
        Assert.True(store.Set("abc-123", new LyricEdit { OffsetMs = -250 }));
        Assert.Equal(-250, store.Get("abc-123")!.OffsetMs);
        Assert.Null(store.Get("other"));
        Assert.True(store.Clear("abc-123"));
        Assert.Null(store.Get("abc-123"));
        Assert.True(File.Exists(Path.Combine(folder, "abc-123.json")));
    }

    private static readonly TimeSpan Late = StudioSession.ReactionTime;

    [Fact]
    public void Tapping_through_words_times_each_word_and_lines_end_where_the_next_begins()
    {
        var session = new StudioSession("Hello there\n\nGeneral Kenobi", StudioMode.Words);
        Assert.Equal(4, session.UnitCount);
        Assert.Equal("Hello", session.Next);
        foreach (var s in new[] { 1.0, 1.5, 4.0, 4.6 }) Assert.True(session.Tap(TimeSpan.FromSeconds(s) + Late));
        Assert.True(session.IsComplete);
        Assert.Null(session.Next);

        var edit = session.ToEdit();
        Assert.Equal(2, edit.Lines!.Count);
        Assert.Equal(1000, edit.Lines[0].StartMs);
        Assert.Equal(4000, edit.Lines[0].EndMs);
        Assert.Null(edit.Lines[1].EndMs);
        Assert.Equal(["Hello", "there"], edit.Lines[0].Words!.Select(w => w.Text));
        Assert.Equal(1500, edit.Lines[0].Words![1].StartMs);
        var lyrics = LyricEdit.Apply(null, edit)!;
        Assert.True(lyrics.IsSynced);
        Assert.Equal(TimeSpan.FromMilliseconds(4600), lyrics.Lines[1].Words![1].Start);
    }

    [Fact]
    public void A_word_the_key_is_held_through_is_a_held_word_and_a_short_press_is_not()
    {
        var session = new StudioSession("Hold me close tonight", StudioMode.Words);
        Assert.True(session.Tap(TimeSpan.FromSeconds(1) + Late));
        Assert.False(session.Release(TimeSpan.FromSeconds(1.1) + Late));
        Assert.True(session.Tap(TimeSpan.FromSeconds(1.5) + Late));
        Assert.True(session.Release(TimeSpan.FromSeconds(3) + Late));
        Assert.False(session.Release(TimeSpan.FromSeconds(3.2) + Late));
        session.Tap(TimeSpan.FromSeconds(3.4) + Late);
        session.Tap(TimeSpan.FromSeconds(3.9) + Late);

        var words = session.ToEdit().Lines![0].Words!;
        Assert.Null(words[0].EndMs);
        Assert.Equal(3000, words[1].EndMs);
        Assert.Null(words[2].EndMs);
        var lyrics = LyricEdit.Apply(null, session.ToEdit())!;
        Assert.Equal(TimeSpan.FromMilliseconds(3000), lyrics.Lines[0].Words![1].End);
        var shifted = LyricEdit.Apply(null, new LyricEdit { OffsetMs = 200, Lines = session.ToEdit().Lines })!;
        Assert.Equal(TimeSpan.FromMilliseconds(3200), shifted.Lines[0].Words![1].End);
    }

    [Fact]
    public void Going_back_forgets_the_held_words_after_the_point_and_a_line_tap_has_no_length()
    {
        var session = new StudioSession("a b c d", StudioMode.Words);
        session.Tap(TimeSpan.FromSeconds(1) + Late);
        session.Tap(TimeSpan.FromSeconds(2) + Late);
        session.Release(TimeSpan.FromSeconds(3) + Late);
        session.RewindTo(TimeSpan.FromSeconds(1.5));
        Assert.Equal(1, session.Tapped);
        session.Tap(TimeSpan.FromSeconds(2) + Late);
        session.Tap(TimeSpan.FromSeconds(2.5) + Late);
        session.Tap(TimeSpan.FromSeconds(3) + Late);
        Assert.All(session.ToEdit().Lines![0].Words!, w => Assert.Null(w.EndMs));

        var lines = new StudioSession("one\ntwo", StudioMode.Lines);
        lines.Tap(TimeSpan.FromSeconds(1));
        Assert.False(lines.Release(TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void Lines_are_split_on_any_line_ending_a_text_box_gives()
    {
        Assert.Equal(["One", "Two", "Three"], new StudioSession("One\rTwo\r\nThree\n", StudioMode.Lines).Lines);
    }

    [Fact]
    public void Tapping_by_line_times_the_lines_only()
    {
        var session = new StudioSession("One\nTwo\nThree", StudioMode.Lines);
        Assert.Equal(3, session.UnitCount);
        session.Tap(TimeSpan.FromSeconds(2));
        session.Tap(TimeSpan.FromSeconds(5));
        var edit = session.ToEdit();
        Assert.Equal(2, edit.Lines!.Count);
        Assert.All(edit.Lines, l => Assert.Null(l.Words));
    }

    [Fact]
    public void A_line_only_part_tapped_is_not_kept_and_a_bounce_is_ignored()
    {
        var session = new StudioSession("One two three\nFour five", StudioMode.Words);
        Assert.True(session.Tap(TimeSpan.FromSeconds(1)));
        Assert.False(session.Tap(TimeSpan.FromSeconds(1).Add(TimeSpan.FromMilliseconds(10))));
        Assert.True(session.Tap(TimeSpan.FromSeconds(2)));
        Assert.Empty(session.ToEdit().Lines!);
        Assert.True(session.Tap(TimeSpan.FromSeconds(3)));
        Assert.Single(session.ToEdit().Lines!);
    }

    [Fact]
    public void Undo_and_going_back_in_the_song_drop_taps_to_be_made_again()
    {
        var session = new StudioSession("a b c d e f", StudioMode.Words);
        foreach (var s in new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }) session.Tap(TimeSpan.FromSeconds(s) + Late);
        Assert.Equal(5, session.Tapped);
        Assert.True(session.Undo());
        Assert.Equal("e", session.Next);
        session.RewindTo(TimeSpan.FromSeconds(3));
        Assert.Equal(2, session.Tapped);
        Assert.Equal("c", session.Next);
        session.Reset();
        Assert.Equal(0, session.Tapped);
        Assert.False(session.Undo());
    }
}
