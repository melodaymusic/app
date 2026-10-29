namespace MelodayMusic.Core.Audio;

public sealed class HighPass
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly double[] _x1, _x2, _y1, _y2;
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _cutoff = -1;

    public HighPass(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _x1 = new double[channels];
        _x2 = new double[channels];
        _y1 = new double[channels];
        _y2 = new double[channels];
    }

    public double Cutoff
    {
        get => _cutoff;
        set
        {
            value = Math.Clamp(value, 0, _sampleRate * 0.45);
            if (Math.Abs(value - _cutoff) < 0.01) return;
            _cutoff = value;
            if (value <= 0) return;
            var w0 = 2 * Math.PI * value / _sampleRate;
            var alpha = Math.Sin(w0) / (2 * 0.7071);
            var cos = Math.Cos(w0);
            var a0 = 1 + alpha;
            _b0 = (1 + cos) / 2 / a0;
            _b1 = -(1 + cos) / a0;
            _b2 = (1 + cos) / 2 / a0;
            _a1 = -2 * cos / a0;
            _a2 = (1 - alpha) / a0;
        }
    }

    public bool IsActive => _cutoff > 0;

    public void Reset()
    {
        Array.Clear(_x1);
        Array.Clear(_x2);
        Array.Clear(_y1);
        Array.Clear(_y2);
    }

    public void Process(float[] buffer, int offset, int frames)
    {
        if (!IsActive)
        {
            for (var c = 0; c < _channels; c++)
            {
                if (frames == 0) break;
                var last = buffer[offset + (frames - 1) * _channels + c];
                var before = frames > 1 ? buffer[offset + (frames - 2) * _channels + c] : last;
                _x1[c] = last;
                _x2[c] = before;
                _y1[c] = 0;
                _y2[c] = 0;
            }
            return;
        }
        for (var i = 0; i < frames; i++)
            for (var c = 0; c < _channels; c++)
            {
                var at = offset + i * _channels + c;
                var x = (double)buffer[at];
                var y = _b0 * x + _b1 * _x1[c] + _b2 * _x2[c] - _a1 * _y1[c] - _a2 * _y2[c];
                _x2[c] = _x1[c];
                _x1[c] = x;
                _y2[c] = _y1[c];
                _y1[c] = y;
                buffer[at] = (float)y;
            }
    }
}

public sealed class LowPass
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly double[] _x1, _x2, _y1, _y2;
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _cutoff = -1;

    public LowPass(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _x1 = new double[channels];
        _x2 = new double[channels];
        _y1 = new double[channels];
        _y2 = new double[channels];
    }

    public double Cutoff
    {
        get => _cutoff;
        set
        {
            value = Math.Clamp(value, 0, _sampleRate * 0.45);
            if (Math.Abs(value - _cutoff) < 0.01) return;
            _cutoff = value;
            if (value <= 0) return;
            var w0 = 2 * Math.PI * value / _sampleRate;
            var alpha = Math.Sin(w0) / (2 * 0.7071);
            var cos = Math.Cos(w0);
            var a0 = 1 + alpha;
            _b0 = (1 - cos) / 2 / a0;
            _b1 = (1 - cos) / a0;
            _b2 = (1 - cos) / 2 / a0;
            _a1 = -2 * cos / a0;
            _a2 = (1 - alpha) / a0;
        }
    }

    public bool IsActive => _cutoff > 0;

    public void Reset()
    {
        Array.Clear(_x1);
        Array.Clear(_x2);
        Array.Clear(_y1);
        Array.Clear(_y2);
    }

    public void Process(float[] buffer, int offset, int frames)
    {
        if (!IsActive)
        {
            for (var c = 0; c < _channels; c++)
            {
                if (frames == 0) break;
                var last = buffer[offset + (frames - 1) * _channels + c];
                var before = frames > 1 ? buffer[offset + (frames - 2) * _channels + c] : last;
                _x1[c] = _y1[c] = last;
                _x2[c] = _y2[c] = before;
            }
            return;
        }
        for (var i = 0; i < frames; i++)
            for (var c = 0; c < _channels; c++)
            {
                var at = offset + i * _channels + c;
                var x = (double)buffer[at];
                var y = _b0 * x + _b1 * _x1[c] + _b2 * _x2[c] - _a1 * _y1[c] - _a2 * _y2[c];
                _x2[c] = _x1[c];
                _x1[c] = x;
                _y2[c] = _y1[c];
                _y1[c] = y;
                buffer[at] = (float)y;
            }
    }
}
