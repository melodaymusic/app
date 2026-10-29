using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class LongMixTests
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

    private static Func<float[], int, int> Tone(double hz, double amplitude)
    {
        long n = 0;
        return (into, frames) =>
        {
            for (var i = 0; i < frames; i++, n++)
            {
                var x = (float)(amplitude * (Math.Sin(2 * Math.PI * hz * n / Rate) + 0.3 * Math.Sin(2 * Math.PI * 2 * hz * n / Rate)));
                into[i * 2] = x;
                into[i * 2 + 1] = x;
            }
            return frames;
        };
    }

    private static float[] Render(BeatMixPlan plan, double leadSeconds)
    {
        var a = new DeckVoice(Rate, 2, Tone(220, 0.25));
        var b = new DeckVoice(Rate, 2, Tone(330, 0.25)) { Gain = 0 };
        var total = (int)((leadSeconds + plan.Shape.Overlap.TotalSeconds + plan.RampBack.TotalSeconds + 6) * Rate);
        var lead = (int)(leadSeconds * Rate);
        var result = new float[total * 2];
        var slice = new float[256 * 2];
        for (var at = 0; at < total; at += 256)
        {
            var n = Math.Min(256, total - at);
            if (at < lead)
            {
                a.Read(slice, n);
                Array.Copy(slice, 0, result, at * 2, n * 2);
                continue;
            }
            var moment = MixTimeline.At(plan, TimeSpan.FromSeconds((at - lead) / (double)Rate));
            if (!moment.OutgoingDone)
            {
                a.Gain = (float)moment.OutGain;
                a.Cutoff = moment.OutCutoff;
                a.Read(slice, n);
                for (var i = 0; i < n * 2; i++) result[at * 2 + i] += slice[i];
            }
            b.Gain = (float)moment.InGain;
            b.Cutoff = moment.InCutoff;
            b.Rate = moment.InRate;
            b.Read(slice, n);
            for (var i = 0; i < n * 2; i++) result[at * 2 + i] += slice[i];
        }
        return result;
    }

    [Fact]
    public void A_sixteen_bar_blend_makes_no_click_nor_overload_and_ends_on_the_next_song_alone()
    {
        var outgoing = Steady(124, 140, 200, 0);
        var incoming = Steady(124, 0, 45, 0);
        var plan = BeatMix.Plan(new Transition(TimeSpan.FromSeconds(195), TimeSpan.Zero, CrossfadeShape.Even(TimeSpan.FromSeconds(1))), outgoing, incoming,
            new VocalSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(176)), new VocalSpan(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(150)))!;
        Assert.Equal(16, plan.Bars);

        var output = Render(plan, leadSeconds: 4);
        Assert.DoesNotContain(output, float.IsNaN);
        Assert.True(output.Max(Math.Abs) < 1.0f, "no overload");
        var steepest = 0f;
        for (var i = 1; i < output.Length / 2; i++) steepest = Math.Max(steepest, Math.Abs(output[i * 2] - output[(i - 1) * 2]));
        Assert.True(steepest < 0.1f, $"steepest step {steepest:0.000}");

        var tail = output.Skip(output.Length - 2 * Rate * 2).Where((_, i) => i % 2 == 0).ToArray();
        var rms = Math.Sqrt(tail.Sum(x => (double)x * x) / tail.Length);
        Assert.InRange(rms, 0.16, 0.26);
        double Tone(double hz)
        {
            double re = 0, im = 0;
            for (var i = 0; i < tail.Length; i++)
            {
                re += tail[i] * Math.Cos(2 * Math.PI * hz * i / Rate);
                im += tail[i] * Math.Sin(2 * Math.PI * hz * i / Rate);
            }
            return Math.Sqrt(re * re + im * im) / tail.Length;
        }
        Assert.True(Tone(330) > 20 * Tone(220));
    }
}
