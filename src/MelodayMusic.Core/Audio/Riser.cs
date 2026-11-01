namespace MelodayMusic.Core.Audio;

// Band-passed noise that sweeps upward into a drop, layered over the mix during a build.
public sealed class Riser
{
    public const float Peak = 0.15f;
    private const double Resonance = 0.7;

    private readonly int _rate;
    private readonly int _channels;
    private readonly double[] _ic1, _ic2;
    private uint _seed = 0x9E3779B9;
    private float _levelNow;

    public Riser(int rate, int channels)
    {
        _rate = rate;
        _channels = channels;
        _ic1 = new double[channels];
        _ic2 = new double[channels];
    }

    public bool IsSounding => _levelNow > 0;

    // Adds the riser to the buffer at a level from 0 to 1, centred on hz.
    public void Process(float[] buffer, int offset, int frames, double level, double hz)
    {
        var to = (float)Math.Clamp(level, 0, 1) * Peak;
        var from = _levelNow;
        _levelNow = to;
        if (from <= 0 && to <= 0)
        {
            Array.Clear(_ic1);
            Array.Clear(_ic2);
            return;
        }
        // A trapezoidal state-variable band-pass, steady under a moving centre.
        var g = Math.Tan(Math.PI * Math.Clamp(hz, 40, _rate * 0.45) / _rate);
        var a1 = 1 / (1 + g * (g + Resonance));
        var a2 = g * a1;
        var a3 = g * a2;
        for (var i = 0; i < frames; i++)
        {
            var l = from + (to - from) * (i + 1) / frames;
            for (var c = 0; c < _channels; c++)
            {
                var x = Noise();
                var v3 = x - _ic2[c];
                var v1 = a1 * _ic1[c] + a2 * v3;
                var v2 = _ic2[c] + a2 * _ic1[c] + a3 * v3;
                _ic1[c] = 2 * v1 - _ic1[c];
                _ic2[c] = 2 * v2 - _ic2[c];
                buffer[offset + i * _channels + c] += (float)(Resonance * v1) * l;
            }
        }
    }

    public void Reset()
    {
        _levelNow = 0;
        Array.Clear(_ic1);
        Array.Clear(_ic2);
    }

    private double Noise()
    {
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return _seed / (double)uint.MaxValue * 2 - 1;
    }
}
