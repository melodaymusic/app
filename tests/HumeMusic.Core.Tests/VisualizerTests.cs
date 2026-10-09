using HumeMusic.Core.Audio;
using HumeMusic.Core.Playback;

namespace HumeMusic.Core.Tests;

public class VisualizerTests
{
    private const int Rate = 48000;

    private static float[] Sine(double hz, double amplitude = 0.5, int samples = SpectrumAnalyzer.FftSize) =>
        Enumerable.Range(0, samples).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate))).ToArray();

    private static int Loudest(float[] bands) => Array.IndexOf(bands, bands.Max());

    /// <summary>Lets the smoothing settle on a steady sound.</summary>
    private static void Feed(SpectrumAnalyzer analyzer, float[] samples, int frames = 40)
    {
        for (var i = 0; i < frames; i++) analyzer.Update(samples, 1 / 30.0);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(440)]
    [InlineData(1000)]
    [InlineData(4000)]
    [InlineData(10000)]
    public void A_tone_lights_the_band_it_falls_in(double hz)
    {
        var analyzer = new SpectrumAnalyzer(Rate);
        Feed(analyzer, Sine(hz));
        var band = Loudest(analyzer.Bands);
        Assert.True(analyzer.EdgeHz(band) <= hz * 1.08 && hz / 1.08 <= analyzer.EdgeHz(band + 1), $"{hz} Hz lit band {band} ({analyzer.EdgeHz(band):0}-{analyzer.EdgeHz(band + 1):0} Hz)");
        Assert.True(analyzer.Bands[band] > 0.7f);
    }

    [Fact]
    public void Silence_shows_nothing_and_a_loud_sound_falls_away_when_it_stops()
    {
        var analyzer = new SpectrumAnalyzer(Rate);
        Feed(analyzer, Sine(200));
        Assert.True(analyzer.Level > 0.2f);
        Feed(analyzer, new float[SpectrumAnalyzer.FftSize], frames: 90);
        Assert.All(analyzer.Bands, b => Assert.True(b < 0.02f));
        Assert.True(analyzer.Level < 0.01f);
        Assert.True(analyzer.Bass < 0.02f);
        // No samples at all is silence too.
        analyzer.Update(ReadOnlySpan<float>.Empty, 1 / 30.0);
        Assert.All(analyzer.Bands, b => Assert.True(b < 0.02f));
    }

    [Fact]
    public void Bars_rise_faster_than_they_fall_and_peaks_hang_above_them()
    {
        var analyzer = new SpectrumAnalyzer(Rate);
        var tone = Sine(1000);
        analyzer.Update(tone, 1 / 30.0);
        var band = Loudest(analyzer.Bands);
        var risen = analyzer.Bands[band];
        Assert.True(risen > 0.4f, "the first frame already shows it");
        Feed(analyzer, tone);
        var peak = analyzer.Bands[band];
        analyzer.Update(new float[SpectrumAnalyzer.FftSize], 1 / 30.0);
        Assert.True(analyzer.Bands[band] < peak && analyzer.Bands[band] > peak * 0.6f, "a little falls at a time");
        Assert.True(analyzer.Peaks[band] >= analyzer.Bands[band]);
    }

    [Fact]
    public void Bass_follows_low_notes_not_high_ones()
    {
        var low = new SpectrumAnalyzer(Rate);
        Feed(low, Sine(60));
        var high = new SpectrumAnalyzer(Rate);
        Feed(high, Sine(8000));
        Assert.True(low.Bass > 0.5f);
        Assert.True(high.Bass < 0.1f);
    }

    [Fact]
    public void The_waveform_starts_on_a_rising_crossing_so_it_holds_still()
    {
        var analyzer = new SpectrumAnalyzer(Rate);
        var samples = Sine(220, 0.8, 4096);
        analyzer.Update(samples, 1 / 30.0);
        Assert.True(analyzer.Wave[0] <= 0.05f && analyzer.Wave[1] > analyzer.Wave[0]);
        Assert.InRange(analyzer.Wave.Max(), 0.7f, 0.81f);
        // Fewer samples than a picture needs: a flat line.
        analyzer.Update(new float[100], 1 / 30.0);
        Assert.All(analyzer.Wave, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void The_ring_gives_back_what_was_written_delayed_by_what_the_speakers_still_hold()
    {
        var ring = new SampleRing();
        // Stereo ramp: the left channel counts up, the right the same, so the mono sample is its own index.
        var buffer = new float[2 * 20000];
        for (var i = 0; i < 20000; i++) buffer[2 * i] = buffer[2 * i + 1] = i;
        ring.WriteStereo(buffer, 0, buffer.Length);
        Assert.Equal(20000, ring.Written);
        var window = new float[512];
        Assert.True(ring.CopyHeard(window, 0));
        Assert.Equal(20000 - 512, window[0]);
        Assert.Equal(19999, window[^1]);
        Assert.True(ring.CopyHeard(window, 4800));
        Assert.Equal(20000 - 4800 - 512, window[0]);
    }

    [Fact]
    public void The_ring_says_so_when_there_is_too_little_or_the_reader_has_been_overtaken()
    {
        var ring = new SampleRing();
        var window = new float[1024];
        Assert.False(ring.CopyHeard(window, 0));
        ring.WriteStereo(new float[2 * 500], 0, 2 * 500);
        Assert.False(ring.CopyHeard(window, 0));
        // A delay longer than the ring holds can't be satisfied.
        ring.WriteStereo(new float[2 * 70000], 0, 2 * 70000);
        Assert.True(ring.CopyHeard(window, 100));
        Assert.False(ring.CopyHeard(window, 70000));
    }

    [Fact]
    public void Every_picture_has_a_name_and_a_description_and_off_is_the_default()
    {
        Assert.Equal(7, VisualizerStyles.All.Count);
        Assert.Equal(VisualizerStyle.Off, new Core.Settings.AppSettings().Visualizer);
        foreach (var style in VisualizerStyles.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(VisualizerStyles.Name(style)));
            Assert.False(string.IsNullOrWhiteSpace(VisualizerStyles.Description(style)));
        }
        Assert.Equal(VisualizerStyles.All.Count, VisualizerStyles.All.Select(VisualizerStyles.Name).Distinct().Count());
    }

    [Theory]
    [InlineData("Radial", VisualizerStyle.Halo)]
    [InlineData("Orbs", VisualizerStyle.Sun)]
    [InlineData("Corona", VisualizerStyle.Halo)]
    [InlineData("Halo", VisualizerStyle.Halo)]
    [InlineData("Ridges", VisualizerStyle.Wave)]
    [InlineData("Sun", VisualizerStyle.Sun)]
    [InlineData("Wave", VisualizerStyle.Wave)]
    public void Settings_that_name_the_old_pictures_still_read(string saved, VisualizerStyle expected)
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var settings = System.Text.Json.JsonSerializer.Deserialize<Core.Settings.AppSettings>($"{{\"Visualizer\":\"{saved}\"}}", options)!;
        Assert.Equal(expected, settings.Visualizer);
        // Written back, it reads again.
        var again = System.Text.Json.JsonSerializer.Deserialize<Core.Settings.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings, options), options)!;
        Assert.Equal(expected, again.Visualizer);
    }

    [Fact]
    public void The_pictures_are_offered_once_each_under_their_new_names()
    {
        Assert.Equal(["Off", "Bars", "Halo", "M-Wave", "Sun", "Lyric Video", "Lyric Glow"], VisualizerStyles.All.Select(VisualizerStyles.Name));
    }
}
