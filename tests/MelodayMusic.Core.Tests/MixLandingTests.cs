using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class MixLandingTests
{
    private const double Bpm = 124;
    private const double Bar = 4 * 60 / Bpm;

    // A steady song whose beats carry the energy the function gives each bar.
    private static AudioAnalysis Song(double from, double to, int key, Func<int, double> barEnergy)
    {
        var beats = new List<double>();
        for (var t = from + 0.1; t < to; t += 60 / Bpm) beats.Add(t);
        return new AudioAnalysis
        {
            Bpm = Bpm, TempoConfidence = 0.8, Beats = beats, Key = key, From = from, To = to,
            BeatEnergy = Enumerable.Range(0, beats.Count - 1).Select(i => barEnergy(i / 4)).ToList(),
        };
    }

    private static Transition Loudness(double soundEnd, double overlap = 8) =>
        new(TimeSpan.FromSeconds(soundEnd - overlap), TimeSpan.Zero, CrossfadeShape.Even(TimeSpan.FromSeconds(overlap)));

    private static double Landing(BeatMixPlan plan) => (plan.StartAt + plan.Landing * plan.OutRate).TotalSeconds;

    [Fact]
    public void A_quiet_outro_is_left_out_and_the_mix_lands_where_the_outgoing_song_stops_being_itself()
    {
        // The outgoing song drops 13 dB for its last eight bars, from bar 21 on.
        var outgoing = Song(140, 200, 0, bar => bar < 21 ? -2 : -15);
        var plan = BeatMix.Plan(Loudness(196), outgoing, Song(0, 45, 0, _ => -2))!;
        Assert.Equal(outgoing.Beats[84], Landing(plan), 2);
        Assert.Equal(MixStyle.EqBlend, plan.Style);
        Assert.True(plan.Throw, "leaving the outgoing song early, its last beat goes into the echo");
        Assert.Contains("left out", plan.Summary);
        Assert.True(plan.Shape.Overlap - plan.Landing <= TimeSpan.FromSeconds(2 * Bar + 0.01), "the quiet outro rings off within two bars");
        // Quiet against what just played, it is left out even where the whole song counts it as usual.
        var judged = BeatMix.Plan(Loudness(196), outgoing, Song(0, 45, 0, _ => -2), outLiveEnd: TimeSpan.FromSeconds(196))!;
        Assert.Equal(outgoing.Beats[84], Landing(judged), 2);
    }

    [Fact]
    public void A_long_quiet_outro_is_judged_against_the_whole_song_not_just_its_last_minute()
    {
        // Four minutes at -12, then a 30-second outro at -30: the song's own last minute is mostly outro and can't tell.
        var levels = Enumerable.Range(0, 480).Select(i => i < 420 ? -12.0 : -30).ToList();
        var whole = AutoMix.LiveEnd(new TrackLoudness(levels, null), TimeSpan.FromSeconds(240), BeatMix.LiveDrop)!.Value;
        Assert.InRange(whole.TotalSeconds, 209, 212);

        var outgoing = Song(180, 240, 0, bar => bar < 16 ? -4 : -10);
        var plan = BeatMix.Plan(Loudness(238), outgoing, Song(0, 45, 0, _ => -2), outLiveEnd: whole)!;
        Assert.InRange(Landing(plan), whole.TotalSeconds - Bar, whole.TotalSeconds + 0.25);
    }

    [Fact]
    public void A_song_that_plays_to_its_end_lands_on_its_last_downbeat()
    {
        var plan = BeatMix.Plan(Loudness(196), Song(140, 200, 0, _ => -2), Song(0, 45, 0, _ => -2))!;
        Assert.InRange(Landing(plan), 196 - Bar, 196 + 0.25);
        Assert.False(plan.Throw);
    }

    [Fact]
    public void The_next_songs_intro_plays_under_the_outgoing_song_so_it_arrives_on_the_landing()
    {
        // An eight-bar intro 14 dB under the song.
        var incoming = Song(0, 45, 0, bar => bar < 8 ? -16 : -2);
        var arrival = BeatMix.Arrival(incoming, incoming.Downbeats.First());
        Assert.Equal(8, arrival.IntroBars);
        Assert.Equal(-14, arrival.Level, 1);

        var plan = BeatMix.Plan(Loudness(196), Song(140, 200, 0, _ => -2), incoming)!;
        Assert.Equal(MixStyle.EqBlend, plan.Style);
        Assert.Equal(incoming.Downbeats.First(), plan.IncomingStart.TotalSeconds, 6);
        Assert.Equal(12, plan.Bars);
        // The intro under the outgoing song's last bars, then four bars of the song itself before the bass swaps on the landing.
        Assert.Equal((plan.Landing - TimeSpan.FromSeconds(4 * Bar)).TotalSeconds, plan.Arrives.TotalSeconds, 2);
        Assert.Equal(-14, plan.IntroLevel, 1);
    }

    [Fact]
    public void An_intro_only_a_little_under_the_song_still_ends_where_the_song_steps_up()
    {
        var incoming = Song(0, 45, 0, bar => bar < 12 ? -8 : -2);
        Assert.Equal(12, BeatMix.Arrival(incoming, incoming.Downbeats.First()).IntroBars);
        var full = Song(0, 45, 0, _ => -3);
        Assert.Equal(0, BeatMix.Arrival(full, full.Downbeats.First()).IntroBars);
        var silentFirst = Song(0, 45, 0, bar => bar < 2 ? -90 : -3);
        Assert.Equal(2, BeatMix.Arrival(silentFirst, silentFirst.Downbeats.First()).IntroBars);
    }

    [Fact]
    public void While_the_next_song_plays_its_quiet_intro_the_outgoing_one_keeps_its_level()
    {
        var plan = BeatMix.Plan(Loudness(196), Song(140, 200, 0, _ => -2), Song(0, 45, 0, bar => bar < 8 ? -16 : -2))!;
        var underIntro = MixTimeline.At(plan, plan.Arrives / 2);
        Assert.True(underIntro.Trim > 0.95, $"trimmed to {underIntro.Trim:0.00}");
        Assert.Equal(1, underIntro.OutGain / underIntro.Trim, 3);
    }

    [Fact]
    public void A_long_intro_is_joined_partway_so_the_next_song_still_arrives_on_the_landing()
    {
        // Sixteen bars of intro, and keys that clash: only eight bars of it, top end only, go under the outgoing song.
        var incoming = Song(0, 45, 6, bar => bar < 16 ? -16 : -2);
        var plan = BeatMix.Plan(Loudness(196), Song(140, 200, 0, _ => -2), incoming)!;
        Assert.Equal(MixStyle.Filter, plan.Style);
        Assert.Equal(8, plan.Bars);
        Assert.Equal(incoming.Downbeats.ElementAt(8), plan.IncomingStart.TotalSeconds, 6);
        Assert.Equal(plan.Landing, plan.Arrives);
        Assert.Contains("8 bars of it skipped", plan.Summary);
    }

    [Fact]
    public void The_next_song_never_takes_over_just_before_its_own_breakdown()
    {
        // Two quiet bars at bars 6 and 7: landing on bar 4 would leave the listener in them straight after the outgoing song goes.
        var incoming = Song(0, 45, 2, bar => bar is 6 or 7 ? -14 : -3);
        Assert.False(BeatMix.LandsStrong(incoming, incoming.Downbeats.ElementAt(4)));
        Assert.True(BeatMix.LandsStrong(incoming, incoming.Downbeats.ElementAt(2)));

        var plan = BeatMix.Plan(Loudness(196), Song(140, 200, 0, _ => -3), incoming)!;
        var landsAt = plan.IncomingStart.TotalSeconds + (plan.Landing - plan.InDelay).TotalSeconds * plan.TempoRatio;
        Assert.True(BeatMix.LandsStrong(incoming, landsAt), $"lands at {landsAt:0.0} s");
    }

    [Fact]
    public void Without_a_loudness_plan_the_songs_own_analyses_place_the_mix()
    {
        // Silence after 196 s, as the analysis hears it.
        var outgoing = Song(140, 200, 0, bar => 140.1 + bar * Bar < 196 ? -2 : -90);
        Assert.InRange(BeatMix.SoundEnd(outgoing), 195, 196 + Bar);
        var incoming = Song(0, 45, 0, bar => bar < 2 ? -90 : -2);
        Assert.InRange(BeatMix.SoundStart(incoming), 2 * Bar - 0.2, 2 * Bar + 0.2);

        var plan = BeatMix.Plan(null, outgoing, incoming)!;
        Assert.InRange(Landing(plan), 194 - Bar, 196 + 0.25);
        Assert.True(plan.IncomingStart.TotalSeconds >= 2 * Bar - 0.2, "the next song skips its silence");
    }

    [Fact]
    public void Without_a_steady_beat_the_loudness_crossfade_melts_into_reverb_when_there_is_time()
    {
        var loudness = Loudness(196, 8);
        var wash = BeatMix.Fallback(loudness);
        Assert.Equal(MixStyle.Wash, wash.Style);
        Assert.Equal(BeatMix.WashTail, wash.Tail);
        Assert.Equal(loudness.Shape, wash.Shape);
        Assert.True(MixTimeline.At(wash, wash.Shape.Overlap * 0.8).OutReverb > 0);
        Assert.Equal(MixStyle.Blend, BeatMix.Fallback(Loudness(196, 2)).Style);
    }

    [Fact]
    public void A_long_blend_matches_the_tempos_where_the_songs_meet_not_their_averages()
    {
        // The next song's intro actually runs at 124.6 though its analysis averages 124: half a percent, a tenth of a second
        // adrift by the end of a twelve-bar blend if left alone.
        var beats = new List<double>();
        for (var t = 0.1; t < 45; t += 60 / 124.6) beats.Add(t);
        var incoming = new AudioAnalysis
        {
            Bpm = 124, TempoConfidence = 0.8, Beats = beats, Key = 0, From = 0, To = 45,
            BeatEnergy = Enumerable.Range(0, beats.Count - 1).Select(i => i / 4 < 8 ? -16.0 : -2).ToList(),
        };
        var plan = BeatMix.Plan(Loudness(196), Song(140, 200, 0, _ => -2), incoming)!;
        Assert.True(plan.Bars >= 8);
        Assert.Equal(124 * plan.OutRate, 124.6 * plan.TempoRatio, 2);
    }

    [Fact]
    public void Every_landing_hands_the_song_over_on_its_downbeat()
    {
        var outgoing = Song(140, 200, 0, _ => -2);
        foreach (var plan in new[]
                 {
                     BeatMix.Plan(Loudness(196), outgoing, Song(0, 45, 0, bar => bar < 8 ? -16 : -2))!,
                     BeatMix.Plan(Loudness(196), outgoing, Song(0, 45, 6, bar => bar < 16 ? -16 : -2))!,
                     BeatMix.Plan(Loudness(196), outgoing, Song(0, 45, 6, _ => -2))!,
                 })
        {
            Assert.Equal(plan.Landing, MixTimeline.Handover(plan));
            Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - Landing(plan)) < 1e-3);
        }
    }
}
