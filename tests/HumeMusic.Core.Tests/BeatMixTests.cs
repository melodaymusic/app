using HumeMusic.Core.Audio;
using HumeMusic.Core.Playback;

namespace HumeMusic.Core.Tests;

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
        // A gentle ending (a 7 s fade) takes eight bars at most.
        var plan = BeatMix.Plan(Loudness(196, 7), outgoing, incoming)!;
        Assert.True(plan.BeatMatched);
        Assert.Equal(8, plan.Bars);
        // On one of the outgoing song's bar lines, ending as its sound does.
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - plan.StartAt.TotalSeconds) < 1e-3);
        Assert.InRange((plan.StartAt + plan.Shape.Overlap).TotalSeconds, 196 - 2 * 4 * 60 / 124.0, 196 + 0.5);
        Assert.Equal((plan.Shape.Overlap / 2).TotalSeconds, plan.BassSwap!.Value.TotalSeconds, 3);
        Assert.Equal(incoming.Downbeats.First(), plan.IncomingStart.TotalSeconds, 6);
    }

    [Fact]
    public void Clashing_keys_blend_briefly_and_far_tempos_cut_on_the_bar()
    {
        var plan = BeatMix.Plan(Loudness(196, 6), Steady(124, 140, 200, key: 0), Steady(124, 0, 45, key: 6))!;
        Assert.Equal(4, plan.Bars);
        // Tempos too far apart: the loudness blend stands, brought onto a bar line.
        var outgoing = Steady(100, 140, 200);
        var blend = BeatMix.Plan(Loudness(196, 8), outgoing, Steady(140, 0, 45))!;
        Assert.False(blend.BeatMatched);
        Assert.Null(blend.BassSwap);
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - blend.StartAt.TotalSeconds) < 1e-3);
        Assert.InRange(blend.Shape.Overlap.TotalSeconds, 6, 10.5);
    }

    [Fact]
    public void Two_voices_never_share_the_blend()
    {
        // The outgoing singing runs to 190 s; the next song sings 4 s in: room for barely any overlap.
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
        // A cold ending (the loudness plan alone would allow only a moment), but the singing is over
        // 12 s before the end and the next song sings 8 s in: 8 bars of instrumental to share, no more.
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
        // The same key, the same tempo to a per cent, the same energy at the join, and the singing is over
        // 20 s before the end while the next song sings 12 s in: 32 s of instrumental, so sixteen bars (31 s).
        var outgoing = WithEnergy(Steady(124, 140, 200, key: 0), -3);
        var incoming = WithEnergy(Steady(124, 0, 45, key: 0), -4);
        var plan = BeatMix.Plan(Loudness(196, 1), outgoing, incoming,
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(176)), new VocalSpan(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(16, plan.Bars);
        Assert.Contains("perfect fit", plan.Summary);
        // The bass changes hands halfway; the next song is up by bar six and the outgoing one stays until bar ten.
        Assert.Equal(plan.Shape.Overlap.TotalSeconds / 2, plan.BassSwap!.Value.TotalSeconds, 3);
        Assert.Equal(plan.Shape.Overlap.TotalSeconds * 6 / 16, plan.Shape.InFade.TotalSeconds, 3);
        Assert.Equal(plan.Shape.Overlap.TotalSeconds * 10 / 16, plan.Shape.OutFadeStart.TotalSeconds, 3);
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
        // 5% off in tempo: heard, so eight bars at most.
        var fast = BeatMix.Plan(Loudness(196, 1), steady, WithEnergy(Steady(130.2, 0, 45, key: 0), -4), vocals.Out, vocals.In)!;
        Assert.True(fast.Bars <= 8);
        // The key a fifth away is fine for eight, but not sixteen? (a fifth apart is a perfect fit for keys: tempo and energy decide).
        Assert.True(BeatMix.Fit(0.9, 1.0, 0.8));
        Assert.False(BeatMix.Fit(0.6, 1.0, 0.8));
        Assert.False(BeatMix.Fit(1.0, 1.05, 0.8));
        Assert.False(BeatMix.Fit(1.0, 1.0, 0.4));
        // Energy nobody measured is no evidence.
        Assert.False(BeatMix.Fit(1.0, 1.0, null));
    }

    [Fact]
    public void A_long_blend_never_outruns_the_next_songs_analysis_or_eats_a_short_song()
    {
        // 70 bpm: a bar is 3.4 s, so sixteen bars are 55 s; the next song was only analysed for 45 s.
        var outgoing = WithEnergy(Steady(70, 110, 200, key: 0), -3);
        var incoming = WithEnergy(Steady(70, 0, 45, key: 0), -4);
        var plan = BeatMix.Plan(Loudness(196, 25), outgoing, incoming)!;
        Assert.True(plan.Bars <= 8);
        Assert.True(plan.Bars * 4 * 60 / 70.0 < 45);
    }

    [Fact]
    public void A_mismatched_opening_keeps_the_blend_short()
    {
        // The same room to share, but the next song comes in far quieter than the ending.
        var outgoing = WithEnergy(Steady(124, 140, 200, key: 0), -3);
        var incoming = WithEnergy(Steady(124, 0, 45, key: 7), -20);
        var plan = BeatMix.Plan(Loudness(196, 1), outgoing, incoming,
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(176)), new VocalSpan(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(150)))!;
        Assert.True(plan.Bars <= 2);
    }
}
