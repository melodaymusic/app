using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class DjMixTests
{
    private const int Rate = 48000;

    private static AudioAnalysis Steady(double bpm, double from, double to, int key, Func<double, double>? energyAt = null)
    {
        var period = 60 / bpm;
        var beats = new List<double>();
        for (var t = from + 0.1; t < to; t += period) beats.Add(t);
        return new AudioAnalysis
        {
            Bpm = bpm, TempoConfidence = 0.8, Beats = beats, Key = key, From = from, To = to,
            BeatEnergy = beats.Take(beats.Count - 1).Select(b => energyAt?.Invoke(b) ?? -3.0).ToList(),
        };
    }

    private static Transition Loudness(double soundEnd, double overlap = 8) =>
        new(TimeSpan.FromSeconds(soundEnd - overlap), TimeSpan.Zero, CrossfadeShape.Even(TimeSpan.FromSeconds(overlap)));

    private static float[] Stereo(double hz, int frames, double left, double right)
    {
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var x = Math.Sin(2 * Math.PI * hz * i / Rate);
            data[i * 2] = (float)(left * x);
            data[i * 2 + 1] = (float)(right * x);
        }
        return data;
    }

    private static double Rms(float[] data, int fromFrame, int frames)
    {
        double sum = 0;
        for (var i = fromFrame * 2; i < (fromFrame + frames) * 2; i++) sum += data[i] * (double)data[i];
        return Math.Sqrt(sum / (frames * 2));
    }

    private static float[] Through(BandGains gains, float[] data)
    {
        var mixer = new BandMixer(Rate, 2);
        var copy = (float[])data.Clone();
        for (var at = 0; at < copy.Length / 2; at += 256)
        {
            var n = Math.Min(256, copy.Length / 2 - at);
            var block = new float[n * 2];
            Array.Copy(copy, at * 2, block, 0, n * 2);
            mixer.Process(block, n, gains);
            Array.Copy(block, 0, copy, at * 2, n * 2);
        }
        return copy;
    }

    [Fact]
    public void At_full_the_eq_leaves_the_song_exactly_as_it_is()
    {
        var song = Stereo(440, Rate / 2, 0.5, 0.3);
        Assert.Equal(song, Through(BandGains.Full, song));
    }

    [Fact]
    public void Each_band_takes_its_own_part_of_the_sound()
    {
        var bass = Stereo(60, Rate, 0.5, 0.5);
        var mid = Stereo(1000, Rate, 0.5, 0.5);
        var hat = Stereo(9000, Rate, 0.5, 0.5);
        var noBass = new BandGains(0, 1, 1, 1);
        Assert.True(Rms(Through(noBass, bass), Rate / 2, Rate / 4) < 0.05 * Rms(bass, Rate / 2, Rate / 4), "the bass goes");
        Assert.InRange(Rms(Through(noBass, mid), Rate / 2, Rate / 4) / Rms(mid, Rate / 2, Rate / 4), 0.85, 1.15);
        Assert.True(Rms(Through(BandGains.HighsOnly, mid), Rate / 2, Rate / 4) < 0.15 * Rms(mid, Rate / 2, Rate / 4), "only the highs: the mids go");
        Assert.InRange(Rms(Through(BandGains.HighsOnly, hat), Rate / 2, Rate / 4) / Rms(hat, Rate / 2, Rate / 4), 0.85, 1.15);
    }

    [Fact]
    public void Voice_only_keeps_the_centre_and_drops_the_sides()
    {
        var voice = Stereo(1000, Rate, 0.4, 0.4);
        var sides = Stereo(1000, Rate, 0.4, -0.4);
        Assert.InRange(Rms(Through(BandGains.VoiceOnly, voice), Rate / 2, Rate / 4) / Rms(voice, Rate / 2, Rate / 4), 0.85, 1.15);
        Assert.True(Rms(Through(BandGains.VoiceOnly, sides), Rate / 2, Rate / 4) < 0.2 * Rms(sides, Rate / 2, Rate / 4));
        Assert.True(Rms(Through(BandGains.MusicOnly, voice), Rate / 2, Rate / 4) < 0.4 * Rms(voice, Rate / 2, Rate / 4), "the voice pulled back");
    }

    [Fact]
    public void Keys_that_sit_together_get_an_eq_blend()
    {
        var plan = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, _ => -8))!;
        Assert.Equal(MixStyle.EqBlend, plan.Style);
        var early = MixTimeline.At(plan, plan.Shape.Overlap * 0.2);
        Assert.True(early.InBands.High > 0.5 && early.InBands.MidCentre == 0 && early.InBands.Low == 0);
        var late = MixTimeline.At(plan, plan.Shape.Overlap * 0.7);
        Assert.Equal(1, late.InBands.Low);
        Assert.Equal(0, late.OutBands.Low);
        Assert.Equal(0, late.OutCutoff);
    }

    [Fact]
    public void The_outgoing_voice_rides_over_the_next_songs_intro()
    {
        var plan = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, _ => -8),
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(197)), new VocalSpan(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(MixStyle.VocalOut, plan.Style);
        var middle = MixTimeline.At(plan, plan.Shape.Overlap / 2);
        Assert.Equal(BandGains.VoiceOnly, middle.OutBands);
        Assert.Equal(BandGains.Full, middle.InBands);
        Assert.Equal(1, middle.OutGain, 3);
    }

    [Fact]
    public void The_next_voice_comes_in_over_the_outgoing_outro()
    {
        var plan = BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, _ => -8),
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(170)), new VocalSpan(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(MixStyle.VocalIn, plan.Style);
        Assert.Equal(BandGains.VoiceOnly, MixTimeline.At(plan, TimeSpan.Zero).InBands);
        Assert.Equal(BandGains.Full, MixTimeline.At(plan, plan.Shape.Overlap * 0.9).InBands);
    }

    [Fact]
    public void Tempos_too_far_apart_cut_where_the_outgoing_song_breaks()
    {
        var outgoing = Steady(124, 140, 200, 0, t => t < 185 ? -3 : -12);
        var plan = BeatMix.Plan(Loudness(196), outgoing, Steady(80, 0, 45, 0))!;
        Assert.Equal(MixStyle.Cut, plan.Style);
        Assert.InRange(plan.StartAt.TotalSeconds, 184, 186.5);
        Assert.Contains(outgoing.Downbeats, d => Math.Abs(d - plan.StartAt.TotalSeconds) < 1e-6);
        Assert.True(plan.Shape.Overlap.TotalSeconds < 0.1);
        Assert.Equal(MixStyle.Echo, BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(80, 0, 45, 0))!.Style);
    }

    [Fact]
    public void Every_move_plays_without_a_click_or_overload()
    {
        var plans = new[]
        {
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, _ => -8))!,
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, _ => -8),
                new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(197)), new VocalSpan(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(150)))!,
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0), Steady(124, 0, 45, 7, _ => -8),
                new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(170)), new VocalSpan(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(150)))!,
            BeatMix.Plan(Loudness(196), Steady(124, 140, 200, 0, t => t < 185 ? -3 : -12), Steady(80, 0, 45, 0))!,
        };
        Assert.Equal([MixStyle.EqBlend, MixStyle.VocalOut, MixStyle.VocalIn, MixStyle.Cut], plans.Select(p => p.Style));
        foreach (var plan in plans)
        {
            var a = new DeckVoice(Rate, 2, Tone(220, 0.3, 110));
            var b = new DeckVoice(Rate, 2, Tone(330, -0.2, 82)) { Gain = 0 };
            const double lead = 2;
            var total = (int)((lead + plan.Shape.Overlap.TotalSeconds + plan.RampBack.TotalSeconds + 3) * Rate);
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
                    a.Bands = moment.OutBands;
                    a.Rate = moment.OutRate;
                    a.Read(slice, n);
                    for (var i = 0; i < n * 2; i++) output[at * 2 + i] += slice[i];
                }
                b.Gain = (float)moment.InGain;
                b.Cutoff = moment.InCutoff;
                b.Bands = moment.InBands;
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

    private static Func<float[], int, int> Tone(double hz, double width, double bassHz)
    {
        long n = 0;
        return (into, frames) =>
        {
            for (var i = 0; i < frames; i++, n++)
            {
                var note = 0.18 * Math.Sin(2 * Math.PI * hz * n / Rate);
                var bass = 0.12 * Math.Sin(2 * Math.PI * bassHz * n / Rate);
                into[i * 2] = (float)(note * (1 + width) + bass);
                into[i * 2 + 1] = (float)(note * (1 - width) + bass);
            }
            return frames;
        };
    }
}
