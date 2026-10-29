namespace MelodayMusic.Core.Audio;

public readonly record struct BandGains(float Low, float MidCentre, float MidSides, float High)
{
    public static BandGains Full { get; } = new(1, 1, 1, 1);
    public static BandGains VoiceOnly { get; } = new(0, 1, 0.12f, 0.2f);
    public static BandGains MusicOnly { get; } = new(1, 0.1f, 1, 1);
    public static BandGains HighsOnly { get; } = new(0, 0, 0, 1);

    public bool IsFull => Low == 1 && MidCentre == 1 && MidSides == 1 && High == 1;

    public static BandGains Lerp(BandGains a, BandGains b, double t)
    {
        if (t <= 0) return a;
        if (t >= 1) return b;
        var k = (float)t;
        return new(a.Low + (b.Low - a.Low) * k, a.MidCentre + (b.MidCentre - a.MidCentre) * k,
            a.MidSides + (b.MidSides - a.MidSides) * k, a.High + (b.High - a.High) * k);
    }
}

public sealed class BandMixer
{
    public const double LowSplit = 200, HighSplit = 3500;

    private readonly int _channels;
    private readonly Stage[] _lowCut1, _lowCut2, _stopLow1, _stopLow2, _stopHigh1, _stopHigh2, _highCut1, _highCut2;
    private readonly Stage _voiceLow1, _voiceLow2, _voiceHigh1, _voiceHigh2;
    private BandGains _now = BandGains.Full;

    public BandMixer(int rate, int channels)
    {
        _channels = channels;
        Stage[] Make(Func<Stage> make) => Enumerable.Range(0, channels).Select(_ => make()).ToArray();
        _lowCut1 = Make(() => Stage.HighPass(rate, LowSplit));
        _lowCut2 = Make(() => Stage.HighPass(rate, LowSplit));
        _stopLow1 = Make(() => Stage.LowPass(rate, LowSplit));
        _stopLow2 = Make(() => Stage.LowPass(rate, LowSplit));
        _stopHigh1 = Make(() => Stage.HighPass(rate, HighSplit));
        _stopHigh2 = Make(() => Stage.HighPass(rate, HighSplit));
        _highCut1 = Make(() => Stage.LowPass(rate, HighSplit));
        _highCut2 = Make(() => Stage.LowPass(rate, HighSplit));
        (_voiceLow1, _voiceLow2) = (Stage.HighPass(rate, LowSplit), Stage.HighPass(rate, LowSplit));
        (_voiceHigh1, _voiceHigh2) = (Stage.LowPass(rate, HighSplit), Stage.LowPass(rate, HighSplit));
    }

    public void Reset()
    {
        foreach (var s in _lowCut1.Concat(_lowCut2).Concat(_stopLow1).Concat(_stopLow2).Concat(_stopHigh1).Concat(_stopHigh2)
                     .Concat(_highCut1).Concat(_highCut2).Append(_voiceLow1).Append(_voiceLow2).Append(_voiceHigh1).Append(_voiceHigh2))
            s.Reset();
        _now = BandGains.Full;
    }

    public void Process(float[] buffer, int frames, BandGains to)
    {
        var from = _now;
        var gliding = from != to;
        var quiet = !gliding && to.IsFull;
        Span<float> y = stackalloc float[_channels];
        for (var i = 0; i < frames; i++)
        {
            var g = gliding ? BandGains.Lerp(from, to, (i + 1) / (double)frames) : to;
            var mids = Math.Max(g.MidCentre, g.MidSides);
            for (var c = 0; c < _channels; c++)
            {
                var x = buffer[i * _channels + c];
                var noLows = _lowCut2[c].Run(_lowCut1[c].Run(x));
                var a = g.Low * x + (1 - g.Low) * noLows;
                var noMids = _stopLow2[c].Run(_stopLow1[c].Run(a)) + _stopHigh2[c].Run(_stopHigh1[c].Run(a));
                var b = mids * a + (1 - mids) * noMids;
                var noHighs = _highCut2[c].Run(_highCut1[c].Run(b));
                y[c] = g.High * b + (1 - g.High) * noHighs;
            }
            if (quiet) continue;
            if (_channels == 2 && mids > 0)
            {
                var centre = (y[0] + y[1]) * 0.5f;
                var sides = (y[0] - y[1]) * 0.5f * (g.MidSides / mids);
                var voiceBand = _voiceHigh2.Run(_voiceHigh1.Run(_voiceLow2.Run(_voiceLow1.Run(centre))));
                centre -= (1 - g.MidCentre / mids) * voiceBand;
                buffer[i * 2] = centre + sides;
                buffer[i * 2 + 1] = centre - sides;
            }
            else
            {
                if (_channels == 2) _voiceHigh2.Run(_voiceHigh1.Run(_voiceLow2.Run(_voiceLow1.Run((y[0] + y[1]) * 0.5f))));
                for (var c = 0; c < _channels; c++) buffer[i * _channels + c] = y[c];
            }
        }
        if (quiet && _channels == 2)
        {
            for (var i = 0; i < frames; i++)
                _voiceHigh2.Run(_voiceHigh1.Run(_voiceLow2.Run(_voiceLow1.Run((buffer[i * 2] + buffer[i * 2 + 1]) * 0.5f))));
        }
        _now = to;
    }

    private sealed class Stage
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        private Stage(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            (_b0, _b1, _b2, _a1, _a2) = (b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        }

        public static Stage LowPass(int rate, double cutoff)
        {
            var w = 2 * Math.PI * cutoff / rate;
            var alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
            var cos = Math.Cos(w);
            return new((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public static Stage HighPass(int rate, double cutoff)
        {
            var w = 2 * Math.PI * cutoff / rate;
            var alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
            var cos = Math.Cos(w);
            return new((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public float Run(float x)
        {
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            if (Math.Abs(_y1) < 1e-20) _y1 = 0;
            return (float)y;
        }

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;
    }
}
