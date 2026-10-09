namespace HumeMusic.Core.Audio;

/// <summary>
/// The last second or so of what the mixer has made, as one mono track, for the visualizers. The audio thread
/// only ever writes into it (a cheap copy); whoever draws reads a window of it, placed so it is what the
/// speakers are playing now rather than what the mixer has just made (the device holds a buffer's worth ahead).
/// No locks: a reader that is overtaken by the writer is told so and tries again a frame later.
/// </summary>
public sealed class SampleRing
{
    private const int Size = 1 << 16;
    private const int Mask = Size - 1;

    private readonly float[] _samples = new float[Size];
    private long _written;

    /// <summary>How many mono samples have been written in all.</summary>
    public long Written => Volatile.Read(ref _written);

    /// <summary>Adds interleaved stereo (the channels averaged to one).</summary>
    public void WriteStereo(float[] buffer, int offset, int count)
    {
        var at = _written;
        for (var i = offset; i + 1 < offset + count; i += 2)
            _samples[at++ & Mask] = (buffer[i] + buffer[i + 1]) * 0.5f;
        Volatile.Write(ref _written, at);
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the samples that ended <paramref name="delaySamples"/> ago. False
    /// when there aren't that many yet, or the writer has gone past them.
    /// </summary>
    public bool CopyHeard(float[] destination, long delaySamples)
    {
        var end = Written - Math.Max(0, delaySamples);
        var start = end - destination.Length;
        // The slack keeps clear of the part the writer is about to overwrite.
        if (start < 0 || Written - start > Size - 2048) return false;
        for (var i = 0; i < destination.Length; i++) destination[i] = _samples[(start + i) & Mask];
        return Written - start <= Size - 1024;
    }
}
