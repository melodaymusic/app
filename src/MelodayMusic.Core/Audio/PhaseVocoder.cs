namespace MelodayMusic.Core.Audio;

public sealed class PhaseVocoder
{
    private const int Size = 4096;
    private const int Hop = Size / 4;
    private const int Bins = Size / 2 + 1;

    private readonly int _channels;
    private readonly double[] _window = new double[Size];
    private readonly double[] _cos = new double[Size / 2];
    private readonly double[] _sin = new double[Size / 2];
    private readonly int[] _bitReverse = new int[Size];
    private readonly double[][] _re, _im, _phase;
    private readonly double[] _midMag = new double[Bins];
    private readonly double[] _midPhase = new double[Bins];
    private readonly double[] _lastMidPhase = new double[Bins];
    private readonly double[] _lastMidMag = new double[Bins];
    private readonly double[] _synthPhase = new double[Bins];
    private readonly int[] _peakOf = new int[Bins];
    private readonly double[] _accumulator;

    private float[] _input = new float[1 << 16];
    private int _inputLength;
    private double _position;
    private int _lastStart;
    private bool _hasLast;
    private float[] _output = new float[1 << 15];
    private int _outputLength;
    private int _discard;
    private double _fluxAverage;

    public PhaseVocoder(int channels)
    {
        _channels = channels;
        for (var i = 0; i < Size; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Size);
        for (var i = 0; i < Size / 2; i++)
        {
            _cos[i] = Math.Cos(-2 * Math.PI * i / Size);
            _sin[i] = Math.Sin(-2 * Math.PI * i / Size);
        }
        var bits = (int)Math.Log2(Size);
        for (var i = 0; i < Size; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _bitReverse[i] = r;
        }
        _re = new double[channels][];
        _im = new double[channels][];
        _phase = new double[channels][];
        for (var c = 0; c < channels; c++)
        {
            _re[c] = new double[Size];
            _im[c] = new double[Size];
            _phase[c] = new double[Bins];
        }
        _accumulator = new double[Size * channels];
    }

    public double Rate { get; set; } = 1;

    public int Wanted => Math.Max(0, (int)Math.Ceiling(_position) + Size + Hop * 2 - _inputLength);

    public void Clear()
    {
        _inputLength = 0;
        _position = 0;
        _hasLast = false;
        _outputLength = 0;
        _discard = 0;
        _fluxAverage = 0;
        Array.Clear(_accumulator);
    }

    // Seed the analysis window with audio that has already played.
    public void Prime(float[] history, int frames)
    {
        Clear();
        Put(history, 0, frames);
        _discard = frames;
    }

    public void Put(float[] samples, int offset, int frames)
    {
        var need = (_inputLength + frames) * _channels;
        if (need > _input.Length) Array.Resize(ref _input, Math.Max(need, _input.Length * 2));
        Array.Copy(samples, offset * _channels, _input, _inputLength * _channels, frames * _channels);
        _inputLength += frames;
    }

    public int Take(float[] buffer, int offset, int frames)
    {
        while (_outputLength < frames && Step()) { }
        var n = Math.Min(frames, _outputLength);
        Array.Copy(_output, 0, buffer, offset * _channels, n * _channels);
        Array.Copy(_output, n * _channels, _output, 0, (_outputLength - n) * _channels);
        _outputLength -= n;
        return n;
    }

    private bool Step()
    {
        var start = (int)Math.Round(_position);
        if (start + Size > _inputLength) return false;
        var priming = _discard > 0;
        var rate = priming ? 1 : Rate;
        var hop = _hasLast ? start - _lastStart : Hop;

        for (var c = 0; c < _channels; c++)
        {
            var re = _re[c];
            var im = _im[c];
            for (var i = 0; i < Size; i++)
            {
                var j = _bitReverse[i];
                re[j] = _input[(start + i) * _channels + c] * _window[i];
                im[j] = 0;
            }
            Transform(re, im);
        }

        // Decide phases on the mid channel so left and right stay aligned.
        double flux = 0, total = 0;
        for (var k = 0; k < Bins; k++)
        {
            double sr = 0, si = 0;
            for (var c = 0; c < _channels; c++) { sr += _re[c][k]; si += _im[c][k]; }
            var mag = Math.Sqrt(sr * sr + si * si);
            _midMag[k] = mag;
            _midPhase[k] = Math.Atan2(si, sr);
            var rise = mag - _lastMidMag[k];
            if (rise > 0) flux += rise;
            total += mag;
        }
        var fluxRatio = total > 1e-9 ? flux / total : 0;
        var transient = _hasLast && fluxRatio > 0.12 && fluxRatio > _fluxAverage * 2.5;
        _fluxAverage = _fluxAverage * 0.9 + fluxRatio * 0.1;

        var exact = !_hasLast || transient || Math.Abs(rate - 1) < 1e-9 && hop == Hop;
        if (exact)
        {
            Array.Copy(_midPhase, _synthPhase, Bins);
        }
        else
        {
            for (var k = 0; k < Bins; k++)
            {
                var omega = 2 * Math.PI * k / Size;
                var deviation = Wrap(_midPhase[k] - _lastMidPhase[k] - omega * hop);
                _synthPhase[k] = Wrap(_synthPhase[k] + (omega + deviation / hop) * Hop);
            }
            LockToPeaks();
        }

        for (var c = 0; c < _channels; c++)
        {
            var re = _re[c];
            var im = _im[c];
            for (var k = 0; k < Bins; k++)
            {
                var mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                var theta = Math.Atan2(im[k], re[k]) + _synthPhase[k] - _midPhase[k];
                re[k] = mag * Math.Cos(theta);
                im[k] = mag * Math.Sin(theta);
            }
            for (var k = 1; k < Size / 2; k++)
            {
                re[Size - k] = re[k];
                im[Size - k] = -im[k];
            }
            Inverse(re, im);
            for (var i = 0; i < Size; i++) _accumulator[i * _channels + c] += re[i] * _window[i] / (Size * 1.5);
        }

        Array.Copy(_midPhase, _lastMidPhase, Bins);
        Array.Copy(_midMag, _lastMidMag, Bins);
        _lastStart = start;
        _hasLast = true;
        _position += Hop * rate;

        var emit = Hop;
        var skip = Math.Min(_discard, emit);
        _discard -= skip;
        var keep = emit - skip;
        var need = (_outputLength + keep) * _channels;
        if (need > _output.Length) Array.Resize(ref _output, Math.Max(need, _output.Length * 2));
        for (var i = 0; i < keep * _channels; i++) _output[_outputLength * _channels + i] = (float)_accumulator[skip * _channels + i];
        _outputLength += keep;
        Array.Copy(_accumulator, emit * _channels, _accumulator, 0, (Size - emit) * _channels);
        Array.Clear(_accumulator, (Size - emit) * _channels, emit * _channels);

        var drop = Math.Max(0, (int)Math.Floor(_position) - Hop);
        if (drop > 0)
        {
            Array.Copy(_input, drop * _channels, _input, 0, (_inputLength - drop) * _channels);
            _inputLength -= drop;
            _position -= drop;
            _lastStart -= drop;
        }
        return true;
    }

    // Each partial's neighbours keep their phase relationship to it (identity phase locking).
    private void LockToPeaks()
    {
        var peak = -1;
        for (var k = 0; k < Bins; k++)
        {
            var isPeak = k > 1 && k < Bins - 2 && _midMag[k] > _midMag[k - 1] && _midMag[k] >= _midMag[k + 1]
                         && _midMag[k] > _midMag[k - 2] && _midMag[k] >= _midMag[k + 2];
            if (isPeak) peak = k;
            _peakOf[k] = peak;
        }
        var next = -1;
        for (var k = Bins - 1; k >= 0; k--)
        {
            if (_peakOf[k] == k) next = k;
            var before = _peakOf[k];
            var owner = before < 0 ? next : next < 0 ? before : (k - before <= next - k ? before : next);
            _peakOf[k] = owner;
        }
        for (var k = 0; k < Bins; k++)
        {
            var p = _peakOf[k];
            if (p < 0 || p == k) continue;
            _scratch[k] = _synthPhase[p] + _midPhase[k] - _midPhase[p];
        }
        for (var k = 0; k < Bins; k++)
        {
            var p = _peakOf[k];
            if (p >= 0 && p != k) _synthPhase[k] = _scratch[k];
        }
    }

    private readonly double[] _scratch = new double[Bins];

    private static double Wrap(double phase) => phase - 2 * Math.PI * Math.Round(phase / (2 * Math.PI));

    private void Transform(double[] re, double[] im)
    {
        for (var len = 2; len <= Size; len <<= 1)
        {
            var half = len / 2;
            var step = Size / len;
            for (var i = 0; i < Size; i += len)
                for (var j = 0; j < half; j++)
                {
                    var wr = _cos[j * step];
                    var wi = _sin[j * step];
                    var a = i + j;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
        }
    }

    private void Inverse(double[] re, double[] im)
    {
        for (var i = 0; i < Size; i++)
        {
            var j = _bitReverse[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
            im[i] = -im[i];
        }
        Transform(re, im);
    }
}
