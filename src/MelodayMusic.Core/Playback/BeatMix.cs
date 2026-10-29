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
    public string Summary { get; init; } = string.Empty;
}

public static class BeatMix
{
    public const double MaxStretch = 0.10;
    public const int MaxBars = 16;
    public const int SuitedBars = 8;

    public const double BassCut = 300;
    public const double FilterTop = 2600, FilterOpening = 700, FilterOpen = 16000;
    public const double EchoAmount = 0.6;

    public const double CutLength = 0.06;
    public const double CutDrop = 3;

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

    public static bool StartsStrong(AudioAnalysis incoming, double inStart, AudioAnalysis outgoing, double cutAt)
    {
        var first = incoming.Beats.Select((b, i) => (b, i)).FirstOrDefault(x => x.b >= inStart - 0.02, (-1, -1)).Item2;
        var cutIndex = outgoing.Beats.Select((b, i) => (b, i)).FirstOrDefault(x => x.b >= cutAt - 0.02, (-1, -1)).Item2;
        if (first < 0 || cutIndex < 8 || incoming.BeatEnergy.Count < first + 8 || outgoing.BeatEnergy.Count < cutIndex) return false;
        var opening = incoming.BeatEnergy.Skip(first).Take(8).Average();
        var going = outgoing.BeatEnergy.Skip(cutIndex - 8).Take(8).Average();
        return opening >= going - 4;
    }

    public static TimeSpan EchoDelayFor(double beatPeriod) => TimeSpan.FromSeconds(Math.Clamp(beatPeriod * 0.75, 0.2, 0.8));

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

    public static bool Suits(AudioAnalysis outgoing, AudioAnalysis incoming, double harmony)
    {
        if (harmony < 0.55 || outgoing.BeatEnergy.Count < 8 || incoming.BeatEnergy.Count < 8) return false;
        var outEnd = outgoing.BeatEnergy.TakeLast(16).Average();
        var inStart = incoming.BeatEnergy.Take(16).Average();
        return Math.Abs(outEnd - inStart) <= 6;
    }

    public static BeatMixPlan? Plan(Transition loudness, AudioAnalysis outgoing, AudioAnalysis incoming, VocalSpan? outVocals = null, VocalSpan? inVocals = null)
    {
        if (!outgoing.HasBeat || !incoming.HasBeat) return null;
        var soundEnd = (loudness.StartAt + loudness.Fade).TotalSeconds;
        var bar = 4 * outgoing.BeatPeriod;
        var ratio = TempoRatio(outgoing.Bpm, incoming.Bpm);
        var matched = ratio is not null;
        var harmony = AudioAnalysis.KeyHarmony(outgoing.Key, incoming.Key);

        var lead = loudness.IncomingStart.TotalSeconds;
        var inStart = incoming.DownbeatAtOrAfter(lead) is { } d && d - lead < 2 * 4 * incoming.BeatPeriod ? d : lead;

        var loudnessOverlap = loudness.Fade.TotalSeconds;

        if (!matched)
        {
            if (CutPoint(outgoing, soundEnd) is { } cut && StartsStrong(incoming, inStart, outgoing, cut.At))
                return new BeatMixPlan
                {
                    StartAt = TimeSpan.FromSeconds(cut.At),
                    IncomingStart = TimeSpan.FromSeconds(inStart),
                    Shape = new CrossfadeShape(TimeSpan.FromSeconds(CutLength), TimeSpan.FromSeconds(0.01), TimeSpan.Zero, TimeSpan.FromSeconds(CutLength)),
                    Bars = 1,
                    Style = MixStyle.Cut,
                    Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm (too far to match), {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)}: " +
                              $"a clean cut on the downbeat at {cut.At:0.0} s, where the outgoing song drops {cut.Drop:0.0} dB",
                };
            var planned = loudness.StartAt.TotalSeconds;
            var snapped = outgoing.NearestDownbeat(planned) is { } near && Math.Abs(near - planned) <= outgoing.BeatPeriod * 2 ? near : planned;
            var length = Math.Max(0.5, soundEnd - snapped);
            var scale = length / Math.Max(0.5, loudnessOverlap);
            var shape0 = loudness.Shape;
            return new BeatMixPlan
            {
                StartAt = TimeSpan.FromSeconds(snapped),
                IncomingStart = TimeSpan.FromSeconds(inStart),
                Shape = new CrossfadeShape(TimeSpan.FromSeconds(length), shape0.InFade * Math.Min(1, scale), shape0.OutFadeStart * scale, TimeSpan.FromSeconds(length) - shape0.OutFadeStart * scale),
                Bars = Math.Max(1, (int)Math.Round(length / bar)),
                Style = MixStyle.Echo,
                EchoDelay = EchoDelayFor(outgoing.BeatPeriod),
                Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm (too far to match), {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)}: " +
                          $"the loudness blend ({length:0.0} s) on a bar line, with an echo out",
            };
        }

        var fit = Fit(harmony, ratio, EnergyFit(outgoing, incoming));
        var bars = fit ? MaxBars : harmony >= 0.55 ? SuitedBars : 4;
        var cap = Math.Max(loudnessOverlap * 2.5, 2 * bar);
        if (outVocals is not null && inVocals is not null)
        {
            var outFree = Math.Max(0, soundEnd - outVocals.LastEnd.TotalSeconds);
            var inFree = Math.Max(0, (inVocals.First.TotalSeconds - inStart) / (ratio ?? 1));
            var instrumental = outFree + inFree;
            cap = Suits(outgoing, incoming, harmony) ? Math.Max(bar, instrumental) : Math.Min(cap, Math.Max(bar, instrumental));
        }
        while (bars > 1 && bars * bar > cap + 1e-6) bars /= 2;
        while (bars > 1 && (inStart + bars * bar * (ratio ?? 1) > incoming.To - 1 || soundEnd - bars * bar < soundEnd / 2)) bars /= 2;
        var overlap = bars * bar;

        var start = outgoing.Downbeats.Where(t => t + overlap <= soundEnd + 0.5 * outgoing.BeatPeriod && t >= outgoing.From)
            .Select(t => (double?)t).LastOrDefault();
        if (start is null) return null;
        if (soundEnd - (start.Value + overlap) > 2 * bar) return null;

        var r = ratio ?? 1;
        var meet = Math.Sqrt(r);
        var outRate = 1 / meet;
        var stretched = Math.Abs(r - 1) > 0.002;
        var overlapSpan = TimeSpan.FromSeconds(overlap / outRate);
        var swapAt = bars >= 2 ? TimeSpan.FromSeconds(bars / 2 * bar / outRate) : (TimeSpan?)null;
        var beat = TimeSpan.FromSeconds(outgoing.BeatPeriod / outRate);
        var shape = bars >= MaxBars && swapAt is { } longSwap
            ? new CrossfadeShape(overlapSpan, overlapSpan * (6.0 / 16), overlapSpan * (10.0 / 16), overlapSpan * (6.0 / 16))
            : swapAt is { } swap
                ? new CrossfadeShape(overlapSpan, swap, swap, overlapSpan - swap)
                : new CrossfadeShape(overlapSpan, TimeSpan.FromSeconds(Math.Min(overlapSpan.TotalSeconds / 2, 1)), TimeSpan.Zero, overlapSpan);

        var style = MixStyle.Blend;
        var bandBar = TimeSpan.FromSeconds(bar / outRate);
        if (!fit && swapAt is { } halfway && bars >= 4 && harmony >= 0.55)
        {
            var overlapEnd = start.Value + overlap;
            var inVoice = inVocals is null ? (double?)null : start.Value + (inVocals.First.TotalSeconds - inStart) / r;
            if (outVocals is not null && outVocals.LastEnd.TotalSeconds > start.Value + bar && inVoice is { } laterIn && laterIn >= overlapEnd - 0.5)
            {
                style = MixStyle.VocalOut;
                shape = new CrossfadeShape(overlapSpan, bandBar < overlapSpan / 4 ? bandBar : overlapSpan / 4, overlapSpan * 0.75, overlapSpan * 0.25);
            }
            else if (outVocals is not null && outVocals.LastEnd.TotalSeconds <= start.Value && inVoice is { } early
                     && early >= start.Value + bar && early <= start.Value + overlap / 2)
            {
                style = MixStyle.VocalIn;
                shape = new CrossfadeShape(overlapSpan, bandBar < overlapSpan / 4 ? bandBar : overlapSpan / 4, halfway, overlapSpan - halfway);
            }
            else
            {
                style = MixStyle.EqBlend;
                shape = new CrossfadeShape(overlapSpan, beat, overlapSpan * 0.875, overlapSpan * 0.125);
            }
        }
        else if (!fit && swapAt is not null) style = MixStyle.Filter;
        var described = style switch
        {
            MixStyle.VocalOut => ", the outgoing voice over the next song's music",
            MixStyle.VocalIn => ", the next voice over the outgoing song's music",
            MixStyle.EqBlend => ", EQ blend (highs, mids, then the bass swap halfway)",
            MixStyle.Filter => ", filter sweep from halfway",
            _ when swapAt is not null => ", bass swap halfway",
            _ => string.Empty,
        };

        return new BeatMixPlan
        {
            StartAt = TimeSpan.FromSeconds(start.Value),
            IncomingStart = TimeSpan.FromSeconds(inStart),
            Shape = shape,
            TempoRatio = stretched ? meet : 1,
            OutRate = stretched ? outRate : 1,
            LeadIn = stretched ? TimeSpan.FromSeconds(Math.Min(8 * bar, start.Value)) : TimeSpan.Zero,
            RampBack = stretched ? TimeSpan.FromSeconds(8 * 4 * incoming.BeatPeriod) : TimeSpan.Zero,
            BassSwap = swapAt,
            SwapLength = beat,
            Bars = bars,
            BeatMatched = matched,
            Style = style,
            Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm" + (stretched ? $" (meeting at {outgoing.Bpm * outRate:0.#}: ×{outRate:0.000} out, ×{meet:0.000} in)" : string.Empty) +
                      $", {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)} (harmony {harmony:0.0}), {bars} bar{(bars == 1 ? string.Empty : "s")}{(fit ? ", a perfect fit" : string.Empty)}" +
                      described,
        };
    }
}
