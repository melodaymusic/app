using HumeMusic.Core.Audio;
using HumeMusic.Core.Lyrics;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Tests;

public class LyricVisualTests
{
    [Fact]
    public void A_lines_words_are_spread_over_the_time_it_takes_to_sing_it_in_order()
    {
        var words = LyricTiming.Words("I walked into the morning light", 6);
        Assert.Equal(6, words.Count);
        Assert.Equal(0, words[0].Start);
        Assert.True(words.Zip(words.Skip(1)).All(p => p.Second.Start > p.First.Start));
        Assert.True(words[^1].Start < LyricTiming.SingingSeconds(6, 6));
        // Longer words take longer: "morning" lasts longer than "I".
        var span = LyricTiming.SingingSeconds(6, 6);
        var gaps = words.Select((w, i) => (i < words.Count - 1 ? words[i + 1].Start : span) - w.Start).ToArray();
        Assert.True(gaps[4] > gaps[0]);
    }

    [Fact]
    public void A_line_in_a_long_gap_is_not_stretched_across_it()
    {
        // Four words with twenty seconds before the next line: sung in a couple of seconds, not twenty.
        Assert.InRange(LyricTiming.SingingSeconds(4, 20), 1.5, 3);
        // A short line with little time takes the time it has (less a little).
        Assert.True(LyricTiming.SingingSeconds(12, 2) <= 2);
        Assert.True(LyricTiming.SingingSeconds(1, 0.1) >= 0.2);
    }

    [Fact]
    public void Words_are_split_on_any_whitespace_and_blank_lines_have_none()
    {
        Assert.Equal(["Hello", "there,", "world"], LyricTiming.Words("  Hello   there,\tworld ", 4).Select(w => w.Text));
        Assert.Empty(LyricTiming.Words("   ", 4));
    }

    [Fact]
    public void The_time_a_line_has_runs_to_the_next_line_or_its_own_end()
    {
        var lyrics = new TrackLyrics(
        [
            new LyricLine(TimeSpan.FromSeconds(10), null, "one"),
            new LyricLine(TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(19), "two"),
            new LyricLine(TimeSpan.FromSeconds(30), null, "three"),
        ]);
        Assert.Equal(4, LyricTiming.Available(lyrics, 0));
        Assert.Equal(16, LyricTiming.Available(lyrics, 1));
        Assert.Equal(5, LyricTiming.Available(lyrics, 2));
    }

    /// <summary>A bass that hits every <paramref name="period"/> seconds (a short thump) sampled at 60 per second.</summary>
    private static BeatTracker Track(double period, double seconds)
    {
        var beat = new BeatTracker();
        for (var i = 0; i < seconds * 60; i++)
        {
            var t = i / 60.0;
            var phase = t % period;
            beat.Update(phase < 0.07 ? 0.85 : 0.12, t);
        }
        return beat;
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.6)]
    [InlineData(0.4)]
    public void The_tempo_is_found_from_the_bass_hits(double period)
    {
        var beat = Track(period, 8);
        Assert.InRange(beat.Period, period * 0.93, period * 1.07);
    }

    [Fact]
    public void Before_there_is_any_beat_the_period_is_a_plain_half_second_and_the_pulse_dies_away()
    {
        var beat = new BeatTracker();
        Assert.Equal(0.5, beat.Period);
        Assert.True(beat.Pulse(5) < 0.01);
        var tracked = Track(0.5, 4);
        var last = tracked.LastHit;
        Assert.True(tracked.Pulse(last) > 0.99);
        Assert.True(tracked.Pulse(last + 0.5) < 0.1);
    }

    [Fact]
    public void A_steady_loud_bass_with_no_hits_gives_no_beat()
    {
        var beat = new BeatTracker();
        var hits = 0;
        for (var i = 0; i < 600; i++) if (beat.Update(0.7, i / 60.0)) hits++;
        // Only the first rise out of silence counts, never a sustained note.
        Assert.True(hits <= 1);
    }

    // ------------------------------------------------------------------ typefaces

    [Fact]
    public void Lyrics_are_set_in_manrope_alone_and_louder_music_is_set_heavier()
    {
        Assert.Equal(["Manrope"], LyricFonts.All.Select(f => f.Family));
        Assert.Equal(400, LyricFonts.Weight(LyricFont.Manrope, 0));
        Assert.Equal(800, LyricFonts.Weight(LyricFont.Manrope, 1));
        Assert.True(LyricFonts.Weight(LyricFont.Manrope, 0.5) is 600 or 700);
        // Never outside what the face has.
        foreach (var e in new[] { -1.0, 0.0, 0.3, 0.7, 1.0, 2.0 })
            Assert.InRange(LyricFonts.Weight(LyricFont.Manrope, e), 400, 800);
        Assert.True(LyricFonts.Energy(0.05) == 0 && LyricFonts.Energy(0.9) == 1);
        // A variable face takes any weight asked for.
        Assert.Equal(("Manrope-Variable.ttf", "Manrope", 650), LyricFonts.For(LyricFont.Manrope).Resolve(650));
    }

    [Fact]
    public void The_face_has_its_file_in_the_package_and_no_other_font_is_left_over()
    {
        var fonts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HumeMusic.App", "Assets", "Fonts"));
        foreach (var file in LyricFonts.All.SelectMany(t => t.Files)) Assert.True(File.Exists(Path.Combine(fonts, file)), file);
        Assert.Equal(["Manrope-Variable.ttf"], Directory.GetFiles(fonts, "*.ttf").Select(Path.GetFileName));
    }
}
