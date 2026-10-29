namespace MelodayMusic.Core.Audio;

public sealed class SpectrumAnalyzer
{
    public const int FftSize = 2048;
    public const int WavePoints = 256;

    private const double FloorDb = -72, CeilingDb = -12;
    private const double Attack = 0.035, Release = 0.20, PeakFall = 0.7;

    private readonly int _rate;
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _cos = new float[FftSize / 2];
    private readonly float[] _sin = new float[FftSize / 2];
    private readonly int[] _reverse = new int[FftSize];
    private readonly double[] _edges;
    private readonly float[] _tilt;

    public SpectrumAnalyzer(int sampleRate, int bands = 64, double minHz = 30, double maxHz = 16000)
    {
        _rate = sampleRate;
        Bands = new float[bands];
        Peaks = new float[bands];
        for (var i = 0; i < FftSize; i++) _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        for (var i = 0; i < FftSize / 2; i++)
        {
            _cos[i] = (float)Math.Cos(-2 * Math.PI * i / FftSize);
            _sin[i] = (float)Math.Sin(-2 * Math.PI * i / FftSize);
        }
        var bits = (int)Math.Log2(FftSize);
        for (var i = 0; i < FftSize; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++) if ((i >> b & 1) != 0) r |= 1 << (bits - 1 - b);
            _reverse[i] = r;
        }
        maxHz = Math.Min(maxHz, sampleRate / 2.0 * 0.95);
        _edges = new double[bands + 1];
        for (var i = 0; i <= bands; i++) _edges[i] = minHz * Math.Pow(maxHz / minHz, i / (double)bands);
        _tilt = new float[bands];
        for (var i = 0; i < bands; i++) _tilt[i] = (float)(3.0 * Math.Log2(Math.Sqrt(_edges[i] * _edges[i + 1]) / 1000.0));
    }

    public float[] Bands { get; }

    public float[] Peaks { get; }

    public float[] Wave { get; } = new float[WavePoints];

    public float Bass { get; private set; }

    public float Level { get; private set; }

    public double EdgeHz(int band) => _edges[band];

    public void Update(ReadOnlySpan<float> samples, double seconds)
    {
        seconds = Math.Clamp(seconds, 0.001, 0.25);
        var have = Math.Min(samples.Length, FftSize);
        var from = samples.Length - have;
        double sumSquares = 0;
        for (var i = 0; i < FftSize; i++)
        {
            var s = i < FftSize - have ? 0f : samples[from + i - (FftSize - have)];
            sumSquares += s * s;
            _re[_reverse[i]] = s * _window[i];
            _im[i] = 0;
        }
        for (var i = 0; i < FftSize; i++) _im[_reverse[i]] = 0;
        Transform();

        var binHz = _rate / (double)FftSize;
        var rise = 1 - Math.Exp(-seconds / Attack);
        var fall = 1 - Math.Exp(-seconds / Release);
        for (var b = 0; b < Bands.Length; b++)
        {
            double lo = _edges[b] / binHz, hi = _edges[b + 1] / binHz;
            double magnitude;
            if (hi - lo < 1)
            {
                var mid = (lo + hi) / 2;
                var k = (int)mid;
                var t = mid - k;
                magnitude = Magnitude(k) * (1 - t) + Magnitude(k + 1) * t;
            }
            else
            {
                magnitude = 0;
                for (var k = (int)Math.Ceiling(lo); k <= (int)Math.Floor(hi); k++) magnitude = Math.Max(magnitude, Magnitude(k));
            }
            var db = 20 * Math.Log10(magnitude / (FftSize / 4.0) + 1e-9) + _tilt[b];
            var target = (float)Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1);
            var now = Bands[b];
            now += (target - now) * (float)(target > now ? rise : fall);
            Bands[b] = now;
            Peaks[b] = Math.Max(now, Peaks[b] - (float)(PeakFall * seconds));
        }

        var bass = 0f;
        var bassBands = Math.Max(1, Bands.Length / 10);
        for (var b = 0; b < bassBands; b++) bass += Bands[b];
        bass /= bassBands;
        Bass += (bass - Bass) * (float)(bass > Bass ? 1 - Math.Exp(-seconds / 0.02) : 1 - Math.Exp(-seconds / 0.12));
        var level = (float)Math.Clamp(Math.Sqrt(sumSquares / FftSize) * 3.2, 0, 1);
        Level += (level - Level) * (float)(level > Level ? rise : fall);

        FillWave(samples);
    }

    private double Magnitude(int bin) =>
        bin <= 0 || bin >= FftSize / 2 ? 0 : Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]);

    private void FillWave(ReadOnlySpan<float> samples)
    {
        const int stride = 3;
        var needed = WavePoints * stride;
        if (samples.Length < needed + 512)
        {
            Array.Clear(Wave);
            return;
        }
        var start = samples.Length - needed - 512;
        var trigger = start;
        for (var i = start; i < start + 512; i++)
            if (samples[i] <= 0 && samples[i + 1] > 0)
            {
                trigger = i;
                break;
            }
        for (var k = 0; k < WavePoints; k++) Wave[k] = samples[trigger + k * stride];
    }

    private void Transform()
    {
        for (var size = 2; size <= FftSize; size <<= 1)
        {
            var half = size / 2;
            var step = FftSize / size;
            for (var start = 0; start < FftSize; start += size)
            {
                for (var j = 0; j < half; j++)
                {
                    var c = _cos[j * step];
                    var s = _sin[j * step];
                    var a = start + j;
                    var b = a + half;
                    var tr = _re[b] * c - _im[b] * s;
                    var ti = _re[b] * s + _im[b] * c;
                    _re[b] = _re[a] - tr;
                    _im[b] = _im[a] - ti;
                    _re[a] += tr;
                    _im[a] += ti;
                }
            }
        }
    }
}
