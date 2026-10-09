using HumeMusic.Core.Audio;
using HumeMusic.Core.Playback;

namespace HumeMusic.Core.Tests;

/// <summary>The filter sweep and the echo out: the filters and the echo do what they say, a plan chooses the right style, and the styles make no sound of their own.</summary>
public class MixStyleTests
{
    private const int Rate = 48000;

    private static AudioAnalysis Steady(double bpm, double from, double to, int key)
    {
        var period = 60 / bpm;
        var beats = new List<double>();
        for (var t = from + 0.1; t < to; t += period) beats.Add(t);
        return new AudioAnalysis
        {
            Bpm = bpm, TempoConfidence = 0.8, Beats = beats, Key = key, From = from, To = to,
            BeatEnergy = Enumerable.Repeat(-3.0, beats.Count - 1).ToList(),
        };
    }

    private static Transition Loudness(double startAt) =>
        new(TimeSpan.FromSeconds(startAt), TimeSpan.Zero, CrossfadeShape.Even(TimeSpan.FromSeconds(8)));

    private static double Rms(float[] data, int from, int count)
    {
        double sum = 0;
        for (var i = from; i < from + count; i++) sum += data[i] * (double)data[i];
        return Math.Sqrt(sum / count);
    }

    private static float[] Sine(double hz, int frames, double amplitude = 0.5)
    {
        var data = new float[frames];
        for (var i = 0; i < frames; i++) data[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate));
        return data;
    }

    [Fact]
    public void The_low_pass_keeps_the_lows_and_takes_the_highs()
    {
        var low = new LowPass(Rate, 1) { Cutoff = 1000 };
        var a = Sine(200, Rate);
        var b = Sine(8000, Rate);
        low.Process(a, 0, a.Length);
        low.Reset();
        low.Process(b, 0, b.Length);
        Assert.True(Rms(a, Rate / 2, Rate / 4) > 0.33, "200 Hz passes");
        Assert.True(Rms(b, Rate / 2, Rate / 4) < 0.02, "8 kHz is taken");
    }

    [Fact]
    public void A_low_pass_that_is_off_leaves_the_sound_as_it_is()
    {
        var low = new LowPass(Rate, 1);
        var a = Sine(5000, 4800);
        var copy = (float[])a.Clone();
        low.Process(a, 0, a.Length);
        Assert.Equal(copy, a);
    }

    [Fact]
    public void An_echo_repeats_the_sound_after_its_delay_and_each_repeat_is_quieter()
    {
        var first = true;
        var voice = new DeckVoice(Rate, 1, (into, frames) =>
        {
            for (var i = 0; i < frames; i++) into[i] = 0;
            if (first)
            {
                into[0] = 0.8f;
                first = false;
            }
            return frames;
        }) { EchoWet = 1, EchoDelay = 0.2 };
        // Read as the mixer does, a slice at a time (the echo's level glides across each slice).
        var output = new float[Rate];
        var slice = new float[256];
        for (var at = 0; at < Rate; at += 256)
        {
            var n = Math.Min(256, Rate - at);
            voice.Read(slice, n);
            Array.Copy(slice, 0, output, at, n);
        }
        var delay = (int)(0.2 * Rate);
        // The first slice brings the echo up from nothing, so only the later repeats are at full strength.
        Assert.Equal(0.8f, output[0], 3);
        Assert.Equal(0.4f, output[2 * delay], 2);
        Assert.Equal(0.2f, output[3 * delay], 2);
        Assert.True(output[delay] > 0.5f, "the first repeat is heard");
    }

    [Fact]
    public void Without_echo_asked_for_nothing_repeats()
    {
        var first = true;
        var voice = new DeckVoice(Rate, 1, (into, frames) =>
        {
            for (var i = 0; i < frames; i++) into[i] = 0;
            if (first)
            {
                into[0] = 0.8f;
                first = false;
            }
            return frames;
        });
        var output = new float[Rate];
        voice.Read(output, Rate);
        Assert.Equal(0, output.Skip(1).Max(Math.Abs));
    }

    [Fact]
    public void A_pair_that_fits_perfectly_is_blended_long_and_other_matched_pairs_get_a_filter_sweep_and_unmatched_ones_an_echo()
    {
        var outgoing = Steady(124, 140, 200, 0);
        // Same tempo, same key, same energy: a perfect fit, so the long blend with the bass changing hands.
        var fit = BeatMix.Plan(Loudness(190), outgoing, Steady(124, 0, 45, 0),
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(176)), new VocalSpan(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(MixStyle.Blend, fit.Style);

        // The tempos match but the keys don't sit together: still beat-matched, so a filter sweep.
        var clash = BeatMix.Plan(Loudness(190), outgoing, Steady(124, 0, 45, 6))!;
        Assert.True(clash.BeatMatched);
        Assert.Equal(MixStyle.Filter, clash.Style);
        Assert.NotNull(clash.BassSwap);

        // Tempos too far apart: an echo out, timed to the outgoing song's beat.
        var far = BeatMix.Plan(Loudness(190), outgoing, Steady(80, 0, 45, 0))!;
        Assert.False(far.BeatMatched);
        Assert.Equal(MixStyle.Echo, far.Style);
        Assert.InRange(far.EchoDelay.TotalSeconds, 0.2, 0.8);
        Assert.Equal(BeatMix.EchoDelayFor(60 / 124.0), far.EchoDelay);
    }

    [Fact]
    public void A_filter_sweep_thins_the_outgoing_song_as_the_next_one_opens_up()
    {
        var plan = BeatMix.Plan(Loudness(190), Steady(124, 140, 200, 0), Steady(124, 0, 45, 6))!;
        Assert.Equal(MixStyle.Filter, plan.Style);
        var start = MixTimeline.At(plan, TimeSpan.Zero);
        var swap = MixTimeline.At(plan, plan.BassSwap!.Value);
        var end = MixTimeline.At(plan, plan.Shape.Overlap - TimeSpan.FromMilliseconds(10));
        // The incoming song begins dull and is open by the time it is fully up.
        Assert.Equal(BeatMix.FilterOpening, start.InLowPass, 0);
        Assert.True(MixTimeline.At(plan, plan.Shape.InFade).InLowPass == 0);
        // The outgoing song's high-pass climbs from the swap to about 2.6 kHz by the end.
        Assert.True(swap.OutCutoff < 50);
        Assert.True(end.OutCutoff > 0.9 * BeatMix.FilterTop);
        Assert.True(MixTimeline.At(plan, plan.BassSwap!.Value + (plan.Shape.Overlap - plan.BassSwap.Value) / 2).OutCutoff is > 100 and < BeatMix.FilterTop);
    }

    [Fact]
    public void An_echo_out_builds_as_the_outgoing_song_fades()
    {
        var plan = BeatMix.Plan(Loudness(190), Steady(124, 140, 200, 0), Steady(80, 0, 45, 0))!;
        Assert.Equal(MixStyle.Echo, plan.Style);
        Assert.Equal(0, MixTimeline.At(plan, TimeSpan.Zero).OutEcho);
        var end = MixTimeline.At(plan, plan.Shape.Overlap);
        Assert.True(end.OutEcho > 0.9 * BeatMix.EchoAmount);
    }

    [Fact]
    public void Each_style_makes_no_click_nor_overload_playing_through_the_mixers_own_processing()
    {
        foreach (var (incomingBpm, incomingKey) in new[] { (124.0, 0), (124.0, 6), (80.0, 0) })
        {
            var plan = BeatMix.Plan(Loudness(190), Steady(124, 140, 200, 0), Steady(incomingBpm, 0, 45, incomingKey))!;
            var a = new DeckVoice(Rate, 2, Tone(220));
            var b = new DeckVoice(Rate, 2, Tone(330)) { Gain = 0 };
            const double lead = 4;
            var total = (int)((lead + plan.Shape.Overlap.TotalSeconds + plan.RampBack.TotalSeconds + 4) * Rate);
            var output = new float[total * 2];
            var slice = new float[256 * 2];
            var leadFrames = (int)(lead * Rate);
            for (var at = 0; at < total; at += 256)
            {
                var n = Math.Min(256, total - at);
                if (at < leadFrames)
                {
                    a.Read(slice, n);
                    Array.Copy(slice, 0, output, at * 2, n * 2);
                    continue;
                }
                var moment = MixTimeline.At(plan, TimeSpan.FromSeconds((at - leadFrames) / (double)Rate));
                if (!moment.OutgoingDone)
                {
                    a.Gain = (float)moment.OutGain;
                    a.Cutoff = moment.OutCutoff;
                    a.EchoDelay = plan.EchoDelay.TotalSeconds;
                    a.EchoWet = moment.OutEcho;
                    a.Read(slice, n);
                    for (var i = 0; i < n * 2; i++) output[at * 2 + i] += slice[i];
                }
                b.Gain = (float)moment.InGain;
                b.Cutoff = moment.InCutoff;
                b.LowCutoff = moment.InLowPass;
                b.Rate = moment.InRate;
                b.Read(slice, n);
                for (var i = 0; i < n * 2; i++) output[at * 2 + i] += slice[i];
            }
            Assert.DoesNotContain(output, float.IsNaN);
            Assert.True(output.Max(Math.Abs) < 1.2f, $"{plan.Style}: overload {output.Max(Math.Abs):0.00}");
            var steepest = 0f;
            for (var i = 1; i < output.Length / 2; i++) steepest = Math.Max(steepest, Math.Abs(output[i * 2] - output[(i - 1) * 2]));
            Assert.True(steepest < 0.2f, $"{plan.Style}: steepest step {steepest:0.000}");
        }
    }

    private static Func<float[], int, int> Tone(double hz)
    {
        long n = 0;
        return (into, frames) =>
        {
            for (var i = 0; i < frames; i++, n++)
            {
                var x = (float)(0.25 * (Math.Sin(2 * Math.PI * hz * n / Rate) + 0.3 * Math.Sin(2 * Math.PI * 2 * hz * n / Rate)));
                into[i * 2] = x;
                into[i * 2 + 1] = x;
            }
            return frames;
        };
    }
}
