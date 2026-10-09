using HumeMusic.Core.Audio;

namespace HumeMusic.Core.Playback;

/// <summary>How the two songs are joined, beyond their volumes: a plain blend (with the bass changing hands), a filter sweep, or an echo out.</summary>
public enum MixStyle
{
    /// <summary>The next song comes in under the outgoing one, the bass changing hands halfway.</summary>
    Blend,
    /// <summary>A DJ's filter sweep: the outgoing song loses its lows and then its mids as it goes, the incoming one opens up from dull.</summary>
    Filter,
    /// <summary>An echo out: the outgoing song repeats itself, fading, to the beat as it leaves (for songs whose tempos can't be matched).</summary>
    Echo,
}

/// <summary>
/// A beat-matched mix from one song into the next, as a DJ (or Apple's AutoMix) does it: the next song
/// comes in on a bar line, playing at the outgoing song's tempo so their beats fall together; their
/// bass is swapped on a bar line halfway, so the two kick drums never fight; and once the outgoing
/// song has gone, the new one eases back to its own tempo.
/// </summary>
public sealed record BeatMixPlan
{
    /// <summary>Where in the outgoing song the next one comes in (a bar line).</summary>
    public required TimeSpan StartAt { get; init; }
    /// <summary>Where the next song starts playing from (its first bar line).</summary>
    public required TimeSpan IncomingStart { get; init; }
    /// <summary>How the two songs' volumes move (over the overlap, timed by the outgoing song).</summary>
    public required CrossfadeShape Shape { get; init; }
    /// <summary>How much faster (or slower) than written the next song plays while the two overlap.</summary>
    public double TempoRatio { get; init; } = 1;
    /// <summary>How long the next song takes to ease back to its own tempo once alone.</summary>
    public TimeSpan RampBack { get; init; }
    /// <summary>When the bass changes hands, into the overlap (null: no swap; a plain blend).</summary>
    public TimeSpan? BassSwap { get; init; }
    /// <summary>How long the bass swap takes (about a beat).</summary>
    public TimeSpan SwapLength { get; init; }
    public int Bars { get; init; }
    public bool BeatMatched { get; init; }
    /// <summary>How the songs are joined beyond their volumes (see <see cref="MixStyle"/>).</summary>
    public MixStyle Style { get; init; }
    /// <summary>The echo's delay when the style is an echo out (a dotted eighth at the outgoing tempo).</summary>
    public TimeSpan EchoDelay { get; init; } = TimeSpan.FromSeconds(0.375);
    /// <summary>For the log: what was decided and why.</summary>
    public string Summary { get; init; } = string.Empty;
}

public static class BeatMix
{
    /// <summary>The most a song's tempo is changed to match another (beyond it, the change is heard).</summary>
    public const double MaxStretch = 0.08;
    /// <summary>The longest blend, in bars: for a pair that fits as well as two songs can (see <see cref="Fit"/>).</summary>
    public const int MaxBars = 16;
    /// <summary>The longest blend for a pair that merely suits each other.</summary>
    public const int SuitedBars = 8;

    /// <summary>The bass cut while the other song's bass plays (Hz).</summary>
    public const double BassCut = 300;
    /// <summary>A filter sweep: how high the outgoing song's high-pass climbs (Hz), where the incoming song's low-pass starts and where it ends (Hz).</summary>
    public const double FilterTop = 2600, FilterOpening = 700, FilterOpen = 16000;
    /// <summary>How much echo an echo out reaches (0-1).</summary>
    public const double EchoAmount = 0.6;

    /// <summary>A dotted eighth at the tempo, kept within what an echo can use (seconds).</summary>
    public static TimeSpan EchoDelayFor(double beatPeriod) => TimeSpan.FromSeconds(Math.Clamp(beatPeriod * 0.75, 0.2, 0.8));

    /// <summary>
    /// The tempo the next song plays at so its beats meet the outgoing song's: the same tempo, or its
    /// double or half (a 174 song meets an 87 one every other beat). Null when even the nearest is
    /// further off than <see cref="MaxStretch"/>.
    /// </summary>
    public static double? TempoRatio(double outgoingBpm, double incomingBpm)
    {
        if (outgoingBpm <= 0 || incomingBpm <= 0) return null;
        var best = new[] { 1.0, 2.0, 0.5 }
            .Select(k => outgoingBpm * k / incomingBpm)
            .OrderBy(r => Math.Abs(Math.Log(r)))
            .First();
        return Math.Abs(best - 1) <= MaxStretch ? best : null;
    }

    /// <summary>How well the two ends match in energy, 0 to 1 (the last bars going out against the first bars coming in); null when either hasn't been measured.</summary>
    public static double? EnergyFit(AudioAnalysis outgoing, AudioAnalysis incoming)
    {
        if (outgoing.BeatEnergy.Count < 8 || incoming.BeatEnergy.Count < 8) return null;
        var outEnd = outgoing.BeatEnergy.TakeLast(16).Average();
        var inStart = incoming.BeatEnergy.Take(16).Average();
        return Math.Exp(-Math.Abs(outEnd - inStart) / 5.0);
    }

    /// <summary>
    /// Whether a pair fits as well as two songs can, so that a sixteen-bar blend is worth it: keys that sit
    /// together (the same, or a fifth apart), tempos within a few per cent (so the stretch is never heard),
    /// and the same sort of energy either side of the join (within about 2.5 dB). Unmeasured energy is no
    /// evidence, so it doesn't count.
    /// </summary>
    public static bool Fit(double harmony, double? ratio, double? energyFit) =>
        harmony >= 0.9 && ratio is { } r && Math.Abs(r - 1) <= 0.03 && energyFit is >= 0.6;

    /// <summary>
    /// Whether the next song's opening suits the outgoing ending well enough for a long blend: keys
    /// that sit together, and the same sort of energy either side of the join (the last bars going
    /// out against the first bars coming in, within 6 dB).
    /// </summary>
    public static bool Suits(AudioAnalysis outgoing, AudioAnalysis incoming, double harmony)
    {
        if (harmony < 0.55 || outgoing.BeatEnergy.Count < 8 || incoming.BeatEnergy.Count < 8) return false;
        var outEnd = outgoing.BeatEnergy.TakeLast(16).Average();
        var inStart = incoming.BeatEnergy.Take(16).Average();
        return Math.Abs(outEnd - inStart) <= 6;
    }

    /// <summary>
    /// Plans the mix from the outgoing song's closing analysis and the next one's opening analysis,
    /// starting from AutoMix's loudness plan (<paramref name="loudness"/>: where the outgoing song's
    /// sound ends, and how much overlap the two can take). Null when either has no steady beat: the
    /// loudness plan is used as it is.
    /// </summary>
    public static BeatMixPlan? Plan(Transition loudness, AudioAnalysis outgoing, AudioAnalysis incoming, VocalSpan? outVocals = null, VocalSpan? inVocals = null)
    {
        if (!outgoing.HasBeat || !incoming.HasBeat) return null;
        var soundEnd = (loudness.StartAt + loudness.Fade).TotalSeconds;
        var bar = 4 * outgoing.BeatPeriod;
        var ratio = TempoRatio(outgoing.Bpm, incoming.Bpm);
        var matched = ratio is not null;
        var harmony = AudioAnalysis.KeyHarmony(outgoing.Key, incoming.Key);

        // The next song comes in on its first bar line (past any silence).
        var lead = loudness.IncomingStart.TotalSeconds;
        var inStart = incoming.DownbeatAtOrAfter(lead) is { } d && d - lead < 2 * 4 * incoming.BeatPeriod ? d : lead;

        var loudnessOverlap = loudness.Fade.TotalSeconds;

        // Tempos too far apart to match: AutoMix's loudness blend as it was planned (the outgoing song
        // fading, the next coming in under it), only brought onto the nearest bar line.
        if (!matched)
        {
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
                // The tempos can't be matched, so the join is hidden in an echo of the outgoing song, to its beat.
                Style = MixStyle.Echo,
                EchoDelay = EchoDelayFor(outgoing.BeatPeriod),
                Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm (too far to match), {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)}: " +
                          $"the loudness blend ({length:0.0} s) on a bar line, with an echo out",
            };
        }

        // How long they may overlap: beat-matched songs can share far longer than a plain blend, the
        // more so in keys that sit well together; lyrics, where known, keep two voices apart.
        // How long they may overlap, at most eight bars. Where the singing is known (synced lyrics):
        // an instrumental ending into an opening that suits it (keys that sit together, the same sort
        // of energy at the join) shares as much as both songs' instrumental stretches allow, as Spotify
        // does; the voices still never meet. Otherwise the blend follows the loudness plan's lead.
        var fit = Fit(harmony, ratio, EnergyFit(outgoing, incoming));
        var bars = fit ? MaxBars : harmony >= 0.55 ? SuitedBars : 4;
        var cap = Math.Max(loudnessOverlap * 2.5, 2 * bar);
        if (outVocals is not null && inVocals is not null)
        {
            // The outgoing singing must be over before the incoming begins, within the overlap.
            var outFree = Math.Max(0, soundEnd - outVocals.LastEnd.TotalSeconds);
            var inFree = Math.Max(0, (inVocals.First.TotalSeconds - inStart) / (ratio ?? 1));
            var instrumental = outFree + inFree;
            cap = Suits(outgoing, incoming, harmony) ? Math.Max(bar, instrumental) : Math.Min(cap, Math.Max(bar, instrumental));
        }
        while (bars > 1 && bars * bar > cap + 1e-6) bars /= 2;
        // The next song must have been analysed that far in (its first minute or so), and the blend must not
        // begin before the outgoing song's middle, so a long blend never eats a short song.
        while (bars > 1 && (inStart + bars * bar * (ratio ?? 1) > incoming.To - 1 || soundEnd - bars * bar < soundEnd / 2)) bars /= 2;
        var overlap = bars * bar;

        // The outgoing bar line from which the overlap ends as its sound does (or just before).
        var start = outgoing.Downbeats.Where(t => t + overlap <= soundEnd + 0.5 * outgoing.BeatPeriod && t >= outgoing.From)
            .Select(t => (double?)t).LastOrDefault();
        if (start is null) return null;
        // Don't come in absurdly early for a long blend if the analysis ran out: keep it near the plan.
        if (soundEnd - (start.Value + overlap) > 2 * bar) return null;

        var overlapSpan = TimeSpan.FromSeconds(overlap);
        var swapAt = bars >= 2 ? TimeSpan.FromSeconds(bars / 2 * bar) : (TimeSpan?)null;
        var beat = TimeSpan.FromSeconds(outgoing.BeatPeriod);
        // Volumes: the next song rises through the first half (its bass held back); the outgoing one
        // holds until the swap and leaves over the rest. A one-bar mix is a quick, beat-aligned cut. A
        // sixteen-bar blend takes its time at both ends: the next song is up by bar six, both play in full
        // from there to bar ten (the bass changing hands in the middle of it), and the outgoing one is gone
        // by bar sixteen.
        var shape = bars >= MaxBars && swapAt is { } longSwap
            ? new CrossfadeShape(overlapSpan, overlapSpan * (6.0 / 16), overlapSpan * (10.0 / 16), overlapSpan * (6.0 / 16))
            : swapAt is { } swap
                ? new CrossfadeShape(overlapSpan, swap, swap, overlapSpan - swap)
                : new CrossfadeShape(overlapSpan, TimeSpan.FromSeconds(Math.Min(overlap / 2, 1)), TimeSpan.Zero, overlapSpan);

        return new BeatMixPlan
        {
            StartAt = TimeSpan.FromSeconds(start.Value),
            IncomingStart = TimeSpan.FromSeconds(inStart),
            Shape = shape,
            TempoRatio = ratio ?? 1,
            RampBack = matched && Math.Abs((ratio ?? 1) - 1) > 0.002 ? TimeSpan.FromSeconds(8 * 4 * incoming.BeatPeriod) : TimeSpan.Zero,
            BassSwap = swapAt,
            SwapLength = beat,
            Bars = bars,
            BeatMatched = matched,
            // A pair that fits perfectly is blended long, the bass changing hands. Any other pair (a blend of two bars or more) gets a
            // filter sweep, which hides what doesn't quite fit (the outgoing song thins out as the next opens up).
            Style = !fit && swapAt is not null ? MixStyle.Filter : MixStyle.Blend,
            Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm" + (matched ? $" (×{ratio:0.000})" : " (too far to match)") +
                      $", {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)} (harmony {harmony:0.0}), {bars} bar{(bars == 1 ? string.Empty : "s")}{(fit ? ", a perfect fit" : string.Empty)}" +
                      (swapAt is not null ? (!fit ? ", filter sweep from halfway" : ", bass swap halfway") : string.Empty),
        };
    }
}
