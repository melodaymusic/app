namespace MelodayMusic.Core.Audio;

public sealed class SampleRing
{
    private const int Size = 1 << 16;
    private const int Mask = Size - 1;

    private readonly float[] _samples = new float[Size];
    private readonly float[] _left = new float[Size];
    private readonly float[] _right = new float[Size];
    private long _written;

    public long Written => Volatile.Read(ref _written);

    public void WriteStereo(float[] buffer, int offset, int count)
    {
        var at = _written;
        for (var i = offset; i + 1 < offset + count; i += 2)
        {
            _left[at & Mask] = buffer[i];
            _right[at & Mask] = buffer[i + 1];
            _samples[at++ & Mask] = (buffer[i] + buffer[i + 1]) * 0.5f;
        }
        Volatile.Write(ref _written, at);
    }

    public bool CopyHeard(float[] destination, long delaySamples)
    {
        var end = Written - Math.Max(0, delaySamples);
        var start = end - destination.Length;
        if (start < 0 || Written - start > Size - 2048) return false;
        for (var i = 0; i < destination.Length; i++) destination[i] = _samples[(start + i) & Mask];
        return Written - start <= Size - 1024;
    }

    public bool CopyHeardStereo(float[] destination, long delaySamples)
    {
        if (destination.Length == 0 || destination.Length % 2 != 0) return false;
        var frames = destination.Length / 2;
        var end = Written - Math.Max(0, delaySamples);
        var start = end - frames;
        if (start < 0 || Written - start > Size - 2048) return false;
        for (var i = 0; i < frames; i++)
        {
            destination[2 * i] = _left[(start + i) & Mask];
            destination[2 * i + 1] = _right[(start + i) & Mask];
        }
        return Written - start <= Size - 1024;
    }
}
