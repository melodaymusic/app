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
    private float _echoSendNow;
    private bool _echoRinging;
    private readonly float[] _toneLow, _toneHigh;
    private readonly float _toneLowCoef, _toneHighCoef;
    private Reverb? _reverb;
    private bool _reverbRinging;
    private float[] _roll = [];
    private int _rollFill;
    private long _rollAt;
    private bool _rolling;
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
        _toneLow = new float[channels];
        _toneHigh = new float[channels];
        _toneLowCoef = (float)(1 - Math.Exp(-2 * Math.PI * EchoTop / rate));
        _toneHighCoef = (float)(1 - Math.Exp(-2 * Math.PI * EchoBottom / rate));
    }

    public BandGains Bands { get; set; } = BandGains.Full;

    public float Gain { get; set; } = 1;
    public float Level { get; set; } = 1;
    public double Rate { get; set; } = 1;
    public double Cutoff { get; set; }

    public double LowCutoff { get; set; }

    // The sends sit after the fader, so what they hold rings on once the fader closes, as on a DJ mixer.
    public float EchoSend { get; set; }
    public double EchoDelay { get; set; } = 0.375;
    public const float EchoFeedback = 0.5f;
    // Each repeat loses a little top and bottom, as a tape echo does, and bounces to the other side.
    public const double EchoTop = 5500, EchoBottom = 180;
    public const float EchoSpill = 0.3f;
    public float ReverbSend { get; set; }

    // Loops the last stretch of this length in seconds while the song runs on underneath; 0 plays straight through.
    public double Roll { get; set; }
    private const int RollEdge = 64;

    public double Played { get; private set; }

    private void Remember(float[] samples, int frames)
    {
        var n = Math.Min(frames, HistoryFrames);
        var keep = Math.Min(_historyFill, HistoryFrames - n);
        Array.Copy(_history, (_historyFill - keep) * _channels, _history, 0, keep * _channels);
        Array.Copy(samples, (frames - n) * _channels, _history, keep * _channels, n * _channels);
        _historyFill = keep + n;
    }

    // The next read rises from silence across its few milliseconds instead of starting at full, so a drop doesn't click.
    public void RiseFromSilence() => _gainNow = 0;

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
        _echoSendNow = 0;
        _echoRinging = false;
        Array.Clear(_toneLow);
        Array.Clear(_toneHigh);
        _reverb?.Reset();
        _reverbRinging = false;
        _rolling = false;
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
        if (Roll > 0 || _rolling) Rolling(output, frames);
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

        var to = Gain * Level;
        var from = _gainNow < 0 ? to : _gainNow;
        for (var i = 0; i < frames; i++)
        {
            var g = from + (to - from) * (i + 1) / frames;
            for (var c = 0; c < _channels; c++) output[i * _channels + c] *= g;
        }
        _gainNow = to;

        if (EchoSend > 0 || _echoSendNow > 0 || _echoRinging) Echo(output, frames);
        if (ReverbSend > 0 || _reverbRinging)
        {
            _reverb ??= new Reverb(_rate, _channels);
            _reverbRinging = _reverb.Process(output, frames, ReverbSend);
        }
        return got;
    }

    private void Echo(float[] output, int frames)
    {
        var delay = Math.Clamp((int)(EchoDelay * _rate), _rate / 10, _rate);
        if (_echo.Length != delay * _channels)
        {
            _echo = new float[delay * _channels];
            _echoPos = 0;
        }
        var from = _echoSendNow;
        var to = EchoSend;
        var peak = 0f;
        for (var i = 0; i < frames; i++)
        {
            var s = from + (to - from) * (i + 1) / frames;
            var k = i * _channels;
            var at = _echoPos * _channels;
            if (_channels == 2)
            {
                var heardL = _echo[at];
                var heardR = _echo[at + 1];
                _echo[at] = (output[k] + output[k + 1]) * 0.5f * s + EchoFeedback * Tone(heardR, 0);
                _echo[at + 1] = EchoFeedback * Tone(heardL, 1);
                output[k] += heardL + EchoSpill * heardR;
                output[k + 1] += heardR + EchoSpill * heardL;
                peak = Math.Max(peak, Math.Max(Math.Abs(heardL), Math.Abs(heardR)));
            }
            else
                for (var c = 0; c < _channels; c++)
                {
                    var heard = _echo[at + c];
                    _echo[at + c] = output[k + c] * s + EchoFeedback * Tone(heard, c);
                    output[k + c] += heard;
                    peak = Math.Max(peak, Math.Abs(heard));
                }
            _echoPos = (_echoPos + 1) % delay;
        }
        _echoSendNow = to;
        _echoRinging = to > 0 || from > 0 || peak > 1e-5f;
        if (_echoRinging) return;
        _echo = [];
        Array.Clear(_toneLow);
        Array.Clear(_toneHigh);
    }

    private float Tone(float x, int channel)
    {
        _toneLow[channel] += _toneLowCoef * (x - _toneLow[channel]);
        var dark = _toneLow[channel];
        _toneHigh[channel] += _toneHighCoef * (dark - _toneHigh[channel]);
        var y = dark - _toneHigh[channel];
        return Math.Abs(y) < 1e-20f ? 0 : y;
    }

    // The first pass plays as it comes and is kept; after that the kept stretch repeats, cut shorter as Roll shrinks,
    // each repeat eased in and out over a millisecond or so so the seams don't click.
    private void Rolling(float[] output, int frames)
    {
        if (Roll <= 0)
        {
            _rolling = false;
            return;
        }
        if (!_rolling)
        {
            _rolling = true;
            _rollAt = 0;
            _rollFill = 0;
            var room = (int)Math.Ceiling(Roll * _rate) + 1;
            if (_roll.Length < room * _channels) _roll = new float[room * _channels];
        }
        var capacity = _roll.Length / _channels;
        var length = Math.Clamp((int)Math.Round(Roll * _rate), 2 * RollEdge, capacity);
        var edge = Math.Max(1, Math.Min(RollEdge, length / 8));
        for (var i = 0; i < frames; i++, _rollAt++)
        {
            var at = (int)(_rollAt % length);
            var k = i * _channels;
            if (_rollAt < length && _rollAt == _rollFill)
            {
                Array.Copy(output, k, _roll, _rollFill * _channels, _channels);
                _rollFill++;
            }
            else if (at < _rollFill)
                Array.Copy(_roll, at * _channels, output, k, _channels);
            var g = 1f;
            if (_rollAt >= length && at < edge) g = at / (float)edge;
            if (at >= length - edge) g = Math.Min(g, (length - at) / (float)edge);
            if (g < 1)
                for (var c = 0; c < _channels; c++) output[k + c] *= g;
        }
    }
}

public readonly record struct MixMoment(double OutGain, double InGain, double OutCutoff, double InCutoff, double InRate, bool OutgoingDone, bool Finished,
    double InLowPass = 0, float OutEcho = 0, double OutRate = 1, BandGains? OutBandsAsked = null, BandGains? InBandsAsked = null,
    float OutReverb = 0, double OutRoll = 0, bool InWaiting = false, double Riser = 0, double RiserHz = 0, double Trim = 1)
{
    public BandGains OutBands => OutBandsAsked ?? BandGains.Full;
    public BandGains InBands => InBandsAsked ?? BandGains.Full;
}

public static class MixTimeline
{
    public static MixMoment At(BeatMixPlan plan, TimeSpan elapsed)
    {
        var (outGain, inGain) = plan.Shape.Gains(elapsed);
        var waiting = elapsed < plan.InDelay;
        if (plan.InDelay > TimeSpan.Zero) inGain = waiting ? 0 : plan.Shape.Gains(elapsed - plan.InDelay).Incoming;
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
        var done = elapsed >= plan.Shape.Overlap + plan.Tail;
        var finished = done && elapsed >= plan.Shape.Overlap + plan.RampBack && elapsed >= plan.InDelay + plan.Shape.InFade;

        double inLow = 0, roll = 0, riser = 0, riserHz = 0;
        float echo = 0, reverb = 0;
        BandGains? outBands = null, inBands = null;
        var o = plan.Shape.Overlap;
        double Through(double from, double to) => to <= from ? (elapsed.TotalSeconds >= from ? 1 : 0)
            : Math.Clamp((elapsed.TotalSeconds - from) / (to - from), 0, 1);
        var swapS = (plan.BassSwap ?? o / 2).TotalSeconds;
        var swapEnd = swapS + Math.Max(0.05, plan.SwapLength.TotalSeconds);
        var len = o.TotalSeconds;
        // Planned around a landing, every lane keys off the downbeat the next song takes over on, however long the lead-up.
        var land = plan.Landing.TotalSeconds;
        var landed = land > 0;
        var beatS = plan.Beat > TimeSpan.Zero ? plan.Beat.TotalSeconds : 0.5;
        var past = Math.Max(0.05, len - land);
        switch (plan.Style)
        {
            case MixStyle.EqBlend when landed:
                outCut = inCut = 0;
                inBands = new BandGains((float)Through(swapS, land), (float)Through(land / 2, land), (float)Through(land / 2, land),
                    (float)Through(0, Math.Min(land / 2, 8 * beatS)));
                outBands = new BandGains(1 - (float)Through(swapS, land), 1 - (float)Through(land, land + past / 2),
                    1 - (float)Through(land, land + past / 2), 1 - (float)Through(land + past / 4, len));
                break;
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
            case MixStyle.Filter when landed:
                // Only the next song's top end plays under the outgoing one, so clashing keys never meet. The outgoing song's filter
                // rises to take its bass out by the landing, where the next one opens up in full, then on up as it rings off.
                inCut = 0;
                inBands = BandGains.Lerp(BandGains.HighsOnly, BandGains.Full, Through(land - beatS / 2, land));
                var sweep = Math.Max(0.05, plan.Sweep.TotalSeconds);
                outCut = elapsed.TotalSeconds < land
                    ? Sweep(elapsed, TimeSpan.FromSeconds(land - sweep), TimeSpan.FromSeconds(sweep), 0, BeatMix.BassCut)
                    : Sweep(elapsed, plan.Landing, TimeSpan.FromSeconds(past), BeatMix.BassCut, BeatMix.FilterTop);
                reverb = (float)(BeatMix.RiseReverb * Through(land - beatS, len));
                break;
            case MixStyle.Filter when plan.BassSwap is { } swapAt && plan.Shape.Overlap > swapAt:
                outCut = Sweep(elapsed, swapAt, plan.Shape.Overlap - swapAt, 0, BeatMix.FilterTop);
                inLow = elapsed >= plan.Shape.InFade ? 0 : Sweep(elapsed, TimeSpan.Zero, plan.Shape.InFade, BeatMix.FilterOpening, BeatMix.FilterOpen);
                // The thinned-out song dissolves into a room rather than just going.
                reverb = (float)(BeatMix.RiseReverb * Through(len * 0.7, len));
                break;
            case MixStyle.Echo when landed:
                break;
            case MixStyle.Echo when plan.Shape.Overlap > plan.Shape.OutFadeStart:
                var through = Math.Clamp((elapsed - plan.Shape.OutFadeStart) / (plan.Shape.Overlap - plan.Shape.OutFadeStart), 0, 1);
                echo = (float)(BeatMix.EchoAmount * Math.Sin(through * Math.PI / 2));
                break;
            case MixStyle.Wash:
                reverb = (float)(BeatMix.WashReverb * Math.Sin(Through(0, len * 0.6) * Math.PI / 2));
                outCut = Sweep(elapsed, o * 0.4, o * 0.6, 0, BeatMix.WashTop);
                break;
            case MixStyle.Cut when plan.InDelay > TimeSpan.Zero:
                // A splash of reverb on the last beat, so the cut leaves a tail rather than a hole.
                reverb = (float)(BeatMix.CutReverb * Through(0, plan.InDelay.TotalSeconds));
                break;
            case MixStyle.Build when plan.Drop > TimeSpan.Zero && plan.Beat > TimeSpan.Zero:
                var drop = plan.Drop.TotalSeconds;
                var b = plan.Beat.TotalSeconds;
                var e = elapsed.TotalSeconds;
                // Under the last bar the next song rises without its bass, its filter opening; at the drop the bass swaps over.
                inLow = e < drop ? Sweep(elapsed, plan.InDelay, plan.Drop - plan.InDelay, BeatMix.BuildInFrom, BeatMix.FilterOpen) : 0;
                inCut = e < drop ? BeatMix.BassCut : Sweep(elapsed, plan.Drop, plan.Beat / 2, BeatMix.BassCut, 0);
                // Past the drop the filter and loop hold while the fader closes, so no full-range burst slips out under the next song.
                outCut = e < drop ? Sweep(elapsed, TimeSpan.Zero, plan.Drop, 0, BeatMix.BuildTop) : BeatMix.BuildTop;
                roll = RollAt(Math.Max(0, drop - e), b);
                reverb = (float)(BeatMix.BuildReverb * Through(drop - b, drop));
                if (e < drop)
                {
                    var rise = e / drop;
                    riser = rise * rise;
                    riserHz = BeatMix.RiserFrom * Math.Pow(BeatMix.RiserTo / BeatMix.RiserFrom, rise);
                }
                break;
        }
        // The throw: the echo opens over the outgoing song's last beat before the landing and holds as its fader closes, so the
        // repeats carry that beat on under the next song.
        if (plan.Throw && landed)
            echo = (float)((plan.Style == MixStyle.Echo ? BeatMix.EchoAmount : BeatMix.ThrowAmount) * Through(land - beatS, land - beatS / 2));
        // Two full songs sum about 3 dB louder and peak near twice full scale, and the limiter would then pull the whole mix
        // down the moment the next song came in. Trimming both decks to keep the sum near one song's level stops that.
        // A next song still in its intro plays well under its usual level, and counting it as full would duck the outgoing song for nothing.
        var intro = elapsed < plan.Arrives ? Math.Pow(10, Math.Max(-20, plan.IntroLevel) / 10) : 1;
        var heard = (done ? 0 : outGain * outGain * Energy(outBands ?? BandGains.Full, outCut, 0))
                    + inGain * inGain * intro * Energy(inBands ?? BandGains.Full, inCut, inLow);
        var trim = heard > 1 ? 1 / Math.Sqrt(heard) : 1;
        return new MixMoment(outGain * trim, inGain * trim, outCut, inCut, rate, done, finished, inLow, echo, plan.OutRate, outBands, inBands,
            reverb, roll, waiting, riser, riserHz, trim);
    }

    // Rough shares of a typical song's energy, by the bands BandMixer splits it into.
    private const double LowShare = 0.45, CentreShare = 0.30, SidesShare = 0.12, HighShare = 0.13;
    private static readonly (double Hz, double Above)[] EnergyAbove =
        [(40, 1), (100, 0.8), (200, 0.55), (500, 0.4), (1000, 0.28), (2600, 0.12), (6000, 0.04), (16000, 0.005)];

    // How much of a song's energy still comes through these bands and filters, from 0 to 1.
    public static double Energy(BandGains bands, double highPass, double lowPass)
    {
        var e = bands.Low * bands.Low * LowShare + bands.MidCentre * bands.MidCentre * CentreShare
                + bands.MidSides * bands.MidSides * SidesShare + bands.High * bands.High * HighShare;
        if (highPass > DeckVoice.FilterFloor) e *= Above(highPass);
        if (lowPass > 0) e *= Math.Max(0.01, 1 - Above(lowPass));
        return e;
    }

    private static double Above(double hz)
    {
        if (hz <= EnergyAbove[0].Hz) return 1;
        for (var i = 1; i < EnergyAbove.Length; i++)
        {
            if (hz > EnergyAbove[i].Hz) continue;
            var (f0, e0) = EnergyAbove[i - 1];
            var (f1, e1) = EnergyAbove[i];
            var t = Math.Log(hz / f0) / Math.Log(f1 / f0);
            return e0 + (e1 - e0) * t;
        }
        return EnergyAbove[^1].Above;
    }

    // The last bar before the drop loops a beat, then a half, a quarter and an eighth.
    private static double RollAt(double untilDrop, double beat) => untilDrop switch
    {
        _ when untilDrop > 4 * beat => 0,
        _ when untilDrop > 2 * beat => beat,
        _ when untilDrop > beat => beat / 2,
        _ when untilDrop > beat / 2 => beat / 4,
        _ => beat / 8,
    };

    // When the next song takes over and becomes the one playing: the drop, the cut, the end of the bass swap, or halfway
    // through a plain blend. Until then the listener still hears the outgoing song as the main one.
    public static TimeSpan Handover(BeatMixPlan plan)
    {
        if (plan.Landing > TimeSpan.Zero) return plan.Landing;
        if (plan.Drop > TimeSpan.Zero) return plan.Drop;
        if (plan.InDelay > TimeSpan.Zero) return plan.InDelay;
        if (plan.BassSwap is { } swap) return swap + plan.SwapLength;
        return plan.Shape.Overlap / 2;
    }

    // Moments that have to land on an exact sample, not just on the next slice: the drop and each change of loop length.
    public static IReadOnlyList<double> Edges(BeatMixPlan plan)
    {
        if (plan.Style != MixStyle.Build || plan.Beat <= TimeSpan.Zero || plan.Drop <= TimeSpan.Zero)
            return plan.InDelay > TimeSpan.Zero ? [plan.InDelay.TotalSeconds] : [];
        var drop = plan.Drop.TotalSeconds;
        var b = plan.Beat.TotalSeconds;
        return new[] { plan.InDelay.TotalSeconds, drop - 4 * b, drop - 2 * b, drop - b, drop - b / 2, drop }
            .Where(t => t > 0).Select(t => Math.Round(t, 9)).Distinct().Order().ToArray();
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
