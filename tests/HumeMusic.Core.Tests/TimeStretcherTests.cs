using HumeMusic.Core.Audio;

namespace HumeMusic.Core.Tests;

public class TimeStretcherTests
{
    private const int Rate = 48000;

    private static float[] Tone(double frequency, double seconds)
    {
        var frames = (int)(seconds * Rate);
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / Rate));
            samples[i * 2] = v;
            samples[i * 2 + 1] = v;
        }
        return samples;
    }

    private static float[] Stretch(float[] input, double rate)
    {
        var stretcher = new TimeStretcher(Rate, 2) { Rate = rate };
        var output = new List<float>();
        var buffer = new float[1024 * 2];
        var frames = input.Length / 2;
        for (var at = 0; at < frames; at += 1024)
        {
            stretcher.Put(input, at, Math.Min(1024, frames - at));
            int got;
            while ((got = stretcher.Take(buffer, 0, 1024)) > 0) output.AddRange(buffer.Take(got * 2));
        }
        return output.ToArray();
    }

    private static double Frequency(float[] stereo, int skip)
    {
        var crossings = 0;
        var frames = stereo.Length / 2;
        for (var i = skip + 1; i < frames - skip; i++)
            if (stereo[(i - 1) * 2] < 0 && stereo[i * 2] >= 0) crossings++;
        return crossings / ((frames - 2 * skip) / (double)Rate);
    }

    [Theory]
    [InlineData(1.05)]
    [InlineData(0.95)]
    [InlineData(1.0)]
    public void Tempo_changes_and_pitch_stays(double rate)
    {
        var output = Stretch(Tone(440, 6), rate);
        var seconds = output.Length / 2 / (double)Rate;
        Assert.InRange(seconds, 6 / rate - 0.15, 6 / rate + 0.05);
        Assert.InRange(Frequency(output, Rate / 10), 437, 443);
        // Steady, without dips where grains join.
        var rms = Math.Sqrt(output.Skip(Rate).Take(Rate * 2).Select(v => (double)v * v).Average());
        Assert.InRange(rms, 0.3, 0.42);
    }

    /// <summary>Something like music: a few tones and a noisy hit every half second.</summary>
    private static float[] Music(double seconds)
    {
        var frames = (int)(seconds * Rate);
        var samples = new float[frames * 2];
        var random = new Random(7);
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)Rate;
            var v = 0.2 * Math.Sin(2 * Math.PI * 220 * t) + 0.15 * Math.Sin(2 * Math.PI * 330.5 * t) + 0.1 * Math.Sin(2 * Math.PI * 523.3 * t);
            var sinceHit = t % 0.5;
            if (sinceHit < 0.05) v += 0.4 * Math.Exp(-sinceHit / 0.01) * (random.NextDouble() * 2 - 1);
            samples[i * 2] = (float)v;
            samples[i * 2 + 1] = (float)(v * 0.9);
        }
        return samples;
    }

    [Fact]
    public void Back_at_its_own_tempo_the_audio_comes_through_exactly()
    {
        var input = Music(8);
        var stretcher = new TimeStretcher(Rate, 2) { Rate = 1.024 };
        var output = new List<float>();
        var buffer = new float[512 * 2];
        var frames = input.Length / 2;
        for (var at = 0; at < frames; at += 512)
        {
            // Matched for the first three seconds, then back to its own tempo.
            if (at > 3 * Rate) stretcher.Rate = 1;
            stretcher.Put(input, at, Math.Min(512, frames - at));
            int got;
            while ((got = stretcher.Take(buffer, 0, 512)) > 0) output.AddRange(buffer.Take(got * 2));
        }
        var result = output.ToArray();
        // A second of output from well after the change, found in the input.
        var start = (result.Length / 2 - Rate * 2) * 2;
        var piece = result.Skip(start).Take(Rate * 2).ToArray();
        double bestError = double.MaxValue;
        for (var shift = 0; shift + piece.Length <= input.Length; shift += 2)
        {
            double error = 0, energy = 0;
            for (var i = 0; i < piece.Length; i += 16)
            {
                var d = piece[i] - input[shift + i];
                error += d * d;
                energy += input[shift + i] * input[shift + i];
            }
            bestError = Math.Min(bestError, error / Math.Max(energy, 1e-9));
        }
        Assert.True(bestError < 1e-6, $"relative error {bestError}");
    }
}
