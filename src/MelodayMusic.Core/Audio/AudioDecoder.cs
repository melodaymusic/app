using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MelodayMusic.Core.Audio;

public static class AudioDecoder
{
    public const int AnalysisRate = 11025;
    private static int _started;

    private static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) MediaFoundationApi.Startup();
    }

    public static float[] ReadWindow(string source, TimeSpan from, TimeSpan length, CancellationToken ct = default, int sampleRate = AnalysisRate)
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
        if (samples.WaveFormat.SampleRate != sampleRate) samples = new WdlResamplingSampleProvider(samples, sampleRate);

        var wanted = (int)(length.TotalSeconds * sampleRate);
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
