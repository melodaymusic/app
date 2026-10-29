using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class CrossfadeTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Theory]
    [InlineData(200, 193, false)]
    [InlineData(200, 194.5, true)]
    [InlineData(200, 150, false)]
    [InlineData(200, 200, false)]
    [InlineData(15, 10, false)]
    public void Starts_only_inside_the_fade_window_of_long_enough_tracks(double duration, double position, bool expected)
    {
        Assert.Equal(expected, Crossfade.ShouldStart(S(position), S(duration), S(6)));
    }

    [Fact]
    public void Gains_cross_with_constant_power()
    {
        foreach (var t in new[] { 0, 0.25, 0.5, 0.75, 1 })
        {
            var (o, i) = Crossfade.Gains(t);
            Assert.Equal(1, o * o + i * i, 6);
        }
        Assert.Equal((1, 0), Crossfade.Gains(0));
        var end = Crossfade.Gains(1);
        Assert.True(end.Outgoing < 1e-9 && Math.Abs(end.Incoming - 1) < 1e-9);
    }

    [Fact]
    public void Length_is_clamped_to_the_supported_range()
    {
        Assert.Equal(S(Crossfade.MinSeconds), Crossfade.Length(0));
        Assert.Equal(S(Crossfade.MaxSeconds), Crossfade.Length(60));
        Assert.Equal(S(6), Crossfade.Length(6));
    }
}

public class AutoMixTests
{
    private static TrackLoudness Song(Func<int, double> level, int points = 128, double? gain = null, VocalSpan? vocals = null) =>
        new(Enumerable.Range(0, points).Select(level).ToList(), gain, vocals);

    private static readonly TimeSpan Length = TimeSpan.FromSeconds(256);

    private static double FadesOut(int i) => i < 118 ? -10 : i < 124 ? -10 - (i - 117) * 4 : -40;
    private static double ColdEnd(int i) => i < 126 ? -9 : -40;
    private static double Loud(int _) => -10;

    private static VocalSpan Vocals(double first, double last) => new(TimeSpan.FromSeconds(first), TimeSpan.FromSeconds(last));

    [Fact]
    public void A_song_that_fades_out_hands_over_as_its_fade_begins()
    {
        var plan = AutoMix.Plan(Song(FadesOut), Length, Song(Loud), Length)!;
        Assert.InRange(plan.StartAt.TotalSeconds, 234, 241);
        Assert.InRange(plan.Fade.TotalSeconds, 6, 14);
        Assert.Equal(248, (plan.StartAt + plan.Fade).TotalSeconds, 0.01);
    }

    [Fact]
    public void A_cold_ending_into_a_loud_start_is_a_tight_cut()
    {
        var plan = AutoMix.Plan(Song(ColdEnd), Length, Song(Loud), Length)!;
        Assert.True(plan.Fade <= TimeSpan.FromSeconds(1));
        Assert.Equal(252, (plan.StartAt + plan.Fade).TotalSeconds, 0.01);
        Assert.True(plan.Shape.InFade <= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_quiet_intro_comes_in_under_the_end_of_the_last_song()
    {
        var quietIntro = Song(i => i < 6 ? -30 : -10);
        var plan = AutoMix.Plan(Song(ColdEnd), Length, quietIntro, Length)!;
        Assert.InRange(plan.Fade.TotalSeconds, 8, 13);
        Assert.Equal(AutoMix.QuickestRise, plan.Shape.InFade);
        Assert.True(plan.Shape.OutFadeStart > TimeSpan.Zero);
    }

    [Fact]
    public void Two_voices_never_sing_over_each_other()
    {
        var plain = AutoMix.Plan(Song(FadesOut), Length, Song(Loud), Length)!;
        var sung = AutoMix.Plan(Song(FadesOut, vocals: Vocals(10, 246)), Length, Song(Loud, vocals: Vocals(0.5, 200)), Length)!;
        Assert.True(sung.Fade < plain.Fade);
    }

    [Fact]
    public void The_next_song_starts_at_its_first_sound()
    {
        var incoming = Song(i => i < 2 ? -40 : -12);
        Assert.Equal(4, AutoMix.LeadIn(incoming, Length).TotalSeconds, 0.01);
        Assert.Equal(TimeSpan.Zero, AutoMix.LeadIn(Song(_ => -12), Length));
        Assert.Equal(TimeSpan.Zero, AutoMix.LeadIn(Song(i => i < 10 ? -40 : -12), Length));
    }

    [Fact]
    public void An_unanalysed_next_song_is_taken_to_start_loud()
    {
        var plan = AutoMix.Plan(Song(ColdEnd), Length, null, Length)!;
        Assert.True(plan.Fade <= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Without_analysis_there_is_no_plan() =>
        Assert.Null(AutoMix.Plan(null, Length, null, Length));

    [Fact]
    public void Shapes_rise_hold_and_fall()
    {
        var shape = new CrossfadeShape(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(3));
        Assert.Equal((1, 0), shape.Gains(TimeSpan.Zero));
        Assert.Equal(1, shape.Gains(TimeSpan.FromSeconds(2)).Incoming, 6);
        Assert.Equal(1, shape.Gains(TimeSpan.FromSeconds(6.9)).Outgoing, 6);
        Assert.Equal(0, shape.Gains(TimeSpan.FromSeconds(10)).Outgoing, 6);
        var even = CrossfadeShape.Even(TimeSpan.FromSeconds(4));
        var (o, i) = even.Gains(TimeSpan.FromSeconds(2));
        Assert.Equal(1, o * o + i * i, 6);
    }

    [Theory]
    [InlineData("a", 1, 3, "a", 1, 4, true)]
    [InlineData("a", 1, 12, "a", 2, 1, true)]
    [InlineData("a", null, 3, "a", null, 4, true)]
    [InlineData("a", 1, 3, "a", 1, 5, false)]
    [InlineData("a", 1, 4, "a", 1, 3, false)]
    [InlineData("a", 1, 3, "b", 1, 4, false)]
    [InlineData(null, 1, 3, null, 1, 4, false)]
    public void Albums_in_order_are_left_as_they_are(string? album, int? disc, int? number, string? nextAlbum, int? nextDisc, int? nextNumber, bool expected) =>
        Assert.Equal(expected, AutoMix.ContinuesAlbum(album, disc, number, nextAlbum, nextDisc, nextNumber));

    [Fact]
    public void Sound_check_only_turns_songs_down()
    {
        Assert.Equal(1, AutoMix.LevelFor(Song(_ => -10, gain: 3)));
        Assert.Equal(Math.Pow(10, -6 / 20.0), AutoMix.LevelFor(Song(_ => -10, gain: -6)), 3);
        Assert.Equal(0.25, AutoMix.LevelFor(Song(_ => -10, gain: -30)));
    }

    [Fact]
    public void Vocal_span_reads_synced_lines()
    {
        var span = VocalSpan.From([(TimeSpan.FromSeconds(7.25), TimeSpan.FromSeconds(9.17)), (TimeSpan.FromSeconds(187.4), null)])!;
        Assert.Equal(7.25, span.First.TotalSeconds, 0.01);
        Assert.Equal(191.4, span.LastEnd.TotalSeconds, 0.01);
        Assert.Null(VocalSpan.From([]));
    }
}
