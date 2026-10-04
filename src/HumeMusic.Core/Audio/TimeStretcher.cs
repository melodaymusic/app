namespace HumeMusic.Core.Audio;

/// <summary>
/// Plays audio faster or slower without changing its pitch (WSOLA: waveform-similarity overlap-add),
/// for matching one song's tempo to another's. Short overlapping grains are taken from the input at
/// the stretched rate, each nudged a little to where it best continues the last, and joined with
/// smooth windows. Interleaved float samples, any number of channels.
/// </summary>
public sealed class TimeStretcher
{
    private readonly int _channels;
    private readonly int _grain;
    private readonly int _hop;
    private readonly int _search;
    private readonly float[] _window;

    // Input not yet used, and output made but not yet read (both interleaved).
    private float[] _input = new float[1 << 16];
    private int _inputLength;
    private double _inputPosition;
    private readonly float[] _overlap;
    private float[] _output = new float[1 << 14];
    private int _outputLength;
    private bool _primed;

    public TimeStretcher(int sampleRate, int channels)
    {
        _channels = channels;
        // Grains of about 40 ms, half overlapping; a search of about 10 ms either way.
        _grain = Math.Max(256, (int)(sampleRate * 0.04));
        if (_grain % 2 == 1) _grain++;
        _hop = _grain / 2;
        _search = Math.Max(32, (int)(sampleRate * 0.01));
        _window = new float[_grain];
        for (var i = 0; i < _grain; i++) _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / _grain));
        _overlap = new float[_hop * channels];
    }

    /// <summary>How much faster than written the audio plays (1 = as written).</summary>
    public double Rate { get; set; } = 1;

    /// <summary>Input frames needed before more output can be made.</summary>
    public int Wanted => Math.Max(0, (int)Math.Ceiling(_inputPosition) + _grain + _search * 2 + _hop * 2 - _inputLength);

    /// <summary>Forgets everything (after a seek).</summary>
    public void Clear()
    {
        _inputLength = 0;
        _inputPosition = 0;
        _outputLength = 0;
        _primed = false;
        _lastBest = 0;
        Array.Clear(_overlap);
    }

    /// <summary>Adds input frames.</summary>
    public void Put(float[] samples, int offset, int frames)
    {
        var needed = (_inputLength + frames) * _channels;
        if (needed > _input.Length) Array.Resize(ref _input, Math.Max(needed, _input.Length * 2));
        Array.Copy(samples, offset * _channels, _input, _inputLength * _channels, frames * _channels);
        _inputLength += frames;
    }

    /// <summary>Takes up to <paramref name="frames"/> output frames; returns how many were available.</summary>
    public int Take(float[] buffer, int offset, int frames)
    {
        while (_outputLength < frames && MakeGrain()) { }
        var n = Math.Min(frames, _outputLength);
        Array.Copy(_output, 0, buffer, offset * _channels, n * _channels);
        Array.Copy(_output, n * _channels, _output, 0, (_outputLength - n) * _channels);
        _outputLength -= n;
        return n;
    }

    private int _lastBest;

    /// <summary>
    /// The candidate in [from, to] whose start looks most like the overlap's natural continuation
    /// (normalised correlation, so loud passages don't win just for being loud).
    /// </summary>
    private int Search(int from, int to, int step, int stride, int fallback)
    {
        var best = fallback;
        var bestScore = double.NegativeInfinity;
        for (var candidate = from; candidate <= to; candidate += step)
        {
            double dot = 0, energy = 1e-9;
            for (var i = 0; i < _hop; i += stride)
                for (var c = 0; c < _channels; c++)
                {
                    var x = _input[(candidate + i) * _channels + c];
                    dot += _overlap[i * _channels + c] * x;
                    energy += x * x;
                }
            var score = dot / Math.Sqrt(energy);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Makes one hop of output from the next grain, if there's enough input.</summary>
    private bool MakeGrain()
    {
        var nominal = (int)Math.Round(_inputPosition);
        if (nominal + _grain + _search * 2 > _inputLength) return false;

        // Where the grain best continues the audio already laid down: the overlap's natural sequel.
        var best = nominal;
        if (_primed && Math.Abs(Rate - 1) < 1e-9)
        {
            // At the written tempo there's nothing to stretch: the next grain simply follows on from
            // the last, and overlapping them gives back the input exactly.
            best = Math.Min(_lastBest + _hop, _inputLength - _grain);
            _inputPosition = best;
        }
        else if (_primed)
        {
            var from = Math.Max(0, nominal - _search);
            var to = Math.Min(_inputLength - _grain, nominal + _search);
            // A coarse look every other sample, then the exact sample around the best.
            best = Search(from, to, 2, 4, nominal);
            best = Search(Math.Max(from, best - 2), Math.Min(to, best + 2), 1, 1, best);
        }
        _lastBest = best;

        // The first half of the grain fades in over the last grain's tail; its second half is kept for next time.
        if (_outputLength + _hop > _output.Length / _channels) Array.Resize(ref _output, _output.Length * 2);
        for (var i = 0; i < _hop; i++)
            for (var c = 0; c < _channels; c++)
            {
                var s = _input[(best + i) * _channels + c] * _window[i];
                _output[(_outputLength + i) * _channels + c] = _primed ? _overlap[i * _channels + c] * _window[i + _hop] + s : _input[(best + i) * _channels + c];
            }
        // The next grain should continue from here: keep the natural continuation of this grain's middle.
        for (var i = 0; i < _hop; i++)
            for (var c = 0; c < _channels; c++)
                _overlap[i * _channels + c] = _input[(best + _hop + i) * _channels + c];
        _outputLength += _hop;
        _primed = true;
        // The next grain is due a stretched hop on from where this one was due (not from where it was
        // nudged to), so the nudges never add up and the tempo is exactly the rate asked for.
        _inputPosition += _hop * Rate;
        // Drop input that's no longer needed.
        var drop = Math.Max(0, (int)Math.Floor(_inputPosition) - _search - 1);
        if (drop > 0)
        {
            Array.Copy(_input, drop * _channels, _input, 0, (_inputLength - drop) * _channels);
            _inputLength -= drop;
            _inputPosition -= drop;
            _lastBest -= drop;
        }
        return true;
    }
}
