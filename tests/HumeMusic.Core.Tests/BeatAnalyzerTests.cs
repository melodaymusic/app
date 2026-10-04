using HumeMusic.Core.Audio;

namespace HumeMusic.Core.Tests;

public class BeatAnalyzerTests
{
    private const int Rate = 11025;

    /// <summary>A drum pattern: a kick on every beat (louder on the first of each bar) and a hat between.</summary>
    private static float[] Drums(double bpm, double seconds, double start = 0.1)
    {
        var samples = new float[(int)(seconds * Rate)];
        var random = new Random(4);
        var period = 60 / bpm;
        var beat = 0;
        for (var t = start; t < seconds - 0.2; t += period, beat++)
        {
            var at = (int)(t * Rate);
            var accent = beat % 4 == 0 ? 1.0 : 0.55;
            // Kick: a falling low tone.
            for (var i = 0; i < Rate / 8 && at + i < samples.Length; i++)
            {
                var env = Math.Exp(-i / (Rate * 0.03));
                var freq = 60 + 80 * Math.Exp(-i / (Rate * 0.01));
                samples[at + i] += (float)(accent * env * Math.Sin(2 * Math.PI * freq * i / Rate));
            }
            // Hat on the off-beat: a short burst of noise.
            var off = (int)((t + period / 2) * Rate);
            for (var i = 0; i < Rate / 40 && off + i < samples.Length; i++)
                samples[off + i] += (float)(0.2 * Math.Exp(-i / (Rate * 0.004)) * (random.NextDouble() * 2 - 1));
        }
        return samples;
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(128)]
    [InlineData(140)]
    [InlineData(174)]
    public void The_tempo_of_a_steady_beat_is_found(double bpm)
    {
        var analysis = BeatAnalyzer.Analyse(Drums(bpm, 30), Rate);
        // Half or double counts: a song at 174 is as well mixed at 87 (every other beat lines up).
        var found = analysis.Bpm < bpm * 0.75 ? analysis.Bpm * 2 : analysis.Bpm > bpm * 1.5 ? analysis.Bpm / 2 : analysis.Bpm;
        Assert.InRange(found, bpm - 1, bpm + 1);
        Assert.True(analysis.HasBeat);
    }

    [Fact]
    public void Beats_land_on_the_kicks_and_bars_on_the_accents()
    {
        const double bpm = 124;
        const double start = 0.3;
        var analysis = BeatAnalyzer.Analyse(Drums(bpm, 30, start), Rate, offset: 100);
        var period = 60 / bpm;
        // Every beat within a few hundredths of a second of a kick (the analysis was told the stretch began at 100 s).
        foreach (var beat in analysis.Beats.Skip(1).SkipLast(1))
        {
            var sinceStart = beat - 100 - start;
            var nearest = Math.Round(sinceStart / period) * period;
            Assert.InRange(sinceStart - nearest, -0.035, 0.035);
        }
        // The bar lines are the accented kicks (every fourth from the first).
        foreach (var downbeat in analysis.Downbeats.Take(6))
        {
            var beatNumber = (int)Math.Round((downbeat - 100 - start) / period);
            Assert.Equal(0, beatNumber % 4);
        }
    }

    private static float[] Chords(int[][] chords, double secondsEach)
    {
        var samples = new float[(int)(chords.Length * secondsEach * Rate)];
        for (var c = 0; c < chords.Length; c++)
            foreach (var midi in chords[c])
            {
                var freq = 440 * Math.Pow(2, (midi - 69) / 12.0);
                for (var i = 0; i < secondsEach * Rate; i++)
                {
                    var at = (int)(c * secondsEach * Rate) + i;
                    samples[at] += (float)(0.2 * Math.Sin(2 * Math.PI * freq * i / Rate) + 0.06 * Math.Sin(4 * Math.PI * freq * i / Rate));
                }
            }
        return samples;
    }

    [Fact]
    public void The_key_of_a_progression_is_found()
    {
        // A minor: Am - F - C - G - Am - E - Am, with the tonic heard most.
        int[][] aMinor = [[57, 60, 64], [53, 57, 60], [60, 64, 67], [55, 59, 62], [57, 60, 64], [52, 56, 59], [57, 60, 64]];
        Assert.Equal(12 + 9, BeatAnalyzer.Analyse(Chords(aMinor, 3), Rate).Key);
        // G major: G - C - D - G - Em - D - G.
        int[][] gMajor = [[55, 59, 62], [60, 64, 67], [62, 66, 69], [55, 59, 62], [52, 55, 59], [62, 66, 69], [55, 59, 62]];
        Assert.Equal(7, BeatAnalyzer.Analyse(Chords(gMajor, 3), Rate).Key);
    }

    [Theory]
    [InlineData(0, 8, true)]   // C major is 8B
    [InlineData(7, 9, true)]   // G major is 9B
    [InlineData(21, 8, false)] // A minor is 8A
    [InlineData(16, 9, false)] // E minor is 9A
    public void Keys_have_their_place_on_the_camelot_wheel(int key, int number, bool major) =>
        Assert.Equal((number, major), AudioAnalysis.Camelot(key));

    [Fact]
    public void Neighbouring_keys_mix_and_distant_ones_clash()
    {
        Assert.Equal(1, AudioAnalysis.KeyHarmony(0, 21));      // C major and A minor (relative)
        Assert.True(AudioAnalysis.KeyHarmony(0, 7) > 0.8);      // C and G
        Assert.True(AudioAnalysis.KeyHarmony(0, 6) < 0.3);      // C and F# (a tritone)
    }
}
