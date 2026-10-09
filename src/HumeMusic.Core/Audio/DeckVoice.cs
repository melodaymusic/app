using HumeMusic.Core.Playback;

namespace HumeMusic.Core.Audio;

/// <summary>
/// One song as the mixer plays it: its samples (from <c>pull</c>) through the tempo stretch, the bass
/// filter and its volume. Every change is smoothed: volume glides across each block, the filter's
/// cutoff glides too, and the filter always runs, warmed up, alongside the untouched sound, which is
/// what's heard unless a bass swap asks for the filter (the two blend over a block). So switching it
/// on never jolts the sound, and a song without a swap plays exactly as it is. Interleaved floats.
/// </summary>
public sealed class DeckVoice
{
    /// <summary>The filter's resting cutoff: below anything heard, so it's transparent.</summary>
    public const double FilterFloor = 10;

    private readonly int _rate;
    private readonly int _channels;
    private readonly Func<float[], int, int> _pull;
    private readonly TimeStretcher _stretcher;
    private readonly HighPass _filter;
    private readonly LowPass _low;
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

    /// <param name="pull">Fills the array with up to the asked number of source frames; returns how many.</param>
    public DeckVoice(int rate, int channels, Func<float[], int, int> pull)
    {
        _rate = rate;
        _channels = channels;
        _pull = pull;
        _stretcher = new TimeStretcher(rate, channels);
        _filter = new HighPass(rate, channels) { Cutoff = FilterFloor };
        _low = new LowPass(rate, channels);
    }

    /// <summary>The volume wanted (0-1); reached smoothly over the next block.</summary>
    public float Gain { get; set; } = 1;
    /// <summary>The song's own level (Sound Check).</summary>
    public float Level { get; set; } = 1;
    /// <summary>How fast the song plays (1 = as written), pitch kept.</summary>
    public double Rate { get; set; } = 1;
    /// <summary>The bass filter's cutoff wanted (Hz; at or below the floor, off).</summary>
    public double Cutoff { get; set; }

    /// <summary>The low-pass cutoff wanted (Hz; 0, off): the song dull at first and opening up as it comes into a mix.</summary>
    public double LowCutoff { get; set; }
    /// <summary>How much echo is mixed in (0-1): the song repeating itself, fading, as it goes out of a mix.</summary>
    public float EchoWet { get; set; }
    /// <summary>The echo's delay in seconds (a dotted eighth at the song's tempo suits it).</summary>
    public double EchoDelay { get; set; } = 0.375;
    /// <summary>The echo's feedback: how much of each repeat comes back round.</summary>
    public const float EchoFeedback = 0.5f;

    /// <summary>How much of the song (seconds) has been played.</summary>
    public double Played { get; private set; }

    /// <summary>Starts counting from <paramref name="seconds"/> (where the song was opened or sought to).</summary>
    public void Reset(double seconds)
    {
        Played = seconds;
        _stretcher.Clear();
        _stretching = false;
        _filter.Reset();
        _low.Reset();
        _lowNow = -1;
        _lowWetNow = 0;
        _echo = [];
        _echoWetNow = 0;
    }

    /// <summary>Renders up to <paramref name="frames"/> frames; returns how many the source could give.</summary>
    public int Read(float[] output, int frames)
    {
        int got;
        if (!_stretching && Math.Abs(Rate - 1) < 1e-6)
        {
            got = _pull(output, frames);
            Played += got / (double)_rate;
        }
        else
        {
            // Once stretched, the song stays in the stretcher: back at its own tempo it passes
            // through exactly, so changing tempo never clicks.
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

        // The cutoff glides (on a log scale, as heard) a quarter of the way per block.
        var target = Math.Max(FilterFloor, Cutoff);
        _cutoffNow = Math.Exp(Math.Log(_cutoffNow) + (Math.Log(target) - Math.Log(_cutoffNow)) * 0.25);
        if (Math.Abs(_cutoffNow - target) / target < 0.01) _cutoffNow = target;
        _filter.Cutoff = _cutoffNow;
        // The filter runs on a copy, always; the copy is heard only as far as a swap asks.
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

        // The low-pass: on from the first block it is asked for (and, as the cutoff is moved, gliding on a log scale as heard);
        // blended in and out over a block, and kept following the sound while off so it never jolts.
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

        // The echo: the song repeating itself, each repeat half as loud, mixed in as far as asked; gone entirely when not asked.
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

        // Volume glides across the block.
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

/// <summary>What a mix plan asks of each song at a moment into the mix.</summary>
public readonly record struct MixMoment(double OutGain, double InGain, double OutCutoff, double InCutoff, double InRate, bool OutgoingDone, bool Finished,
    double InLowPass = 0, float OutEcho = 0);

public static class MixTimeline
{
    /// <summary>The volumes, bass filters and tempo a plan asks for <paramref name="elapsed"/> into the mix.</summary>
    public static MixMoment At(BeatMixPlan plan, TimeSpan elapsed)
    {
        var (outGain, inGain) = plan.Shape.Gains(elapsed);
        double outCut = 0, inCut = 0;
        if (plan.BassSwap is { } swap)
        {
            outCut = Sweep(elapsed, swap, plan.SwapLength, 0, BeatMix.BassCut);
            inCut = Sweep(elapsed, swap, plan.SwapLength, BeatMix.BassCut, 0);
        }
        // Matched to the outgoing tempo while they overlap, then easing back to its own.
        var after = elapsed - plan.Shape.Overlap;
        var rate = after <= TimeSpan.Zero ? plan.TempoRatio
            : plan.RampBack <= TimeSpan.Zero ? 1
            : plan.TempoRatio + (1 - plan.TempoRatio) * Math.Min(1, after / plan.RampBack);
        var done = elapsed >= plan.Shape.Overlap;
        var finished = done && elapsed >= plan.Shape.Overlap + plan.RampBack && elapsed >= plan.Shape.InFade;

        // The style of the mix: a filter sweep (the outgoing song loses its lows then its mids as it goes, while the incoming one,
        // dull at first, opens up), or an echo out (the outgoing song repeating itself, fading, as it leaves).
        double inLow = 0;
        float echo = 0;
        switch (plan.Style)
        {
            case MixStyle.Filter when plan.BassSwap is { } swapAt && plan.Shape.Overlap > swapAt:
                outCut = Sweep(elapsed, swapAt, plan.Shape.Overlap - swapAt, 0, BeatMix.FilterTop);
                inLow = elapsed >= plan.Shape.InFade ? 0 : Sweep(elapsed, TimeSpan.Zero, plan.Shape.InFade, BeatMix.FilterOpening, BeatMix.FilterOpen);
                break;
            case MixStyle.Echo when plan.Shape.Overlap > plan.Shape.OutFadeStart:
                var through = Math.Clamp((elapsed - plan.Shape.OutFadeStart) / (plan.Shape.Overlap - plan.Shape.OutFadeStart), 0, 1);
                echo = (float)(BeatMix.EchoAmount * Math.Sin(through * Math.PI / 2));
                break;
        }
        return new MixMoment(outGain, inGain, outCut, inCut, rate, done, finished, inLow, echo);
    }

    /// <summary>A cutoff moving from <paramref name="from"/> to <paramref name="to"/> over the swap (on a log scale, as heard).</summary>
    private static double Sweep(TimeSpan elapsed, TimeSpan at, TimeSpan length, double from, double to)
    {
        if (elapsed < at) return from;
        if (elapsed >= at + length) return to;
        var t = (elapsed - at) / length;
        double Log(double hz) => Math.Log(Math.Max(hz, DeckVoice.FilterFloor));
        return Math.Exp(Log(from) + (Log(to) - Log(from)) * t);
    }
}
