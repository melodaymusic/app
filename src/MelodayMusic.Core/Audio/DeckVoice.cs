using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Audio;

public sealed class DeckVoice
{
    public const double FilterFloor = 10;

    private readonly int _rate;
    private readonly int _channels;
    private readonly Func<float[], int, int> _pull;
    private const int HistoryFrames = 4096;
    private readonly PhaseVocoder _stretcher;
    private readonly float[] _history;
    private int _historyFill;
    private readonly HighPass _filter;
    private readonly LowPass _low;
    private readonly BandMixer _bands;
    private double _lowNow = -1;
    private float[] _lowBuf = [];
    private float _lowWetNow;
    private float[] _echo = [];
    private int _echoPos;
    private float _echoWetNow;
    private float[] _feed = [];
    private bool _stretching;
    private float _gainNow = -1;
    private double _cutoffNow = FilterFloor;
    private float[] _wet = [];
    private float _wetNow;

    public DeckVoice(int rate, int channels, Func<float[], int, int> pull)
    {
        _rate = rate;
        _channels = channels;
        _pull = pull;
        _stretcher = new PhaseVocoder(channels);
        _history = new float[HistoryFrames * channels];
        _filter = new HighPass(rate, channels) { Cutoff = FilterFloor };
        _low = new LowPass(rate, channels);
        _bands = new BandMixer(rate, channels);
    }

    public BandGains Bands { get; set; } = BandGains.Full;

    public float Gain { get; set; } = 1;
    public float Level { get; set; } = 1;
    public double Rate { get; set; } = 1;
    public double Cutoff { get; set; }

    public double LowCutoff { get; set; }
    public float EchoWet { get; set; }
    public double EchoDelay { get; set; } = 0.375;
    public const float EchoFeedback = 0.5f;

    public double Played { get; private set; }

    private void Remember(float[] samples, int frames)
    {
        var n = Math.Min(frames, HistoryFrames);
        var keep = Math.Min(_historyFill, HistoryFrames - n);
        Array.Copy(_history, (_historyFill - keep) * _channels, _history, 0, keep * _channels);
        Array.Copy(samples, (frames - n) * _channels, _history, keep * _channels, n * _channels);
        _historyFill = keep + n;
    }

    public void Reset(double seconds)
    {
        Played = seconds;
        _stretcher.Clear();
        _stretching = false;
        _historyFill = 0;
        _filter.Reset();
        _low.Reset();
        _bands.Reset();
        _lowNow = -1;
        _lowWetNow = 0;
        _echo = [];
        _echoWetNow = 0;
    }

    public int Read(float[] output, int frames)
    {
        int got;
        if (!_stretching && Math.Abs(Rate - 1) < 1e-6)
        {
            got = _pull(output, frames);
            Played += got / (double)_rate;
            Remember(output, got);
        }
        else
        {
            // Prime with recent output so the stretched audio doesn't click on entry.
            if (!_stretching) _stretcher.Prime(_history, _historyFill);
            _stretching = true;
            _stretcher.Rate = Rate;
            got = 0;
            while (got < frames)
            {
                got += _stretcher.Take(output, got, frames - got);
                if (got >= frames) break;
                var need = Math.Max(256, _stretcher.Wanted);
                if (_feed.Length < need * _channels) _feed = new float[need * _channels];
                var pulled = _pull(_feed, need);
                if (pulled == 0) break;
                _stretcher.Put(_feed, 0, pulled);
            }
            Played += got / (double)_rate * Rate;
        }
        if (got < frames) Array.Clear(output, got * _channels, (frames - got) * _channels);
        _bands.Process(output, frames, Bands);

        var target = Math.Max(FilterFloor, Cutoff);
        _cutoffNow = Math.Exp(Math.Log(_cutoffNow) + (Math.Log(target) - Math.Log(_cutoffNow)) * 0.25);
        if (Math.Abs(_cutoffNow - target) / target < 0.01) _cutoffNow = target;
        _filter.Cutoff = _cutoffNow;
        if (_wet.Length < frames * _channels) _wet = new float[frames * _channels];
        Array.Copy(output, _wet, frames * _channels);
        _filter.Process(_wet, 0, frames);
        var wetTo = Cutoff > FilterFloor ? 1f : 0f;
        var wetFrom = _wetNow;
        if (wetFrom > 0 || wetTo > 0)
            for (var i = 0; i < frames; i++)
            {
                var w = wetFrom + (wetTo - wetFrom) * (i + 1) / frames;
                for (var c = 0; c < _channels; c++)
                {
                    var k = i * _channels + c;
                    output[k] = output[k] * (1 - w) + _wet[k] * w;
                }
            }
        _wetNow = wetTo;

        var lowOn = LowCutoff > 0;
        if (lowOn || _lowWetNow > 0)
        {
            var target2 = lowOn ? Math.Clamp(LowCutoff, 40, _rate * 0.45) : Math.Max(40, _lowNow);
            _lowNow = _lowNow < 0 ? target2 : Math.Exp(Math.Log(_lowNow) + (Math.Log(target2) - Math.Log(_lowNow)) * 0.25);
            _low.Cutoff = _lowNow;
            if (_lowBuf.Length < frames * _channels) _lowBuf = new float[frames * _channels];
            Array.Copy(output, _lowBuf, frames * _channels);
            _low.Process(_lowBuf, 0, frames);
            var lowTo = lowOn ? 1f : 0f;
            var lowFrom = _lowWetNow;
            for (var i = 0; i < frames; i++)
            {
                var w = lowFrom + (lowTo - lowFrom) * (i + 1) / frames;
                for (var c = 0; c < _channels; c++)
                {
                    var k = i * _channels + c;
                    output[k] = output[k] * (1 - w) + _lowBuf[k] * w;
                }
            }
            _lowWetNow = lowTo;
            if (!lowOn && lowTo == 0) _lowNow = -1;
        }
        else
        {
            _low.Cutoff = 0;
            _low.Process(output, 0, frames);
        }

        if (EchoWet > 0 || _echoWetNow > 0)
        {
            var delay = Math.Clamp((int)(EchoDelay * _rate), _rate / 10, _rate);
            if (_echo.Length != delay * _channels)
            {
                _echo = new float[delay * _channels];
                _echoPos = 0;
            }
            var echoFrom = _echoWetNow;
            var echoTo = EchoWet;
            for (var i = 0; i < frames; i++)
            {
                var w = echoFrom + (echoTo - echoFrom) * (i + 1) / frames;
                for (var c = 0; c < _channels; c++)
                {
                    var k = i * _channels + c;
                    var at = _echoPos * _channels + c;
                    var heard = _echo[at];
                    var x = output[k];
                    _echo[at] = x + EchoFeedback * heard;
                    output[k] = x + w * heard;
                }
                _echoPos = (_echoPos + 1) % delay;
            }
            _echoWetNow = echoTo;
            if (echoTo == 0 && echoFrom == 0) _echo = [];
        }

        var to = Gain * Level;
        var from = _gainNow < 0 ? to : _gainNow;
        for (var i = 0; i < frames; i++)
        {
            var g = from + (to - from) * (i + 1) / frames;
            for (var c = 0; c < _channels; c++) output[i * _channels + c] *= g;
        }
        _gainNow = to;
        return got;
    }
}

public readonly record struct MixMoment(double OutGain, double InGain, double OutCutoff, double InCutoff, double InRate, bool OutgoingDone, bool Finished,
    double InLowPass = 0, float OutEcho = 0, double OutRate = 1, BandGains? OutBandsAsked = null, BandGains? InBandsAsked = null)
{
    public BandGains OutBands => OutBandsAsked ?? BandGains.Full;
    public BandGains InBands => InBandsAsked ?? BandGains.Full;
}

public static class MixTimeline
{
    public static MixMoment At(BeatMixPlan plan, TimeSpan elapsed)
    {
        var (outGain, inGain) = plan.Shape.Gains(elapsed);
        double outCut = 0, inCut = 0;
        if (plan.BassSwap is { } swap)
        {
            outCut = Sweep(elapsed, swap, plan.SwapLength, 0, BeatMix.BassCut);
            inCut = Sweep(elapsed, swap, plan.SwapLength, BeatMix.BassCut, 0);
        }
        var after = elapsed - plan.Shape.Overlap;
        var rate = after <= TimeSpan.Zero ? plan.TempoRatio
            : plan.RampBack <= TimeSpan.Zero ? 1
            : plan.TempoRatio + (1 - plan.TempoRatio) * Math.Min(1, after / plan.RampBack);
        var done = elapsed >= plan.Shape.Overlap;
        var finished = done && elapsed >= plan.Shape.Overlap + plan.RampBack && elapsed >= plan.Shape.InFade;

        double inLow = 0;
        float echo = 0;
        BandGains? outBands = null, inBands = null;
        var o = plan.Shape.Overlap;
        double Through(double from, double to) => to <= from ? (elapsed.TotalSeconds >= from ? 1 : 0)
            : Math.Clamp((elapsed.TotalSeconds - from) / (to - from), 0, 1);
        var swapS = (plan.BassSwap ?? o / 2).TotalSeconds;
        var swapEnd = swapS + Math.Max(0.05, plan.SwapLength.TotalSeconds);
        var len = o.TotalSeconds;
        switch (plan.Style)
        {
            case MixStyle.EqBlend:
                outCut = inCut = 0;
                inBands = new BandGains((float)Through(swapS, swapEnd), (float)Through(len / 4, len / 2), (float)Through(len / 4, len / 2),
                    (float)Through(0, len / 4));
                outBands = new BandGains(1 - (float)Through(swapS, swapEnd), 1 - (float)Through(len / 2, len * 3 / 4),
                    1 - (float)Through(len / 2, len * 3 / 4), 1 - (float)Through(len * 5 / 8, len));
                break;
            case MixStyle.VocalOut:
                outCut = inCut = 0;
                outBands = BandGains.Lerp(BandGains.Full, BandGains.VoiceOnly, Through(0, Math.Min(len / 4, Math.Max(0.05, plan.SwapLength.TotalSeconds * 4))));
                break;
            case MixStyle.VocalIn:
                outCut = inCut = 0;
                inBands = BandGains.Lerp(BandGains.VoiceOnly, BandGains.Full, Through(swapS, swapEnd));
                outBands = BandGains.Lerp(BandGains.Full, BandGains.MusicOnly, Through(0, swapS));
                break;
            case MixStyle.Filter when plan.BassSwap is { } swapAt && plan.Shape.Overlap > swapAt:
                outCut = Sweep(elapsed, swapAt, plan.Shape.Overlap - swapAt, 0, BeatMix.FilterTop);
                inLow = elapsed >= plan.Shape.InFade ? 0 : Sweep(elapsed, TimeSpan.Zero, plan.Shape.InFade, BeatMix.FilterOpening, BeatMix.FilterOpen);
                break;
            case MixStyle.Echo when plan.Shape.Overlap > plan.Shape.OutFadeStart:
                var through = Math.Clamp((elapsed - plan.Shape.OutFadeStart) / (plan.Shape.Overlap - plan.Shape.OutFadeStart), 0, 1);
                echo = (float)(BeatMix.EchoAmount * Math.Sin(through * Math.PI / 2));
                break;
        }
        return new MixMoment(outGain, inGain, outCut, inCut, rate, done, finished, inLow, echo, plan.OutRate, outBands, inBands);
    }

    public static double LeadInRate(BeatMixPlan plan, double from, double easeFrom, double position)
    {
        var start = plan.StartAt.TotalSeconds;
        if (start - easeFrom <= 1e-3 || position >= start) return plan.OutRate;
        var t = Math.Clamp((position - easeFrom) / (start - easeFrom), 0, 1);
        return from + (plan.OutRate - from) * t * t * (3 - 2 * t);
    }

    private static double Sweep(TimeSpan elapsed, TimeSpan at, TimeSpan length, double from, double to)
    {
        if (elapsed < at) return from;
        if (elapsed >= at + length) return to;
        var t = (elapsed - at) / length;
        double Log(double hz) => Math.Log(Math.Max(hz, DeckVoice.FilterFloor));
        return Math.Exp(Log(from) + (Log(to) - Log(from)) * t);
    }
}
