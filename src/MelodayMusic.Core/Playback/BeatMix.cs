using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Playback;

public enum MixStyle
{
    Blend,
    Filter,
    Echo,
    EqBlend,
    VocalOut,
    VocalIn,
    Cut,
    Wash,
    Build,
}

public sealed record BeatMixPlan
{
    public required TimeSpan StartAt { get; init; }
    public required TimeSpan IncomingStart { get; init; }
    public required CrossfadeShape Shape { get; init; }
    public double TempoRatio { get; init; } = 1;
    public double OutRate { get; init; } = 1;
    public TimeSpan LeadIn { get; init; }
    public TimeSpan RampBack { get; init; }
    public TimeSpan? BassSwap { get; init; }
    public TimeSpan SwapLength { get; init; }
    public int Bars { get; init; }
    public bool BeatMatched { get; init; }
    public MixStyle Style { get; init; }
    public TimeSpan EchoDelay { get; init; } = TimeSpan.FromSeconds(0.375);
    // The next song stays paused and silent this long into the mix, then drops in.
    public TimeSpan InDelay { get; init; }
    // How long the outgoing song's echo or reverb rings on after the overlap.
    public TimeSpan Tail { get; init; }
    // One beat of the outgoing song as heard, for loop rolls.
    public TimeSpan Beat { get; init; }
    // For a build, when the next song takes over in full.
    public TimeSpan Drop { get; init; }
    // When the next song takes over: the downbeat the outgoing song's last full bar ends on, where the bass swaps or the
    // echo is thrown. Zero leaves it to the drop, the delay or the bass swap.
    public TimeSpan Landing { get; init; }
    // When the next song's intro is over and it plays in full; before this it plays this many dB under its usual level.
    public TimeSpan Arrives { get; init; }
    public double IntroLevel { get; init; }
    // How long the outgoing song's filter takes to rise, for a filter blend.
    public TimeSpan Sweep { get; init; }
    // The outgoing song's last beat before the landing goes into the echo, so leaving its ending early sounds meant.
    public bool Throw { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public static class BeatMix
{
    public const double MaxStretch = 0.10;
    public const int MaxBars = 16;
    public const int SuitedBars = 8;
    public const int ShortestBars = 4;

    public const double BassCut = 300;
    public const double FilterTop = 2600, FilterOpening = 700, FilterOpen = 16000;
    public const double EchoAmount = 0.6, ThrowAmount = 0.45;

    public const double CutLength = 0.06;
    public const double CutDrop = 3;

    public const double RiseReverb = 0.45, CutReverb = 0.6, WashReverb = 0.45, BuildReverb = 0.7;
    public const double WashTop = 650;
    public const double BuildTop = 800, RiserFrom = 350, RiserTo = 9000;
    // Under the build's last bar the next song is filtered up from this.
    public const double BuildInFrom = 500;
    public const int BuildBars = 2, LongBuildBars = 4;
    public const double BuildLift = 2, BuildLiftAnyKey = 4, WashDip = 4, LongBuildLift = 6;
    // A bigger jump than this is a quiet ending meeting a loud master, not a song coming in harder.
    public const double BuildLiftMost = 12, BuildFading = 4;
    // Rolls and drops belong to dance tempos; slower songs stutter and stop rather than build.
    public const double BuildSlowest = 100, BuildFastest = 160;
    public static readonly TimeSpan RiseTail = TimeSpan.FromSeconds(3), CutTail = TimeSpan.FromSeconds(3),
        WashTail = TimeSpan.FromSeconds(5), BuildTail = TimeSpan.FromSeconds(3.5);

    // A bar this many dB under a song's usual level is an outro, a fade or a tag rather than the song itself.
    public const double LiveDrop = 7;
    // The next song has arrived once it plays within FullDrop dB of its usual level, stepping up at least IntroStep dB from its intro.
    public const double FullDrop = 5, IntroStep = 4;
    private const double SilentBar = -40;
    // The most of the outgoing song's ending a mix leaves out, in bars.
    public const int LongestSkip = 24;
    // Bars of the next song played in full under the outgoing one before the swap, past its intro, by how well they suit. Where
    // the keys clash, only its top end plays under the outgoing one, and for no longer than ClashBars in all.
    public const int FitFullBars = 16, CloseFullBars = 8, SuitedFullBars = 4, ClashFullBars = 4;
    public const int ClashBars = 8;

    public static (double At, double Drop)? CutPoint(AudioAnalysis outgoing, double soundEnd)
    {
        var energy = outgoing.BeatEnergy;
        var beats = outgoing.Beats;
        if (energy.Count < 16 || !outgoing.HasBeat) return null;
        var bar = 4 * outgoing.BeatPeriod;
        (double At, double Drop)? best = null;
        for (var i = Math.Max(8, outgoing.DownbeatPhase); i < Math.Min(beats.Count, energy.Count); i++)
        {
            if ((i - outgoing.DownbeatPhase) % 4 != 0) continue;
            var at = beats[i];
            if (at < soundEnd - 16 * bar || at > soundEnd - outgoing.BeatPeriod) continue;
            var afterCount = Math.Min(8, Math.Min(energy.Count - i, beats.Count(b => b >= at && b < soundEnd)));
            if (afterCount < 4) continue;
            var before = energy.Skip(i - 8).Take(8).Average();
            var after = energy.Skip(i).Take(afterCount).Average();
            var drop = before - after;
            if (drop >= CutDrop && (best is null || drop > best.Value.Drop)) best = (at, drop);
        }
        return best;
    }

    public static bool StartsStrong(AudioAnalysis incoming, double inStart, AudioAnalysis outgoing, double cutAt) =>
        Lift(incoming, inStart, outgoing, cutAt) >= -4;

    // How much louder, in dB, the next song's first two bars are than the outgoing song's last two before the change.
    public static double? Lift(AudioAnalysis incoming, double inStart, AudioAnalysis outgoing, double at)
    {
        var first = incoming.Beats.Select((b, i) => (b, i)).FirstOrDefault(x => x.b >= inStart - 0.02, (-1, -1)).Item2;
        var atIndex = outgoing.Beats.Select((b, i) => (b, i)).FirstOrDefault(x => x.b >= at - 0.02, (-1, -1)).Item2;
        if (first < 0 || atIndex < 8 || incoming.BeatEnergy.Count < first + 8 || outgoing.BeatEnergy.Count < atIndex) return null;
        var opening = incoming.BeatEnergy.Skip(first).Take(8).Average();
        var going = outgoing.BeatEnergy.Skip(atIndex - 8).Take(8).Average();
        return opening - going;
    }

    // The outgoing song's energy over the two bars before a moment, and its usual energy across the analysed ending.
    private static double? Going(AudioAnalysis outgoing, double at)
    {
        var index = outgoing.Beats.Select((b, i) => (b, i)).FirstOrDefault(x => x.b >= at - 0.02, (-1, -1)).Item2;
        return index < 8 || outgoing.BeatEnergy.Count < index ? null : outgoing.BeatEnergy.Skip(index - 8).Take(8).Average();
    }

    private static double Usual(AudioAnalysis outgoing) =>
        outgoing.BeatEnergy.Count == 0 ? 0 : outgoing.BeatEnergy.Order().ElementAt(outgoing.BeatEnergy.Count / 2);

    // The analysed stretch bar by bar: where each bar starts, and how loud it is in dB against the loudest beat.
    private static List<(double At, double Energy)> BarsOf(AudioAnalysis a)
    {
        var bars = new List<(double, double)>();
        var energy = a.BeatEnergy;
        for (var i = a.DownbeatPhase; i + 3 < energy.Count && i < a.Beats.Count; i += 4)
        {
            var power = 0.0;
            for (var k = 0; k < 4; k++) power += Math.Pow(10, energy[i + k] / 10);
            bars.Add((a.Beats[i], 10 * Math.Log10(power / 4 + 1e-12)));
        }
        return bars;
    }

    // A song's usual level across the analysed stretch: loud enough to be the song, not its quietest bars.
    private static double Typical(List<(double At, double Energy)> bars)
    {
        var heard = bars.Select(b => b.Energy).Where(e => e > SilentBar).OrderDescending().ToList();
        return heard.Count == 0 ? 0 : heard[heard.Count / 4];
    }

    private static double PowerMean(IEnumerable<(double At, double Energy)> bars)
    {
        var list = bars.ToList();
        return list.Count == 0 ? 0 : 10 * Math.Log10(list.Average(b => Math.Pow(10, b.Energy / 10)) + 1e-12);
    }

    // Where the outgoing song stops being itself: the downbeat its last bar near its usual level ends on. A fade, a quiet tag
    // or a long tail after it is left to the mix instead of being played out to silence.
    public static double LiveEnd(AudioAnalysis outgoing, double soundEnd)
    {
        var bars = BarsOf(outgoing);
        if (bars.Count < 4) return soundEnd;
        var usual = Typical(bars);
        var bar = 4 * outgoing.BeatPeriod;
        for (var k = bars.Count - 1; k >= 0; k--)
        {
            var end = k + 1 < bars.Count ? bars[k + 1].At : bars[k].At + bar;
            if (end > soundEnd + 0.5 * outgoing.BeatPeriod) continue;
            if (end < soundEnd - LongestSkip * bar) break;
            if (bars[k].Energy >= usual - LiveDrop) return end;
        }
        return soundEnd;
    }

    // Where the next song arrives in full: the first downbeat it steps up from its opening on, into four bars near its usual level,
    // or failing a step, the first of two bars in a row near it. The bars before are its intro, sung or not, and Level is how far
    // under its usual level they play.
    public static (double At, int IntroBars, double Level) Arrival(AudioAnalysis incoming, double inStart)
    {
        var all = BarsOf(incoming);
        var bars = all.Where(b => b.At >= inStart - 0.02).ToList();
        if (bars.Count < 4) return (inStart, 0, 0);
        var usual = Typical(all);
        var k = 1;
        for (; k + 4 <= bars.Count; k++)
        {
            var ahead = PowerMean(bars.GetRange(k, 4));
            if (bars[k].Energy >= usual - FullDrop && ahead >= usual - FullDrop && ahead - PowerMean(bars.Take(k)) >= IntroStep) break;
        }
        if (k + 4 > bars.Count)
        {
            k = 0;
            while (k + 1 < bars.Count && !(bars[k].Energy >= usual - FullDrop && bars[k + 1].Energy >= usual - FullDrop)) k++;
            if (k + 1 >= bars.Count) k = 0;
        }
        var level = k == 0 ? 0 : Math.Clamp(PowerMean(bars.Take(k)) - usual, -24, 0);
        return (bars[k].At, k, level);
    }

    // Whether the next song plays near its usual level from a downbeat for four bars, so it never takes over just before a breakdown.
    // Without the bars to tell, it is taken to.
    public static bool LandsStrong(AudioAnalysis incoming, double at)
    {
        var bars = BarsOf(incoming);
        var from = bars.FindIndex(b => b.At >= at - 0.05);
        if (bars.Count < 4 || from < 0 || from + 4 > bars.Count) return true;
        var four = bars.GetRange(from, 4);
        var usual = Typical(bars);
        return PowerMean(four) >= usual - FullDrop && four.Min(b => b.Energy) >= usual - LiveDrop;
    }

    // A song's beat period across a stretch, fitted through the beats in it; null with too few to go on.
    public static double? LocalPeriod(AudioAnalysis a, double from, double to)
    {
        var beats = a.Beats.Where(b => b >= from && b <= to).ToList();
        if (beats.Count < 16) return null;
        double meanX = (beats.Count - 1) / 2.0, meanY = beats.Average(), num = 0, den = 0;
        for (var i = 0; i < beats.Count; i++)
        {
            num += (i - meanX) * (beats[i] - meanY);
            den += (i - meanX) * (i - meanX);
        }
        return num / den;
    }

    // Where a song's sound ends, from its own analysis, for when no loudness analysis is to hand.
    public static double SoundEnd(AudioAnalysis outgoing)
    {
        var energy = outgoing.BeatEnergy;
        for (var i = Math.Min(energy.Count, outgoing.Beats.Count - 1) - 1; i >= 0; i--)
            if (energy[i] > SilentBar) return outgoing.Beats[i + 1];
        return outgoing.To;
    }

    // Where a song's sound starts, from its own analysis.
    public static double SoundStart(AudioAnalysis incoming)
    {
        var energy = incoming.BeatEnergy;
        for (var i = 0; i < Math.Min(energy.Count, incoming.Beats.Count); i++)
            if (energy[i] > SilentBar) return Math.Max(incoming.From, incoming.Beats[i] - 0.05);
        return incoming.From;
    }

    public static string Label(BeatMixPlan plan) => plan.Style switch
    {
        MixStyle.Blend when plan.BeatMatched => "Beat blend",
        MixStyle.Blend => "Fade",
        MixStyle.Filter => "Filter rise",
        MixStyle.Echo => "Echo out",
        MixStyle.EqBlend => "EQ blend",
        MixStyle.VocalOut or MixStyle.VocalIn => "Vocal blend",
        MixStyle.Cut => "Cut",
        MixStyle.Wash => "Reverb wash",
        MixStyle.Build => "Loop roll",
        _ => "Mix",
    };

    public static TimeSpan EchoDelayFor(double beatPeriod) => TimeSpan.FromSeconds(Math.Clamp(beatPeriod * 0.75, 0.2, 0.8));

    // Long enough for the repeats to fall about 60 dB.
    public static TimeSpan EchoTail(TimeSpan delay) => TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds * 10, 2, 6));

    // When the next song comes in harder than the outgoing one goes out, loop the outgoing song's last bars down to an eighth
    // of a beat under a rising filter and riser, then drop the next song in full on the landing as a reverb tail rings. A big
    // jump gets a longer build.
    private static BeatMixPlan? BuildPlan(AudioAnalysis outgoing, AudioAnalysis incoming, double arrival, double land, double liveEnd, double soundEnd,
        double ratio, double harmony, double earliest)
    {
        var beat = outgoing.BeatPeriod;
        var bar = 4 * beat;
        if (soundEnd - land > 2 * bar && land < liveEnd - 0.5 * beat) return null;
        if (Lift(incoming, arrival, outgoing, land) is not { } lift) return null;
        if (!(lift >= BuildLiftAnyKey || (lift >= BuildLift && harmony < 0.55)) || lift > BuildLiftMost) return null;
        // A song already fading out has nothing left to build from; that wants a long blend, not a drop.
        if (Going(outgoing, land) is not { } going || going < Usual(outgoing) - BuildFading) return null;
        var bars = lift >= LongBuildLift && land - LongBuildBars * bar >= Math.Max(earliest - 1e-3, soundEnd / 2) ? LongBuildBars : BuildBars;
        var length = bars * bar;
        if (land - length < Math.Max(earliest - 1e-3, soundEnd / 2)) return null;

        var meet = Math.Sqrt(ratio);
        var outRate = 1 / meet;
        var stretched = Math.Abs(ratio - 1) > 0.002;
        var heard = stretched ? outRate : 1;
        if (outgoing.Bpm * heard is < BuildSlowest or > BuildFastest) return null;
        var start = land - length;
        var span = TimeSpan.FromSeconds(length / heard);
        var heardBeat = TimeSpan.FromSeconds(beat / heard);
        // The next song comes in under the last bar, on its own downbeat, bass cut and filtered, so the drop is a swap rather than a gap.
        var entry = span - 4 * heardBeat;
        var inBar = 4 * incoming.BeatPeriod;
        var inFrom = incoming.Downbeats.Where(d => d <= arrival - inBar + 0.02).Select(d => (double?)d).LastOrDefault() ?? arrival;
        return new BeatMixPlan
        {
            StartAt = TimeSpan.FromSeconds(start),
            IncomingStart = TimeSpan.FromSeconds(inFrom),
            Shape = new CrossfadeShape(span + heardBeat, 4 * heardBeat, span, heardBeat),
            InDelay = entry,
            Drop = span,
            Landing = span,
            Arrives = span,
            Tail = BuildTail,
            Beat = heardBeat,
            TempoRatio = stretched ? meet : 1,
            OutRate = stretched ? outRate : 1,
            LeadIn = stretched ? TimeSpan.FromSeconds(Math.Min(8 * bar, start)) : TimeSpan.Zero,
            RampBack = stretched ? TimeSpan.FromSeconds(8 * 4 * incoming.BeatPeriod) : TimeSpan.Zero,
            Bars = bars,
            BeatMatched = true,
            Style = MixStyle.Build,
            Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm, {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)} (harmony {harmony:0.0}): " +
                      $"the next song comes in {lift:0.0} dB harder, so a {bars}-bar build (a loop roll to an eighth of a beat under a rising filter and riser, " +
                      $"the next song filtered in under the last bar) dropping into it at {land:0.0} s",
        };
    }

    // Without a steady beat to work with, the loudness plan's crossfade, the outgoing song melting into reverb as it goes when
    // there is time for it.
    public static BeatMixPlan Fallback(Transition loudness) => loudness.Fade >= TimeSpan.FromSeconds(3)
        ? new BeatMixPlan
        {
            StartAt = loudness.StartAt,
            IncomingStart = loudness.IncomingStart,
            Shape = loudness.Shape,
            Style = MixStyle.Wash,
            Tail = WashTail,
            Summary = $"no steady beat: the loudness plan's {loudness.Fade.TotalSeconds:0.0} s crossfade, washed out into reverb",
        }
        : new BeatMixPlan { StartAt = loudness.StartAt, IncomingStart = loudness.IncomingStart, Shape = loudness.Shape, Summary = "no steady beat: loudness plan" };

    public static double? TempoRatio(double outgoingBpm, double incomingBpm)
    {
        if (outgoingBpm <= 0 || incomingBpm <= 0) return null;
        var best = new[] { 1.0, 2.0, 0.5 }
            .Select(k => outgoingBpm * k / incomingBpm)
            .OrderBy(r => Math.Abs(Math.Log(r)))
            .First();
        return EachStretch(best) <= MaxStretch ? best : null;
    }

    public static double EachStretch(double ratio) => Math.Exp(Math.Abs(Math.Log(ratio)) / 2) - 1;

    public static double? EnergyFit(AudioAnalysis outgoing, AudioAnalysis incoming)
    {
        if (outgoing.BeatEnergy.Count < 8 || incoming.BeatEnergy.Count < 8) return null;
        var outEnd = outgoing.BeatEnergy.TakeLast(16).Average();
        var inStart = incoming.BeatEnergy.Take(16).Average();
        return Math.Exp(-Math.Abs(outEnd - inStart) / 5.0);
    }

    public static bool Fit(double harmony, double? ratio, double? energyFit) =>
        harmony >= 0.9 && ratio is { } r && EachStretch(r) <= 0.03 && energyFit is >= 0.6;

    // notBefore is where playback already is, as after a seek late in the song: the mix then fits in what's left. Without a
    // loudness plan the songs' own analyses say where the sound ends and starts.
    // outLiveEnd is where the outgoing song stops being itself judged against its whole length, from its loudness analysis; the
    // song's own analysis only hears the ending, which a long quiet outro can fill.
    public static BeatMixPlan? Plan(Transition? loudness, AudioAnalysis outgoing, AudioAnalysis incoming, VocalSpan? outVocals = null, VocalSpan? inVocals = null,
        TimeSpan? notBefore = null, TimeSpan? outLiveEnd = null)
    {
        if (!outgoing.HasBeat || !incoming.HasBeat) return null;
        var soundEnd = loudness is { } l ? (l.StartAt + l.Fade).TotalSeconds : SoundEnd(outgoing);
        var earliest = Math.Max(outgoing.From, notBefore?.TotalSeconds ?? 0);
        var ratio = TempoRatio(outgoing.Bpm, incoming.Bpm);
        var harmony = AudioAnalysis.KeyHarmony(outgoing.Key, incoming.Key);

        var lead = loudness?.IncomingStart.TotalSeconds ?? SoundStart(incoming);
        var inStart = incoming.DownbeatAtOrAfter(lead) is { } d && d - lead < 2 * 4 * incoming.BeatPeriod ? d : lead;

        var beat = outgoing.BeatPeriod;
        var bar = 4 * beat;
        var downbeats = outgoing.Downbeats.ToList();
        // The landing: the downbeat the outgoing song's last full bar ends on, or after a late seek the first one left.
        // An ending quiet against either the whole song or what has just played is left out, so the mix never sits in a lull.
        var liveEnd = LiveEnd(outgoing, soundEnd);
        if (outLiveEnd is { } whole && whole.TotalSeconds >= outgoing.From + 4 * bar && whole.TotalSeconds >= soundEnd - LongestSkip * bar)
            liveEnd = Math.Min(liveEnd, whole.TotalSeconds);
        var j = downbeats.FindLastIndex(t => t <= liveEnd + 0.5 * beat && t >= earliest + bar - 1e-3);
        if (j < 0) j = downbeats.FindIndex(t => t >= earliest + bar - 1e-3 && t <= soundEnd - beat);
        if (j < 1) return null;
        var arrival = Arrival(incoming, inStart);

        return ratio is { } r
            ? Matched(outgoing, incoming, r, harmony, liveEnd, soundEnd, earliest, inStart, downbeats, j, arrival, outVocals, inVocals)
            : Unmatched(outgoing, incoming, harmony, soundEnd, earliest, inStart, downbeats, j, arrival);
    }

    private static BeatMixPlan Unmatched(AudioAnalysis outgoing, AudioAnalysis incoming, double harmony, double soundEnd, double earliest, double inStart,
        List<double> downbeats, int j, (double At, int IntroBars, double Level) arrival)
    {
        var beat = outgoing.BeatPeriod;
        var bar = 4 * beat;
        var keys = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm (too far to match), {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)}";
        if (CutPoint(outgoing, soundEnd) is { } cut && cut.At - beat >= earliest && StartsStrong(incoming, inStart, outgoing, cut.At))
        {
            // The mix starts a beat early so that beat can be thrown into the reverb before the cut.
            var lastBeat = TimeSpan.FromSeconds(Math.Min(beat, cut.At));
            return new BeatMixPlan
            {
                StartAt = TimeSpan.FromSeconds(cut.At) - lastBeat,
                IncomingStart = TimeSpan.FromSeconds(inStart),
                Shape = new CrossfadeShape(lastBeat + TimeSpan.FromSeconds(CutLength), TimeSpan.Zero, lastBeat, TimeSpan.FromSeconds(CutLength)),
                InDelay = lastBeat,
                Landing = lastBeat,
                Arrives = lastBeat,
                Tail = CutTail,
                Beat = lastBeat,
                Bars = 1,
                Style = MixStyle.Cut,
                Summary = $"{keys}: a clean cut on the downbeat at {cut.At:0.0} s, where the outgoing song drops {cut.Drop:0.0} dB, with a reverb splash",
            };
        }

        var land = downbeats[j];
        var inBars = incoming.Downbeats.Where(t => t >= inStart - 0.02).ToList();
        var lout = Math.Clamp(soundEnd - land, 0.5 * beat, bar);
        var echoDelay = EchoDelayFor(beat);
        // A soft opening is left to rise under a long reverb tail; a steady one comes in on the landing as the echo rings, its intro
        // mostly skipped so the gap the echo leaves is short.
        if (Lift(incoming, inStart, outgoing, land) is <= -WashDip)
        {
            var introUsed = Math.Min(arrival.IntroBars, 4);
            var entry = inBars.Count > arrival.IntroBars - introUsed ? inBars[arrival.IntroBars - introUsed] : inStart;
            var from = downbeats[Math.Max(0, j - 2)] >= earliest - 1e-3 ? downbeats[Math.Max(0, j - 2)] : downbeats[j - 1];
            var landing = TimeSpan.FromSeconds(land - from);
            var overlap = landing + TimeSpan.FromSeconds(lout);
            return new BeatMixPlan
            {
                StartAt = TimeSpan.FromSeconds(from),
                IncomingStart = TimeSpan.FromSeconds(entry),
                Shape = new CrossfadeShape(overlap, landing, TimeSpan.FromSeconds(bar), overlap - TimeSpan.FromSeconds(bar)),
                Landing = landing,
                Arrives = landing + TimeSpan.FromSeconds(introUsed * 4 * incoming.BeatPeriod),
                IntroLevel = introUsed > 0 ? arrival.Level : 0,
                Bars = (int)Math.Round((land - from) / bar),
                Style = MixStyle.Wash,
                EchoDelay = echoDelay,
                Tail = WashTail,
                Summary = $"{keys}: washed out into reverb over the next song's soft opening, landing at {land:0.0} s" +
                          (arrival.IntroBars > introUsed ? $", {arrival.IntroBars - introUsed} bars of its intro skipped" : string.Empty),
            };
        }

        var lead = Math.Min(arrival.IntroBars, 2);
        var start = downbeats[j - 1];
        var heardBar = TimeSpan.FromSeconds(land - start);
        var close = TimeSpan.FromSeconds(Math.Min(0.5 * beat, lout));
        return new BeatMixPlan
        {
            StartAt = TimeSpan.FromSeconds(start),
            IncomingStart = TimeSpan.FromSeconds(inBars.Count > arrival.IntroBars - lead ? inBars[arrival.IntroBars - lead] : inStart),
            Shape = new CrossfadeShape(heardBar + close, lead > 0 ? TimeSpan.FromSeconds(4 * incoming.BeatPeriod) : TimeSpan.Zero, heardBar, close),
            InDelay = heardBar,
            Landing = heardBar,
            Arrives = heardBar + TimeSpan.FromSeconds(lead * 4 * incoming.BeatPeriod),
            IntroLevel = lead > 0 ? arrival.Level : 0,
            Bars = 1,
            Style = MixStyle.Echo,
            Throw = true,
            Beat = TimeSpan.FromSeconds(beat),
            EchoDelay = echoDelay,
            Tail = EchoTail(echoDelay),
            Summary = $"{keys}: the outgoing song's last beat thrown into a ping-pong echo at {land:0.0} s, the next song dropping in on it" +
                      (arrival.IntroBars > lead ? $", {arrival.IntroBars - lead} bars of its intro skipped" : string.Empty),
        };
    }

    private static BeatMixPlan? Matched(AudioAnalysis outgoing, AudioAnalysis incoming, double ratio, double harmony, double liveEnd, double soundEnd, double earliest,
        double inStart, List<double> downbeats, int j, (double At, int IntroBars, double Level) arrival, VocalSpan? outVocals, VocalSpan? inVocals)
    {
        var beat = outgoing.BeatPeriod;
        var bar = 4 * beat;
        var inBar = 4 * incoming.BeatPeriod;
        var land = downbeats[j];
        var fit = Fit(harmony, ratio, EnergyFit(outgoing, incoming));
        if (!fit && BuildPlan(outgoing, incoming, arrival.At, land, liveEnd, soundEnd, ratio, harmony, earliest) is { } build) return build;

        // How long the next song plays under the outgoing one before the landing: its intro, and past it a few bars of the song
        // itself where the two suit each other, so a long intro over a long ending makes a long blend and a song that starts at
        // full tilt makes a short one.
        var full = fit ? FitFullBars : harmony >= 0.9 ? CloseFullBars : harmony >= 0.55 ? SuitedFullBars : ClashFullBars;
        // Not knowing where the voices are, two full songs share only a few bars, however well they suit.
        if (outVocals is null || inVocals is null) full = Math.Min(full, fit ? CloseFullBars : SuitedFullBars);
        var want = arrival.IntroBars + full;
        var cap = harmony >= 0.55 ? MaxBars : ClashBars;
        var inBars = incoming.Downbeats.Where(t => t >= inStart - 0.02).ToList();
        var lin = 0;
        // Phrase lengths, or the whole intro so the next song starts from its very first bar.
        foreach (var n in new[] { 16, 8, 4, 2, 1, arrival.IntroBars, want }.Distinct().OrderDescending())
        {
            if (n < 1 || n > Math.Min(want, cap) || j - n < 0) continue;
            var from = downbeats[j - n];
            if (from < earliest - 1e-3 || from < soundEnd / 2 || Math.Abs(land - from - n * bar) > 0.5 * bar) continue;
            var skip = arrival.IntroBars - Math.Min(n, arrival.IntroBars);
            if (skip >= inBars.Count || inBars[skip] + n * inBar > incoming.To - 1) continue;
            if (skip + n < inBars.Count && !LandsStrong(incoming, inBars[skip + n])) continue;
            // Two voices never sing over each other: the next voice may only be heard once the outgoing one has finished. Under an
            // EQ blend the next song's mids, and its voice with them, only come up over the second half of the lead-in; under a
            // filter blend not until the landing.
            if (outVocals is { } ov && inVocals is { } iv)
            {
                var sings = from + Math.Max(0, iv.First.TotalSeconds - inBars[skip]) / ratio;
                var audible = Math.Max(sings, harmony >= 0.55 ? (from + land) / 2 : land);
                if (audible < ov.LastEnd.TotalSeconds - 0.5 * beat) continue;
            }
            lin = n;
            break;
        }

        // Over a long blend a song's own tempo where the two meet counts more than its average, or the beats drift apart.
        if (lin >= 1)
        {
            var entryAt = inBars[arrival.IntroBars - Math.Min(lin, arrival.IntroBars)];
            var octave = ratio * incoming.Bpm / outgoing.Bpm;
            if (LocalPeriod(outgoing, downbeats[j - lin] - bar, land + bar) is { } outPeriod
                && LocalPeriod(incoming, entryAt, entryAt + (lin + 1) * inBar) is { } inPeriod
                && octave * inPeriod / outPeriod is var local && Math.Abs(local / ratio - 1) < 0.01)
                ratio = local;
        }

        var meet = Math.Sqrt(ratio);
        var outRate = 1 / meet;
        var stretched = Math.Abs(ratio - 1) > 0.002;
        var heard = stretched ? outRate : 1;
        TimeSpan H(double seconds) => TimeSpan.FromSeconds(seconds / heard);
        var start = downbeats[j - Math.Max(1, lin)];
        var introUsed = Math.Min(lin, arrival.IntroBars);
        var fullUsed = lin - introUsed;
        var entry = lin == 0 ? arrival.At : inBars[arrival.IntroBars - introUsed];
        var landing = H(land - start);
        var heardBeat = H(beat);
        var heardBar = H(bar);
        var arrives = lin == 0 ? landing : landing - H(fullUsed * bar);
        var lout = Math.Clamp(soundEnd - land, 0.5 * beat, 2 * bar);
        // Leaving the outgoing song while it still has sound to play, its last beat goes into the echo so the exit sounds meant.
        var leavesEarly = soundEnd - land > lout + beat;
        var echoDelay = EchoDelayFor(heardBeat.TotalSeconds);
        var keys = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm" + (stretched ? $" (meeting at {outgoing.Bpm * outRate:0.#}: ×{outRate:0.000} out, ×{meet:0.000} in)" : string.Empty) +
                   $", {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)} (harmony {harmony:0.0})";
        var skipped = arrival.IntroBars - introUsed;
        var plan = new BeatMixPlan
        {
            StartAt = TimeSpan.FromSeconds(start),
            IncomingStart = TimeSpan.FromSeconds(entry),
            Shape = CrossfadeShape.Even(landing),
            TempoRatio = stretched ? meet : 1,
            OutRate = stretched ? outRate : 1,
            LeadIn = stretched ? TimeSpan.FromSeconds(Math.Min(8 * bar, start)) : TimeSpan.Zero,
            RampBack = stretched ? TimeSpan.FromSeconds(8 * 4 * incoming.BeatPeriod) : TimeSpan.Zero,
            Bars = lin,
            BeatMatched = true,
            Landing = landing,
            Arrives = arrives,
            IntroLevel = introUsed > 0 ? arrival.Level : 0,
            EchoDelay = echoDelay,
            Beat = heardBeat,
        };

        if (lin < (harmony >= 0.55 ? 2 : ShortestBars))
        {
            // No room to blend: the next song's short intro, if it has one, rises under the outgoing song's last bar or two, and the
            // outgoing song's last beat is thrown into the echo as the next one lands on the downbeat.
            var close = H(Math.Min(0.5 * beat, lout));
            return plan with
            {
                Shape = new CrossfadeShape(landing + close, lin == 0 ? TimeSpan.Zero : landing, landing, close),
                InDelay = lin == 0 ? landing : TimeSpan.Zero,
                Style = MixStyle.Echo,
                Throw = true,
                Tail = EchoTail(echoDelay),
                Summary = $"{keys}, no room to blend: the last beat thrown into the echo at {land:0.0} s" +
                          (lin > 0 ? $" over {lin} bar{(lin == 1 ? string.Empty : "s")} of the next song's intro" : ", the next song dropping in on it") +
                          (skipped > 0 ? $", {skipped} bars of its intro skipped" : string.Empty),
            };
        }

        var overlap = landing + H(lout);
        var tailOut = overlap - landing;
        // The next song's lead-in is its own intro and so carries no voice; the outgoing voice can then ride over it, or where the
        // outgoing song has finished singing, the next voice can start over its music.
        var inSings = inVocals is { } v ? (double?)(start + (v.First.TotalSeconds - entry) / ratio) : null;
        var bodied = fullUsed > 0 || arrival.Level >= -8;
        var style = harmony >= 0.55 ? MixStyle.EqBlend : MixStyle.Filter;
        if (outVocals is not null && outVocals.LastEnd.TotalSeconds > start + bar && bodied && (inSings is null || inSings >= land - 0.5 * beat))
            style = MixStyle.VocalOut;
        else if (outVocals is not null && outVocals.LastEnd.TotalSeconds <= start && inSings is { } sings && sings >= start + bar && sings < land - bar)
            style = MixStyle.VocalIn;
        // Where the next song's bass already plays under the outgoing one, it comes in over a bar; out of an intro, on the downbeat.
        var swapLength = fullUsed > 0 ? H(Math.Min(bar, (land - start) / 4)) : heardBeat / 2;
        var sweep = H(Math.Clamp(lin / 2.0, 2, 8) * bar);
        var described = style switch
        {
            MixStyle.VocalOut => "the outgoing voice over the next song's music",
            MixStyle.VocalIn => "the next voice over the outgoing song's music",
            MixStyle.EqBlend => "EQ blend (highs, then mids, the bass swapping on the landing)",
            _ => $"filter blend (only the next song's top end under the outgoing one, a {sweep.TotalSeconds:0} s filter rise into the landing)",
        };
        return plan with
        {
            Shape = style switch
            {
                MixStyle.Filter => new CrossfadeShape(overlap, heardBar, landing, tailOut),
                MixStyle.VocalOut or MixStyle.VocalIn => new CrossfadeShape(overlap, heardBar < landing / 4 ? heardBar : landing / 4, landing, tailOut),
                _ => new CrossfadeShape(overlap, heardBeat, landing, tailOut),
            },
            BassSwap = landing - swapLength,
            SwapLength = swapLength,
            Style = style,
            Sweep = style == MixStyle.Filter ? sweep : TimeSpan.Zero,
            Throw = leavesEarly && style != MixStyle.Filter,
            Tail = style == MixStyle.Filter ? RiseTail : leavesEarly ? EchoTail(echoDelay) : TimeSpan.Zero,
            Summary = $"{keys}, {lin} bars{(fit ? ", a perfect fit" : string.Empty)}: {described}, landing at {land:0.0} s" +
                      (introUsed > 0 ? $" as the next song's {arrival.IntroBars}-bar intro ends" + (skipped > 0 ? $" ({skipped} bars of it skipped)" : string.Empty) : string.Empty) +
                      (leavesEarly ? $", {soundEnd - land - lout:0.0} s of the outgoing ending left out" + (style != MixStyle.Filter ? " behind an echo" : string.Empty) : string.Empty),
        };
    }
}
