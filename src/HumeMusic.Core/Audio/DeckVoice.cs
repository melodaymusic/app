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
    }

    /// <summary>The volume wanted (0-1); reached smoothly over the next block.</summary>
    public float Gain { get; set; } = 1;
    /// <summary>The song's own level (Sound Check).</summary>
    public float Level { get; set; } = 1;
    /// <summary>How fast the song plays (1 = as written), pitch kept.</summary>
    public double Rate { get; set; } = 1;
    /// <summary>The bass filter's cutoff wanted (Hz; at or below the floor, off).</summary>
    public double Cutoff { get; set; }

    /// <summary>How much of the song (seconds) has been played.</summary>
    public double Played { get; private set; }

    /// <summary>Starts counting from <paramref name="seconds"/> (where the song was opened or sought to).</summary>
    public void Reset(double seconds)
    {
        Played = seconds;
        _stretcher.Clear();
        _stretching = false;
        _filter.Reset();
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
public readonly record struct MixMoment(double OutGain, double InGain, double OutCutoff, double InCutoff, double InRate, bool OutgoingDone, bool Finished);

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
        return new MixMoment(outGain, inGain, outCut, inCut, rate, done, finished);
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
