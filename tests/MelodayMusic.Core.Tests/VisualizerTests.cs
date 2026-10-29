using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class VisualizerTests
{
    private const int Rate = 48000;

    private static float[] Sine(double hz, double amplitude = 0.5, int samples = SpectrumAnalyzer.FftSize) =>
        Enumerable.Range(0, samples).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate))).ToArray();

    private static int Loudest(float[] bands) => Array.IndexOf(bands, bands.Max());

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
        analyzer.Update(new float[100], 1 / 30.0);
        Assert.All(analyzer.Wave, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void The_ring_gives_back_what_was_written_delayed_by_what_the_speakers_still_hold()
    {
        var ring = new SampleRing();
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
        ring.WriteStereo(new float[2 * 70000], 0, 2 * 70000);
        Assert.True(ring.CopyHeard(window, 100));
        Assert.False(ring.CopyHeard(window, 70000));
    }

    [Fact]
    public void Every_picture_has_a_name_and_a_description_and_off_is_the_default()
    {
        Assert.Equal(14, VisualizerStyles.All.Count);
        Assert.Equal(Enum.GetValues<VisualizerStyle>().Distinct().OrderBy(s => s), VisualizerStyles.All.OrderBy(s => s));
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
    [InlineData("Ripples", VisualizerStyle.Sun)]
    [InlineData("Spiral", VisualizerStyle.Wave)]
    public void Settings_that_name_the_old_pictures_still_read(string saved, VisualizerStyle expected)
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var settings = System.Text.Json.JsonSerializer.Deserialize<Core.Settings.AppSettings>($"{{\"Visualizer\":\"{saved}\"}}", options)!;
        Assert.Equal(expected, settings.Visualizer);
        var again = System.Text.Json.JsonSerializer.Deserialize<Core.Settings.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings, options), options)!;
        Assert.Equal(expected, again.Visualizer);
    }

    [Fact]
    public void The_pictures_are_offered_once_each_under_their_new_names()
    {
        Assert.Equal(["Off", "Bars", "Halo", "M-Wave", "Sun", "Pillars", "Tiles", "Lyric Video", "Lyric Glow", "Airhandler", "Neon Space", "Spiral Artifact", "Cosmic Dust", "Molten Glass"], VisualizerStyles.All.Select(VisualizerStyles.Name));
    }

    [Fact]
    public void ProjectM_presets_have_distinct_files_and_do_not_change_saved_style_numbers()
    {
        Assert.Equal(6, (int)VisualizerStyle.LyricGlow);
        Assert.Equal([12, 15], new[] { VisualizerStyle.Pillars, VisualizerStyle.Tiles }.Select(s => (int)s));
        var presets = VisualizerStyles.All.Where(VisualizerStyles.IsProjectM).Select(VisualizerStyles.PresetFile).ToArray();
        Assert.Equal(5, presets.Length);
        Assert.Equal(5, presets.Distinct().Count());
        Assert.All(presets, file => Assert.EndsWith(".milk", file));
        Assert.Null(VisualizerStyles.PresetFile(VisualizerStyle.Wave));
    }

    [Fact]
    public void ProjectM_audio_uses_the_latest_window_and_clears_silence()
    {
        var pcm = new float[512];
        var ramp = Enumerable.Range(0, 2048).Select(i => i / 2048f).ToArray();
        Assert.Equal(512, ProjectMAudio.CopyWindow(ramp, 44100, pcm));
        Assert.Equal(ramp[^512], pcm[0]);
        Assert.Equal(ramp[^1], pcm[^1]);
        Assert.Equal(0, ProjectMAudio.CopyWindow([], 48000, pcm));
        Assert.All(pcm, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void ProjectM_preserves_stereo_and_the_speakers_delay()
    {
        var ring = new SampleRing();
        var stereo = Enumerable.Range(0, 4000).SelectMany(i => new float[] { i, -i }).ToArray();
        ring.WriteStereo(stereo, 0, stereo.Length);
        var heard = new float[1024];
        Assert.True(ring.CopyHeardStereo(heard, 100));
        Assert.Equal(4000 - 100 - 512, heard[0]);
        Assert.Equal(-heard[0], heard[1]);
        Assert.Equal(3899, heard[^2]);
        Assert.False(ring.CopyHeardStereo(new float[3], 0));
        Assert.False(ring.CopyHeardStereo(heard, 4000));
        var pcm = new float[1024];
        var opposite = Enumerable.Range(0, 2048).SelectMany(i => new float[] { 0.4f, -0.4f }).ToArray();
        Assert.Equal(512, ProjectMAudio.CopyStereoWindow(opposite, 48000, pcm));
        for (var i = 0; i < 512; i++)
        {
            Assert.Equal(0.4f, pcm[2 * i]);
            Assert.Equal(-0.4f, pcm[2 * i + 1]);
        }
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(96000)]
    public void ProjectM_audio_resampling_preserves_pitch(int rate)
    {
        var source = Enumerable.Range(0, 4096).Select(i => (float)(0.6 * Math.Sin(2 * Math.PI * 440 * i / rate))).ToArray();
        var pcm = new float[1024];
        var count = ProjectMAudio.CopyWindow(source, rate, pcm);
        Assert.Equal(pcm.Length, count);
        var crossings = Enumerable.Range(1, count - 1).Count(i => pcm[i - 1] <= 0 && pcm[i] > 0);
        Assert.InRange(crossings, 9, 11);
        Assert.InRange(pcm.Max(), 0.59f, 0.61f);
    }
}
