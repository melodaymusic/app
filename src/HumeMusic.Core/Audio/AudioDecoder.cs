using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HumeMusic.Core.Audio;

/// <summary>
/// Reads a stretch of a song as plain samples, for analysing it: from a file or from the server
/// (Windows' Media Foundation decodes it and seeks with ranged requests, so only the stretch asked
/// for is fetched), mixed to mono at a low sample rate.
/// </summary>
public static class AudioDecoder
{
    public const int AnalysisRate = 11025;
    private static int _started;

    private static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) MediaFoundationApi.Startup();
    }

    /// <summary>
    /// Mono samples at <see cref="AnalysisRate"/> from <paramref name="from"/> for up to
    /// <paramref name="length"/>. Throws when the source can't be opened or decoded.
    /// </summary>
    public static float[] ReadWindow(string source, TimeSpan from, TimeSpan length, CancellationToken ct = default)
    {
        Start();
        using var reader = new MediaFoundationReader(source, new MediaFoundationReader.MediaFoundationReaderSettings
        {
            RequestFloatOutput = true,
            RepositionInRead = true,
        });
        if (from > TimeSpan.Zero) reader.CurrentTime = from;
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1) samples = new StereoToMono(samples);
        if (samples.WaveFormat.SampleRate != AnalysisRate) samples = new WdlResamplingSampleProvider(samples, AnalysisRate);

        var wanted = (int)(length.TotalSeconds * AnalysisRate);
        var result = new float[wanted];
        var read = 0;
        while (read < wanted)
        {
            ct.ThrowIfCancellationRequested();
            var got = samples.Read(result, read, Math.Min(8192, wanted - read));
            if (got <= 0) break;
            read += got;
        }
        if (read < wanted) Array.Resize(ref result, read);
        return result;
    }

    /// <summary>Mixes any number of channels down to one.</summary>
    private sealed class StereoToMono(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var needed = count * _channels;
            if (_buffer.Length < needed) _buffer = new float[needed];
            var got = source.Read(_buffer, 0, needed) / _channels;
            for (var i = 0; i < got; i++)
            {
                float sum = 0;
                for (var c = 0; c < _channels; c++) sum += _buffer[i * _channels + c];
                buffer[offset + i] = sum / _channels;
            }
            return got;
        }
    }
}
