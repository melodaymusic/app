using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Tests;

public class LimiterTests
{
    private const int Rate = 48000;

    private static float[] Tone(double amplitude, double hz, int frames)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
            samples[i * 2] = samples[i * 2 + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate));
        return samples;
    }

    [Fact]
    public void Quiet_audio_passes_through_unchanged_after_the_lookahead()
    {
        var limiter = new Limiter(Rate, 2);
        var input = Tone(0.5, 440, 4800);
        var output = (float[])input.Clone();
        limiter.Process(output, 0, output.Length);
        var delay = limiter.LatencyFrames * 2;
        for (var i = delay; i < output.Length; i++) Assert.Equal(input[i - delay], output[i], 5);
    }

    [Fact]
    public void Peaks_never_pass_the_ceiling()
    {
        var limiter = new Limiter(Rate, 2);
        var loud = Tone(1.6, 220, 24000);
        for (var i = 12000; i < 12010; i++) loud[i * 2] = 3f;
        limiter.Process(loud, 0, loud.Length);
        Assert.All(loud, s => Assert.True(Math.Abs(s) <= 0.9701f));
    }

    [Fact]
    public void Gain_changes_are_gradual_rather_than_sample_to_sample_jumps()
    {
        var limiter = new Limiter(Rate, 2);
        var input = Tone(0.5, 110, Rate);
        for (var i = Rate / 2; i < Rate; i++) input[i * 2] = input[i * 2 + 1] *= 3.2f;
        var output = (float[])input.Clone();
        limiter.Process(output, 0, output.Length);
        var delay = limiter.LatencyFrames;
        var biggestStep = 0.0;
        double? previous = null;
        for (var i = delay; i < Rate; i++)
        {
            var dry = input[(i - delay) * 2];
            if (Math.Abs(dry) < 0.3) { previous = null; continue; }
            var gain = output[i * 2] / dry;
            if (previous is { } p) biggestStep = Math.Max(biggestStep, Math.Abs(gain - p));
            previous = gain;
        }
        Assert.True(biggestStep < 0.01, $"gain jumped by {biggestStep:0.000} between samples");
    }
}
