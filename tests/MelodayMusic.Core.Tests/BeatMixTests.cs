using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class BeatMixTests
{
    private static AudioAnalysis Steady(double bpm, double from, double to, int key = 0, int phase = 0)
    {
        var period = 60 / bpm;
        var beats = new List<double>();
        for (var t = from + 0.1; t < to; t += period) beats.Add(t);
        return new AudioAnalysis { Bpm = bpm, TempoConfidence = 0.8, Beats = beats, DownbeatPhase = phase, Key = key, From = from, To = to };
    }

    private static Transition Loudness(double soundEnd, double overlap, double leadIn = 0) =>
        new(TimeSpan.FromSeconds(soundEnd - overlap), TimeSpan.FromSeconds(leadIn), CrossfadeShape.Even(TimeSpan.FromSeconds(overlap)));

    [Theory]
    [InlineData(124, 126, 124 / 126.0)]
    [InlineData(87, 174, 1.0)]
    [InlineData(128, 64, 1.0)]
    [InlineData(113, 123, 113 / 123.0)]
    [InlineData(100, 128, null)]
    public void Tempos_meet_at_the_same_or_double_or_half(double outgoing, double incoming, double? expected)
    {
        var ratio = BeatMix.TempoRatio(outgoing, incoming);
        if (expected is null) Assert.Null(ratio);
        else Assert.Equal(expected.Value, ratio!.Value, 6);
    }

    [Fact]
    public void Matched_songs_in_close_keys_blend_long_on_bar_lines()
    {
        var outgoing = Steady(124, 140, 200, key: 0);
        var incoming = Steady(126, 0, 45, key: 7);
        // Not knowing where the voices are, the songs share four bars; knowing they keep apart, eight.
        Assert.Equal(4, BeatMix.Plan(Loudness(196, 7), outgoing, incoming)!.Bars);
        var plan = BeatMix.Plan(Loudness(196, 7), outgoing, incoming,
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(150)), new VocalSpan(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(150)))!;
        Assert.True(plan.BeatMatched);
        Assert.Equal(8, plan.Bars);
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - plan.StartAt.TotalSeconds) < 1e-3);
        Assert.InRange((plan.StartAt + plan.Shape.Overlap).TotalSeconds, 196 - 2 * 4 * 60 / 124.0, 196 + 0.5);
        // The bass swaps on the landing, the downbeat the outgoing song's last bar ends on.
        Assert.Equal(plan.Landing.TotalSeconds, (plan.BassSwap!.Value + plan.SwapLength).TotalSeconds, 6);
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - (plan.StartAt.TotalSeconds + plan.Landing.TotalSeconds * plan.OutRate)) < 1e-3);
        Assert.Equal(incoming.Downbeats.First(), plan.IncomingStart.TotalSeconds, 6);
    }

    [Fact]
    public void Clashing_keys_blend_briefly_and_far_tempos_echo_out_on_the_bar()
    {
        var plan = BeatMix.Plan(Loudness(196, 6), Steady(124, 140, 200, key: 0), Steady(124, 0, 45, key: 6))!;
        Assert.Equal(4, plan.Bars);
        var outgoing = Steady(100, 140, 200);
        var echo = BeatMix.Plan(Loudness(196, 8), outgoing, Steady(140, 0, 45))!;
        Assert.False(echo.BeatMatched);
        Assert.Null(echo.BassSwap);
        Assert.Equal(MixStyle.Echo, echo.Style);
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - echo.StartAt.TotalSeconds) < 1e-3);
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - (echo.StartAt + echo.Landing).TotalSeconds) < 1e-3);
        Assert.Equal(echo.Landing, echo.InDelay);
    }

    [Fact]
    public void Two_voices_never_share_the_blend()
    {
        var plan = BeatMix.Plan(Loudness(196, 10), Steady(124, 140, 200), Steady(124, 0, 45),
            new VocalSpan(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(190)), new VocalSpan(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(150)))!;
        Assert.True(plan.Bars <= 4);
    }

    [Fact]
    public void Without_a_steady_beat_the_loudness_plan_stands() =>
        Assert.Null(BeatMix.Plan(Loudness(196, 6), new AudioAnalysis(), Steady(124, 0, 45)));

    private static AudioAnalysis WithEnergy(AudioAnalysis a, double level) => a with { BeatEnergy = Enumerable.Repeat(level, a.Beats.Count - 1).ToList() };

    [Fact]
    public void An_instrumental_ending_into_a_matching_opening_shares_eight_bars()
    {
        var outgoing = WithEnergy(Steady(124, 140, 200, key: 0), -3);
        var incoming = WithEnergy(Steady(124, 0, 45, key: 7), -4);
        var plan = BeatMix.Plan(Loudness(196, 1), outgoing, incoming,
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(184)), new VocalSpan(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(8, plan.Bars);
        Assert.NotNull(plan.BassSwap);
    }

    [Fact]
    public void A_perfect_fit_with_room_for_it_blends_sixteen_bars_with_both_songs_full_through_the_middle()
    {
        var outgoing = WithEnergy(Steady(124, 140, 200, key: 0), -3);
        var incoming = WithEnergy(Steady(124, 0, 45, key: 0), -4);
        var plan = BeatMix.Plan(Loudness(196, 1), outgoing, incoming,
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(176)), new VocalSpan(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(16, plan.Bars);
        Assert.Contains("perfect fit", plan.Summary);
        Assert.Equal(plan.Landing.TotalSeconds, (plan.BassSwap!.Value + plan.SwapLength).TotalSeconds, 6);
        Assert.Equal(plan.Beat, plan.Shape.InFade);
        Assert.Equal(plan.Landing, plan.Shape.OutFadeStart);
        var (outgoingGain, incomingGain) = plan.Shape.Gains(plan.Shape.Overlap / 2);
        Assert.Equal(1, outgoingGain, 6);
        Assert.Equal(1, incomingGain, 6);
        Assert.Equal(0, plan.Shape.Gains(plan.Shape.Overlap).Outgoing, 6);
    }

    [Fact]
    public void Only_a_close_tempo_and_a_close_energy_make_a_perfect_fit()
    {
        var steady = WithEnergy(Steady(124, 140, 200, key: 0), -3);
        var vocals = (Out: new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(176)), In: new VocalSpan(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(150)));
        var fast = BeatMix.Plan(Loudness(196, 1), steady, WithEnergy(Steady(134, 0, 45, key: 0), -4), vocals.Out, vocals.In)!;
        Assert.True(fast.Bars <= 8);
        Assert.True(BeatMix.Fit(0.9, 1.0, 0.8));
        Assert.False(BeatMix.Fit(0.6, 1.0, 0.8));
        Assert.False(BeatMix.Fit(1.0, 1.08, 0.8));
        Assert.False(BeatMix.Fit(1.0, 1.0, 0.4));
        Assert.False(BeatMix.Fit(1.0, 1.0, null));
    }

    [Fact]
    public void A_long_blend_never_outruns_the_next_songs_analysis_or_eats_a_short_song()
    {
        var outgoing = WithEnergy(Steady(70, 110, 200, key: 0), -3);
        var incoming = WithEnergy(Steady(70, 0, 45, key: 0), -4);
        var plan = BeatMix.Plan(Loudness(196, 25), outgoing, incoming)!;
        Assert.True(plan.Bars <= 8);
        Assert.True(plan.Bars * 4 * 60 / 70.0 < 45);
    }

    [Fact]
    public void A_mismatched_opening_still_lets_the_outgoing_song_fade()
    {
        var outgoing = WithEnergy(Steady(124, 140, 200, key: 0), -3);
        var incoming = WithEnergy(Steady(124, 0, 45, key: 7), -20);
        var plan = BeatMix.Plan(Loudness(196, 1), outgoing, incoming,
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(176)), new VocalSpan(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(150)))!;
        Assert.True(plan.Bars >= BeatMix.ShortestBars);
    }

    [Theory]
    [InlineData(0, 0, BeatMix.SuitedBars)]
    [InlineData(0, 7, BeatMix.SuitedBars)]
    [InlineData(0, 6, BeatMix.ShortestBars)]
    public void A_tight_loudness_plan_never_rushes_the_outgoing_song_out(int outKey, int inKey, int fewestBars)
    {
        // As in a real log: the loudness plan left 0.8 s, and the beat mix used to follow it down to a bar or two.
        var plan = BeatMix.Plan(Loudness(196, 0.8), WithEnergy(Steady(123.9, 140, 200, key: outKey), -3), WithEnergy(Steady(123.2, 0, 45, key: inKey), -3))!;
        Assert.True(plan.Bars >= fewestBars, $"{plan.Bars} bars");
        AssertFades(plan);
    }

    [Fact]
    public void Where_two_voices_would_meet_the_outgoing_one_echoes_out_and_the_next_comes_in_after_it()
    {
        var plan = BeatMix.Plan(Loudness(196, 0.8), WithEnergy(Steady(123.9, 140, 200, key: 0), -3), WithEnergy(Steady(123.2, 0, 45, key: 0), -3),
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(194)), new VocalSpan(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(MixStyle.Echo, plan.Style);
        Assert.True(plan.Throw);
        var nextVoice = plan.StartAt.TotalSeconds + (plan.InDelay.TotalSeconds + (1 - plan.IncomingStart.TotalSeconds) / plan.TempoRatio) * plan.OutRate;
        Assert.True(nextVoice >= 194 - 0.3, $"the next voice starts at {nextVoice:0.0} s");
    }

    private static void AssertFades(BeatMixPlan plan)
    {
        var bar = 4 * 60 / 123.9;
        Assert.True(plan.Shape.Overlap >= TimeSpan.FromSeconds(2 * bar), "the outgoing song plays on for at least two bars");
        var fading = plan.Shape.Overlap - plan.Shape.OutFadeStart;
        Assert.True(fading >= TimeSpan.FromSeconds(0.25 * bar), $"{plan.Style}: the outgoing song fades rather than stops");
    }

    [Fact]
    public void Far_tempos_still_play_the_outgoing_song_to_its_last_bar()
    {
        var outgoing = Steady(127.1, 140, 200);
        var plan = BeatMix.Plan(Loudness(196, 0.8), outgoing, Steady(99.7, 0, 45))!;
        Assert.False(plan.BeatMatched);
        var landing = (plan.StartAt + plan.Landing).TotalSeconds;
        Assert.InRange(landing, 196 - 4 * 60 / 127.1, 196 + 0.5);
        var lastBeat = MixTimeline.At(plan, plan.Landing - plan.Beat);
        Assert.Equal(1, lastBeat.OutGain / lastBeat.Trim, 3);
    }

    [Fact]
    public void Both_songs_meet_halfway_in_tempo_so_neither_changes_much()
    {
        var outgoing = Steady(113, 140, 200);
        var incoming = Steady(123, 0, 45);
        var plan = BeatMix.Plan(Loudness(196, 7), outgoing, incoming)!;
        Assert.True(plan.BeatMatched);
        Assert.Equal(113 * plan.OutRate, 123 * plan.TempoRatio, 6);
        Assert.True(plan.OutRate > 1 && plan.TempoRatio < 1);
        Assert.InRange(plan.OutRate - 1, 0, BeatMix.MaxStretch);
        Assert.InRange(1 - plan.TempoRatio, 0, BeatMix.MaxStretch);
        Assert.True(plan.LeadIn > TimeSpan.Zero);
        Assert.Equal(plan.Bars * 4 * 60 / 113.0 / plan.OutRate, plan.Landing.TotalSeconds, 3);
        Assert.Equal(plan.Landing.TotalSeconds, (plan.BassSwap!.Value + plan.SwapLength).TotalSeconds, 6);
    }

    [Fact]
    public void The_outgoing_song_eases_smoothly_to_the_meeting_tempo()
    {
        var plan = BeatMix.Plan(Loudness(196, 7), Steady(113, 140, 200), Steady(123, 0, 45))!;
        var start = plan.StartAt.TotalSeconds;
        var easeFrom = start - plan.LeadIn.TotalSeconds;
        Assert.Equal(1, MixTimeline.LeadInRate(plan, 1, easeFrom, easeFrom - 5), 6);
        Assert.Equal(plan.OutRate, MixTimeline.LeadInRate(plan, 1, easeFrom, start), 6);
        var previous = 1.0;
        for (var t = easeFrom; t <= start; t += 0.25)
        {
            var rate = MixTimeline.LeadInRate(plan, 1, easeFrom, t);
            Assert.True(rate >= previous - 1e-9);
            Assert.True(rate - previous < 0.002);
            previous = rate;
        }
        Assert.Equal(plan.OutRate, MixTimeline.At(plan, plan.Shape.Overlap / 2).OutRate, 6);
    }

    [Fact]
    public void Matched_tempos_change_neither_song()
    {
        var plan = BeatMix.Plan(Loudness(196, 7), Steady(124, 140, 200), Steady(124, 0, 45))!;
        Assert.Equal(1, plan.OutRate);
        Assert.Equal(1, plan.TempoRatio);
        Assert.Equal(TimeSpan.Zero, plan.LeadIn);
    }
}
