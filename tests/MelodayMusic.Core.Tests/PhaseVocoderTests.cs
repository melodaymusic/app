using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Tests;

public class PhaseVocoderTests
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
        var stretcher = new PhaseVocoder(2) { Rate = rate };
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
        var rms = Math.Sqrt(output.Skip(Rate).Take(Rate * 2).Select(v => (double)v * v).Average());
        Assert.InRange(rms, 0.3, 0.42);
    }

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
        var stretcher = new PhaseVocoder(2) { Rate = 1.024 };
        var output = new List<float>();
        var buffer = new float[512 * 2];
        var frames = input.Length / 2;
        for (var at = 0; at < frames; at += 512)
        {
            if (at > 3 * Rate) stretcher.Rate = 1;
            stretcher.Put(input, at, Math.Min(512, frames - at));
            int got;
            while ((got = stretcher.Take(buffer, 0, 512)) > 0) output.AddRange(buffer.Take(got * 2));
        }
        var result = output.ToArray();
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

    private static double NoiseBetween(float[] stereo, double[] notes)
    {
        const int n = 1 << 15;
        var start = stereo.Length / 4 - n / 2;
        var re = new double[n];
        var im = new double[n];
        for (var i = 0; i < n; i++) re[i] = stereo[(start + i) * 2] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (re[i], re[j]) = (re[j], re[i]);
        }
        for (var len = 2; len <= n; len <<= 1)
            for (var i = 0; i < n; i += len)
                for (var k = 0; k < len / 2; k++)
                {
                    var a = -2 * Math.PI * k / len;
                    var (wr, wi) = (Math.Cos(a), Math.Sin(a));
                    var (ur, ui) = (re[i + k], im[i + k]);
                    var vr = re[i + k + len / 2] * wr - im[i + k + len / 2] * wi;
                    var vi = re[i + k + len / 2] * wi + im[i + k + len / 2] * wr;
                    (re[i + k], im[i + k]) = (ur + vr, ui + vi);
                    (re[i + k + len / 2], im[i + k + len / 2]) = (ur - vr, ui - vi);
                }
        double tone = 0, rest = 0;
        for (var k = 1; k < n / 2; k++)
        {
            var f = k * (double)Rate / n;
            if (f < 100 || f > 4000) continue;
            var p = re[k] * re[k] + im[k] * im[k];
            if (notes.Any(x => Math.Abs(x - f) < 8)) tone += p; else rest += p;
        }
        return 10 * Math.Log10(rest / tone);
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(0.926)]
    [InlineData(1.08)]
    public void A_chord_stays_clean_when_stretched(double rate)
    {
        double[] notes = [220, 277.18, 329.63, 415.3, 554.37, 880];
        var frames = Rate * 6;
        var chord = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(notes.Sum(f => Math.Sin(2 * Math.PI * f * i / Rate)) / notes.Length * 0.8);
            chord[i * 2] = chord[i * 2 + 1] = v;
        }
        var noise = NoiseBetween(Stretch(chord, rate), notes);
        Assert.True(noise < -40, $"noise {noise:0.0} dB");
    }

    [Fact]
    public void Starting_to_stretch_carries_straight_on_from_what_was_playing()
    {
        var source = Tone(220, 4);
        var read = 0;
        var voice = new DeckVoice(Rate, 2, (into, n) =>
        {
            var take = Math.Min(n, source.Length / 2 - read);
            Array.Copy(source, read * 2, into, 0, take * 2);
            read += take;
            return take;
        });
        var output = new List<float>();
        var block = new float[256 * 2];
        for (var i = 0; i < 600; i++)
        {
            if (i == 300) voice.Rate = 0.95;
            voice.Read(block, 256);
            output.AddRange(block);
        }
        var at = 300 * 256;
        var biggestStep = 0.0;
        for (var i = at - 64; i < at + 64; i++) biggestStep = Math.Max(biggestStep, Math.Abs(output[(i + 1) * 2] - output[i * 2]));
        var expected = 0.5 * 2 * Math.PI * 220 / Rate;
        Assert.True(biggestStep < expected * 1.5, $"step {biggestStep:0.0000} against {expected:0.0000}");
    }
}
