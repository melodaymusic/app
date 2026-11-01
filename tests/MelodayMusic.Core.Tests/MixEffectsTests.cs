using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class MixEffectsTests
{
    private const int Rate = 48000;

    private static AudioAnalysis Steady(double bpm, double from, double to, int key, double energy = -3)
    {
        var period = 60 / bpm;
        var beats = new List<double>();
        for (var t = from + 0.1; t < to; t += period) beats.Add(t);
        return new AudioAnalysis
        {
            Bpm = bpm, TempoConfidence = 0.8, Beats = beats, Key = key, From = from, To = to,
            BeatEnergy = Enumerable.Repeat(energy, beats.Count - 1).ToList(),
        };
    }

    private static Transition Loudness(double soundEnd, double overlap = 8) =>
        new(TimeSpan.FromSeconds(soundEnd - overlap), TimeSpan.Zero, CrossfadeShape.Even(TimeSpan.FromSeconds(overlap)));

    private static Func<float[], int, int> Tone(double hz, int channels = 2)
    {
        long n = 0;
        return (into, frames) =>
        {
            for (var i = 0; i < frames; i++, n++)
            {
                var x = (float)(0.3 * Math.Sin(2 * Math.PI * hz * n / Rate));
                for (var c = 0; c < channels; c++) into[i * channels + c] = x;
            }
            return frames;
        };
    }

    private static float[] Play(DeckVoice voice, int frames, int channels = 2, Action<int>? before = null)
    {
        var output = new float[frames * channels];
        var slice = new float[256 * channels];
        for (var at = 0; at < frames; at += 256)
        {
            var n = Math.Min(256, frames - at);
            before?.Invoke(at);
            voice.Read(slice, n);
            Array.Copy(slice, 0, output, at * channels, n * channels);
        }
        return output;
    }

    private static double Rms(float[] data, int fromFrame, int frames, int channels = 2)
    {
        double sum = 0;
        for (var i = fromFrame * channels; i < (fromFrame + frames) * channels; i++) sum += data[i] * (double)data[i];
        return Math.Sqrt(sum / (frames * channels));
    }

    [Fact]
    public void A_loop_roll_repeats_what_it_first_heard_and_shortens_on_the_grid()
    {
        // A slow sweep so every sample is distinct.
        long n = 0;
        var voice = new DeckVoice(Rate, 1, (into, frames) =>
        {
            for (var i = 0; i < frames; i++, n++) into[i] = (float)Math.Sin(n * 0.0007);
            return frames;
        });
        var beat = 0.25;
        var length = (int)(beat * Rate);
        var output = Play(voice, 4 * length, 1, at => voice.Roll = at < 2 * length ? beat : beat / 2);
        for (var k = 200; k < length - 200; k += 997)
            Assert.Equal(output[k], output[length + k], 5);
        // Halving the loop keeps it on the same grid: the first half of the kept beat, over and over.
        for (var k = 200; k < length / 2 - 200; k += 499)
        {
            Assert.Equal(output[k], output[2 * length + k], 5);
            Assert.Equal(output[k], output[2 * length + length / 2 + k], 5);
        }
    }

    [Fact]
    public void A_loop_roll_eases_its_seams()
    {
        var voice = new DeckVoice(Rate, 2, Tone(997)) { Roll = 0.05 };
        var output = Play(voice, Rate);
        var steepest = 0f;
        for (var i = 1; i < output.Length / 2; i++) steepest = Math.Max(steepest, Math.Abs(output[i * 2] - output[(i - 1) * 2]));
        Assert.True(steepest < 0.2f, $"steepest step {steepest:0.000}");
    }

    [Fact]
    public void Echo_and_reverb_ring_on_after_the_fader_closes_then_fall_silent()
    {
        foreach (var send in new Action<DeckVoice, float>[] { (v, s) => v.EchoSend = s, (v, s) => v.ReverbSend = s })
        {
            var voice = new DeckVoice(Rate, 2, Tone(440)) { EchoDelay = 0.3 };
            var close = Rate / 2;
            var output = Play(voice, 10 * Rate, before: at =>
            {
                send(voice, at < close ? 1 : 0);
                voice.Gain = at < close ? 1 : 0;
            });
            Assert.True(Rms(output, close + Rate / 10, Rate / 2) > 0.01, "a tail rings on once the fader is down");
            Assert.True(Rms(output, 9 * Rate, Rate / 2) < 1e-4, "and then dies away");
        }
    }

    [Fact]
    public void Nothing_reaches_the_sends_with_the_fader_down()
    {
        var voice = new DeckVoice(Rate, 2, Tone(440)) { Gain = 0, EchoSend = 1, ReverbSend = 1 };
        Assert.True(Rms(Play(voice, Rate), 0, Rate) < 1e-6);
    }

    [Fact]
    public void Stereo_echoes_bounce_from_side_to_side()
    {
        var calls = 0;
        var voice = new DeckVoice(Rate, 2, (into, frames) =>
        {
            Array.Clear(into, 0, frames * 2);
            if (calls++ == 1) into[0] = into[1] = 0.8f;
            return frames;
        }) { EchoSend = 1, EchoDelay = 0.2 };
        var output = Play(voice, Rate);
        var delay = (int)(0.2 * Rate);
        double Side(int from, int channel)
        {
            double sum = 0;
            for (var i = from - 20; i < from + 400; i++) sum += Math.Abs(output[i * 2 + channel]);
            return sum;
        }
        Assert.True(Side(256 + delay, 0) > 2 * Side(256 + delay, 1), "the first repeat leans left");
        Assert.True(Side(256 + 2 * delay, 1) > 2 * Side(256 + 2 * delay, 0), "the second leans right");
    }

    [Fact]
    public void The_riser_is_silent_until_asked_and_never_loud()
    {
        var riser = new Riser(Rate, 2);
        var quiet = new float[512];
        riser.Process(quiet, 0, 256, 0, 1000);
        Assert.All(quiet, x => Assert.Equal(0, x));
        var loud = new float[Rate * 2];
        for (var at = 0; at < Rate; at += 256) riser.Process(loud, at * 2, Math.Min(256, Rate - at), 1, 400 * Math.Pow(20, at / (double)Rate));
        Assert.True(loud.Max(Math.Abs) < 0.6f, $"peak {loud.Max(Math.Abs):0.00}");
        Assert.True(Rms(loud, Rate / 2, Rate / 2) > 0.01);
    }

    [Fact]
    public void A_next_song_that_comes_in_harder_over_clashing_keys_gets_a_build_and_a_drop()
    {
        var outgoing = Steady(124, 140, 200, 0, energy: -7);
        var plan = BeatMix.Plan(Loudness(196), outgoing, Steady(124, 0, 45, 6))!;
        Assert.Equal(MixStyle.Build, plan.Style);
        var beat = 60 / 124.0;
        Assert.Equal(8 * beat, plan.Drop.TotalSeconds, 3);
        Assert.Equal(4 * beat, plan.InDelay.TotalSeconds, 3);
        Assert.Equal(beat, plan.Beat.TotalSeconds, 3);
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - (plan.StartAt + plan.Drop).TotalSeconds) < 1e-6);

        MixMoment At(double beats) => MixTimeline.At(plan, TimeSpan.FromSeconds(beats * beat));
        Assert.Equal(0, At(2).OutRoll);
        Assert.Equal(beat, At(4.5).OutRoll, 6);
        Assert.Equal(beat / 2, At(6.5).OutRoll, 6);
        Assert.Equal(beat / 4, At(7.2).OutRoll, 6);
        Assert.Equal(beat / 8, At(7.8).OutRoll, 6);
        Assert.True(At(3.9).InWaiting);
        Assert.Equal(0, At(3.9).InGain);
        Assert.True(At(1).Riser < At(4).Riser && At(4).Riser < At(7.9).Riser, "the riser climbs");
        Assert.True(At(1).RiserHz < At(7.9).RiserHz, "and sweeps up");
        Assert.True(At(1).OutCutoff < At(7.9).OutCutoff, "the outgoing song thins as it builds");
        Assert.True(At(7.95).OutReverb > 0.8 * BeatMix.BuildReverb, "the last beat is thrown into the reverb");

        // Under the last bar the next song is already playing, bass cut and filtered, rising towards the drop.
        var under = At(6);
        Assert.False(under.InWaiting);
        Assert.InRange(under.InGain, 0.5, 0.95);
        Assert.Equal(1, under.OutGain / under.Trim, 3);
        Assert.Equal(BeatMix.BassCut, under.InCutoff);
        Assert.InRange(under.InLowPass, BeatMix.BuildInFrom + 1, BeatMix.FilterOpen - 1);
        Assert.True(At(4.5).InLowPass < At(7.5).InLowPass, "its filter opens");

        var dropped = At(8.25);
        Assert.Equal(1, dropped.InGain / dropped.Trim, 3);
        Assert.Equal(0, dropped.InLowPass);
        Assert.True(dropped.OutGain / dropped.Trim is > 0.1 and < 0.95, "the outgoing song fades over a beat rather than stopping");
        Assert.Equal(0, dropped.Riser);
        Assert.True(At(8.6).InCutoff < 50, "the bass has swapped");
        Assert.Equal(0, At(9.1).OutGain, 3);
        Assert.False(At(9.1).OutgoingDone, "the reverb rings on over the drop");
        Assert.True(MixTimeline.At(plan, plan.Shape.Overlap + plan.Tail).OutgoingDone);
    }

    [Fact]
    public void No_move_leaves_a_hole_between_the_songs()
    {
        var plans = new[]
        {
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0, energy: -7), Steady(124, 0, 45, 6))!,
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(80, 0, 45, 0, energy: -10))!,
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(80, 0, 45, 0))!,
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 6))!,
        };
        Assert.Equal([MixStyle.Build, MixStyle.Wash, MixStyle.Echo, MixStyle.Filter], plans.Select(p => p.Style));
        foreach (var plan in plans)
        {
            var a = new DeckVoice(Rate, 2, Tone(220));
            var b = new DeckVoice(Rate, 2, Tone(330));
            var edges = MixTimeline.Edges(plan).Select(t => (long)Math.Ceiling(t * Rate - 1e-6)).ToArray();
            var total = (int)((plan.Shape.Overlap.TotalSeconds + 1) * Rate);
            var output = new float[total * 2];
            var slice = new float[256 * 2];
            var waited = false;
            for (var at = 0; at < total;)
            {
                var n = Math.Min(256, total - at);
                foreach (var edge in edges)
                    if (edge > at && edge - at < n) n = (int)(edge - at);
                var moment = MixTimeline.At(plan, TimeSpan.FromSeconds(at / (double)Rate));
                (a.Gain, a.Cutoff, a.Bands, a.Rate) = ((float)moment.OutGain, moment.OutCutoff, moment.OutBands, moment.OutRate);
                (a.EchoSend, a.ReverbSend, a.Roll, a.EchoDelay) = (moment.OutEcho, moment.OutReverb, moment.OutRoll, plan.EchoDelay.TotalSeconds);
                a.Read(slice, n);
                Array.Copy(slice, 0, output, at * 2, n * 2);
                if (moment.InWaiting) waited = true;
                else
                {
                    if (waited) b.RiseFromSilence();
                    waited = false;
                    (b.Gain, b.Cutoff, b.LowCutoff, b.Bands, b.Rate) = ((float)moment.InGain, moment.InCutoff, moment.InLowPass, moment.InBands, moment.InRate);
                    b.Read(slice, n);
                    for (var i = 0; i < n * 2; i++) output[at * 2 + i] += slice[i];
                }
                at += n;
            }
            var steady = Rms(output, 0, Rate / 10);
            var window = Rate / 50;
            var quietest = Enumerable.Range(0, total / window).Min(w => Rms(output, w * window, window));
            Assert.True(quietest > 0.3 * steady, $"{plan.Style}: dips to {20 * Math.Log10(quietest / steady):0.0} dB");
        }
    }

    [Fact]
    public void Two_full_songs_are_trimmed_to_about_one_songs_level_and_one_song_is_never_raised()
    {
        var both = new BeatMixPlan { StartAt = default, IncomingStart = default, Shape = new CrossfadeShape(TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(4)) };
        var middle = MixTimeline.At(both, TimeSpan.FromSeconds(8));
        Assert.InRange(middle.Trim, 0.69, 0.72);
        Assert.Equal(1, middle.OutGain * middle.OutGain + middle.InGain * middle.InGain, 2);
        Assert.Equal(1, MixTimeline.At(both, TimeSpan.Zero).Trim);
        Assert.Equal(1, MixTimeline.At(both, TimeSpan.FromSeconds(16.5)).Trim);

        // A deck with its bass and mids taken out barely counts.
        Assert.True(MixTimeline.Energy(BandGains.HighsOnly, 0, 0) < 0.2);
        Assert.True(MixTimeline.Energy(BandGains.Full, 1500, 0) < 0.3);
        Assert.Equal(1, MixTimeline.Energy(BandGains.Full, 0, 0), 6);
    }

    [Fact]
    public void Real_mixes_stay_under_the_limiter_through_the_overlap()
    {
        foreach (var plan in new[]
                 {
                     BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 0))!,
                     BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, energy: -8))!,
                     BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 6))!,
                 })
            for (var t = TimeSpan.Zero; t < plan.Shape.Overlap; t += TimeSpan.FromSeconds(0.25))
            {
                var m = MixTimeline.At(plan, t);
                var heard = m.OutGain * m.OutGain * MixTimeline.Energy(m.OutBands, m.OutCutoff, 0) + m.InGain * m.InGain * MixTimeline.Energy(m.InBands, m.InCutoff, m.InLowPass);
                Assert.True(heard <= 1.001, $"{plan.Style} at {t.TotalSeconds:0.0} s: {10 * Math.Log10(heard):+0.0} dB over one song");
            }
    }

    [Fact]
    public void A_song_fading_out_is_left_before_its_fade_and_a_far_louder_master_never_builds()
    {
        // The outgoing song fades over its last bars, from beat 100 on: the mix lands before it has faded much, never down in it.
        var fading = Steady(124, 140, 200, 0) with { BeatEnergy = Enumerable.Range(0, 120).Select(i => i < 100 ? -3.0 : -3.0 - (i - 100) * 1.5).ToList() };
        var plan = BeatMix.Plan(Loudness(196), fading, Steady(124, 0, 45, 6, energy: 0))!;
        var landing = (plan.StartAt + plan.Landing).TotalSeconds;
        Assert.True(landing <= fading.Beats[106], $"lands at {landing:0.0} s, the fade 10 dB down at {fading.Beats[106]:0.0} s");
        // A jump this big is two masterings, not a song coming in harder.
        Assert.NotEqual(MixStyle.Build, BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0, energy: -20), Steady(124, 0, 45, 6, energy: -3))!.Style);
    }

    [Fact]
    public void The_next_song_takes_over_at_the_bass_swap_the_drop_or_the_cut_not_when_the_mix_starts()
    {
        var eq = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, energy: -8))!;
        Assert.Equal(MixStyle.EqBlend, eq.Style);
        Assert.Equal(eq.BassSwap!.Value + eq.SwapLength, MixTimeline.Handover(eq));
        Assert.True(MixTimeline.Handover(eq) > eq.Shape.Overlap / 2);

        var build = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0, energy: -7), Steady(124, 0, 45, 6))!;
        Assert.Equal(build.Drop, MixTimeline.Handover(build));

        var plain = new BeatMixPlan { StartAt = default, IncomingStart = default, Shape = CrossfadeShape.Even(TimeSpan.FromSeconds(8)) };
        Assert.Equal(TimeSpan.FromSeconds(4), MixTimeline.Handover(plain));
    }

    [Fact]
    public void Slow_songs_never_build()
    {
        var plan = BeatMix.Plan(Loudness(196), Steady(82, 140, 200, 0, energy: -9), Steady(82, 0, 45, 6))!;
        Assert.NotEqual(MixStyle.Build, plan.Style);
    }

    [Fact]
    public void Each_change_in_a_build_lands_on_its_own_sample()
    {
        var plan = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0, energy: -7), Steady(124, 0, 45, 6))!;
        var beat = plan.Beat.TotalSeconds;
        var drop = plan.Drop.TotalSeconds;
        Assert.Equal(new[] { drop - 4 * beat, drop - 2 * beat, drop - beat, drop - beat / 2, drop }, MixTimeline.Edges(plan).ToArray(), new Near());
        Assert.Empty(MixTimeline.Edges(BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 0))!));
    }

    private sealed class Near : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-6;
        public int GetHashCode(double obj) => 0;
    }

    [Fact]
    public void A_build_waits_for_a_real_lift()
    {
        var level = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 6))!;
        Assert.Equal(MixStyle.Filter, level.Style);
        var bigLift = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0, energy: -9), Steady(124, 0, 45, 7))!;
        Assert.Equal(MixStyle.Build, bigLift.Style);
    }

    [Fact]
    public void Far_tempos_into_a_soft_opening_wash_out_into_reverb()
    {
        var plan = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(80, 0, 45, 0, energy: -10))!;
        Assert.Equal(MixStyle.Wash, plan.Style);
        Assert.Equal(BeatMix.WashTail, plan.Tail);
        var o = plan.Shape.Overlap;
        Assert.Equal(0, MixTimeline.At(plan, TimeSpan.Zero).OutReverb);
        Assert.True(MixTimeline.At(plan, o * 0.7).OutReverb > 0.9 * BeatMix.WashReverb);
        Assert.True(MixTimeline.At(plan, o * 0.2).OutCutoff < 50);
        Assert.True(MixTimeline.At(plan, o * 0.99).OutCutoff > 0.8 * BeatMix.WashTop);
        Assert.False(MixTimeline.At(plan, o + TimeSpan.FromSeconds(1)).OutgoingDone);
    }

    [Fact]
    public void The_filter_rise_dissolves_into_reverb_and_echoes_ring_out()
    {
        var rise = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 6))!;
        Assert.Equal(MixStyle.Filter, rise.Style);
        Assert.Equal(BeatMix.RiseTail, rise.Tail);
        Assert.Equal(0, MixTimeline.At(rise, rise.Shape.Overlap * 0.5).OutReverb);
        Assert.True(MixTimeline.At(rise, rise.Shape.Overlap).OutReverb > 0.9 * BeatMix.RiseReverb);

        var echo = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(80, 0, 45, 0))!;
        Assert.Equal(MixStyle.Echo, echo.Style);
        Assert.Equal(BeatMix.EchoTail(echo.EchoDelay), echo.Tail);
    }

    [Fact]
    public void Smooth_blends_keep_no_tail()
    {
        var plan = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 0))!;
        Assert.Equal(TimeSpan.Zero, plan.Tail);
        Assert.Equal(TimeSpan.Zero, plan.InDelay);
        Assert.True(MixTimeline.At(plan, plan.Shape.Overlap).OutgoingDone);
    }

    [Fact]
    public void Each_move_has_a_name_to_show()
    {
        Assert.Equal("Loop roll", BeatMix.Label(new BeatMixPlan { StartAt = default, IncomingStart = default, Shape = CrossfadeShape.Even(TimeSpan.FromSeconds(1)), Style = MixStyle.Build }));
        Assert.Equal("Fade", BeatMix.Label(new BeatMixPlan { StartAt = default, IncomingStart = default, Shape = CrossfadeShape.Even(TimeSpan.FromSeconds(1)) }));
        Assert.All(Enum.GetValues<MixStyle>(), style =>
            Assert.NotEqual("Mix", BeatMix.Label(new BeatMixPlan { StartAt = default, IncomingStart = default, Shape = CrossfadeShape.Even(TimeSpan.FromSeconds(1)), Style = style })));
    }
}
