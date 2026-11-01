namespace MelodayMusic.Core.Audio;

// Freeverb: eight damped combs into four allpasses a side, the right side tuned a little apart for width.
// It works as a send: Process feeds part of the buffer in and adds the tail back, so the tail outlives the song that fed it.
public sealed class Reverb
{
    private static readonly int[] CombTuning = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllpassTuning = [556, 441, 341, 225];
    private const int Spread = 23;
    private const float InputGain = 0.015f;
    private const float WetGain = 3f;
    private const float Silence = 1e-5f;

    // Below this the tail turns to mud under the next song's bass.
    public const double LowCut = 280;

    private readonly int _channels;
    private readonly Comb[] _combL, _combR;
    private readonly Allpass[] _allL, _allR;
    private readonly float _lowCoef;
    private float _lowState;
    private float _sendNow;

    public Reverb(int rate, int channels, double size = 0.85, double damping = 0.45)
    {
        _channels = channels;
        var scale = rate / 44100.0;
        var feedback = (float)(Math.Clamp(size, 0, 1) * 0.28 + 0.7);
        var damp = (float)(Math.Clamp(damping, 0, 1) * 0.4);
        _combL = CombTuning.Select(t => new Comb((int)(t * scale), feedback, damp)).ToArray();
        _combR = CombTuning.Select(t => new Comb((int)((t + Spread) * scale), feedback, damp)).ToArray();
        _allL = AllpassTuning.Select(t => new Allpass((int)(t * scale))).ToArray();
        _allR = AllpassTuning.Select(t => new Allpass((int)((t + Spread) * scale))).ToArray();
        _lowCoef = (float)(1 - Math.Exp(-2 * Math.PI * LowCut / rate));
    }

    // Returns whether the tail is still sounding, so an idle reverb can be skipped.
    public bool Process(float[] buffer, int frames, float send)
    {
        var from = _sendNow;
        var peak = 0f;
        for (var i = 0; i < frames; i++)
        {
            var s = from + (send - from) * (i + 1) / frames;
            var k = i * _channels;
            var dry = _channels == 2 ? (buffer[k] + buffer[k + 1]) * 0.5f : buffer[k];
            _lowState += _lowCoef * (dry - _lowState);
            var input = (dry - _lowState) * s * InputGain;

            float left = 0, right = 0;
            for (var c = 0; c < _combL.Length; c++) left += _combL[c].Run(input);
            for (var a = 0; a < _allL.Length; a++) left = _allL[a].Run(left);
            if (_channels == 2)
            {
                for (var c = 0; c < _combR.Length; c++) right += _combR[c].Run(input);
                for (var a = 0; a < _allR.Length; a++) right = _allR[a].Run(right);
                buffer[k] += left * WetGain;
                buffer[k + 1] += right * WetGain;
                peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
            }
            else
            {
                for (var c = 1; c < _channels; c++) buffer[k + c] += left * WetGain;
                buffer[k] += left * WetGain;
                peak = Math.Max(peak, Math.Abs(left));
            }
        }
        _sendNow = send;
        return send > 0 || from > 0 || peak * WetGain > Silence;
    }

    public void Reset()
    {
        foreach (var c in _combL.Concat(_combR)) c.Reset();
        foreach (var a in _allL.Concat(_allR)) a.Reset();
        _lowState = 0;
        _sendNow = 0;
    }

    private sealed class Comb(int length, float feedback, float damp)
    {
        private readonly float[] _buffer = new float[Math.Max(1, length)];
        private int _at;
        private float _store;

        public float Run(float input)
        {
            var output = _buffer[_at];
            _store = output * (1 - damp) + _store * damp;
            if (Math.Abs(_store) < 1e-20f) _store = 0;
            _buffer[_at] = input + _store * feedback;
            if (++_at == _buffer.Length) _at = 0;
            return output;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _store = 0;
            _at = 0;
        }
    }

    private sealed class Allpass(int length)
    {
        private readonly float[] _buffer = new float[Math.Max(1, length)];
        private int _at;

        public float Run(float input)
        {
            var held = _buffer[_at];
            var output = held - input;
            var next = input + held * 0.5f;
            _buffer[_at] = Math.Abs(next) < 1e-20f ? 0 : next;
            if (++_at == _buffer.Length) _at = 0;
            return output;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _at = 0;
        }
    }
}
