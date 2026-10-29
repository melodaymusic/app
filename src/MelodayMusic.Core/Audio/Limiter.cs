namespace MelodayMusic.Core.Audio;

public sealed class Limiter
{
    private readonly int _channels;
    private readonly int _lookahead;
    private readonly float _ceiling;
    private readonly float _release;
    private readonly float[] _delay;
    private readonly float[] _targets;
    private readonly long[] _window;
    private int _windowHead, _windowCount;
    private readonly float[] _mins;
    private double _minsSum;
    private long _frame;
    private float _gain;

    public Limiter(int sampleRate, int channels, float ceiling = 0.97f, double lookaheadSeconds = 0.005, double releaseSeconds = 0.2)
    {
        _channels = channels;
        _ceiling = ceiling;
        _lookahead = Math.Max(1, (int)Math.Round(sampleRate * lookaheadSeconds));
        _release = (float)Math.Exp(-1.0 / (releaseSeconds * sampleRate));
        _delay = new float[_lookahead * channels];
        _targets = new float[_lookahead + 1];
        _window = new long[_lookahead + 1];
        _mins = new float[_lookahead];
        Reset();
    }

    public int LatencyFrames => _lookahead;

    public void Reset()
    {
        Array.Clear(_delay);
        Array.Fill(_targets, 1f);
        Array.Fill(_mins, 1f);
        _windowHead = _windowCount = 0;
        _minsSum = _lookahead;
        _frame = 0;
        _gain = 1;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        for (var i = offset; i + _channels <= offset + count; i += _channels)
        {
            var peak = 0f;
            for (var c = 0; c < _channels; c++) peak = Math.Max(peak, Math.Abs(buffer[i + c]));
            var target = peak > _ceiling ? _ceiling / peak : 1f;
            _targets[_frame % _targets.Length] = target;

            while (_windowCount > 0 && _targets[Back() % _targets.Length] >= target) _windowCount--;
            _window[(_windowHead + _windowCount) % _window.Length] = _frame;
            _windowCount++;
            if (_window[_windowHead] < _frame - _lookahead)
            {
                _windowHead = (_windowHead + 1) % _window.Length;
                _windowCount--;
            }
            var lowest = _targets[_window[_windowHead] % _targets.Length];

            var slot = (int)(_frame % _mins.Length);
            _minsSum += lowest - _mins[slot];
            _mins[slot] = lowest;
            var smooth = (float)(_minsSum / _mins.Length);
            _gain = smooth < _gain ? smooth : smooth - (smooth - _gain) * _release;

            var at = (int)(_frame % _lookahead) * _channels;
            for (var c = 0; c < _channels; c++)
            {
                var delayed = _delay[at + c];
                _delay[at + c] = buffer[i + c];
                buffer[i + c] = delayed * _gain;
            }
            _frame++;
        }
    }

    private long Back() => _window[(_windowHead + _windowCount - 1) % _window.Length];
}
