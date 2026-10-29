namespace MelodayMusic.Core.Audio;

public static class ProjectMAudio
{
    public const int SampleRate = 44100;

    public static int CopyWindow(ReadOnlySpan<float> heard, int sourceRate, Span<float> destination) => Copy(heard, sourceRate, destination, 1);

    public static int CopyStereoWindow(ReadOnlySpan<float> heard, int sourceRate, Span<float> destination) => Copy(heard, sourceRate, destination, 2);

    private static int Copy(ReadOnlySpan<float> heard, int sourceRate, Span<float> destination, int channels)
    {
        destination.Clear();
        if (sourceRate <= 0 || heard.IsEmpty || destination.IsEmpty) return 0;
        var step = sourceRate / (double)SampleRate;
        var frames = heard.Length / channels;
        var count = Math.Min(destination.Length / channels, (int)Math.Floor(frames / step));
        var start = frames - count * step;
        for (var i = 0; i < count; i++)
        {
            var at = start + i * step;
            var lo = Math.Min(frames - 1, (int)at);
            var hi = Math.Min(frames - 1, lo + 1);
            for (var channel = 0; channel < channels; channel++)
            {
                var a = heard[lo * channels + channel];
                var b = heard[hi * channels + channel];
                var value = a + (b - a) * (float)(at - lo);
                destination[i * channels + channel] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
            }
        }
        return count;
    }
}
