using HumeMusic.Core.Audio;

namespace HumeMusic.Core.Playback;

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
    /// <summary>For the log: what was decided and why.</summary>
    public string Summary { get; init; } = string.Empty;
}

public static class BeatMix
{
    /// <summary>The most a song's tempo is changed to match another (beyond it, the change is heard).</summary>
    public const double MaxStretch = 0.08;
    /// <summary>The longest blend, in bars.</summary>
    public const int MaxBars = 8;

    /// <summary>The bass cut while the other song's bass plays (Hz).</summary>
    public const double BassCut = 300;

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
                Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm (too far to match), {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)}: " +
                          $"the loudness blend ({length:0.0} s) on a bar line",
            };
        }

        // How long they may overlap: beat-matched songs can share far longer than a plain blend, the
        // more so in keys that sit well together; lyrics, where known, keep two voices apart.
        // How long they may overlap, at most eight bars. Where the singing is known (synced lyrics):
        // an instrumental ending into an opening that suits it (keys that sit together, the same sort
        // of energy at the join) shares as much as both songs' instrumental stretches allow, as Spotify
        // does; the voices still never meet. Otherwise the blend follows the loudness plan's lead.
        var bars = harmony >= 0.55 ? MaxBars : 4;
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
        // holds until the swap and leaves over the rest. A one-bar mix is a quick, beat-aligned cut.
        var shape = swapAt is { } swap
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
            Summary = $"{outgoing.Bpm:0.#} → {incoming.Bpm:0.#} bpm" + (matched ? $" (×{ratio:0.000})" : " (too far to match)") +
                      $", {AudioAnalysis.KeyName(outgoing.Key)} → {AudioAnalysis.KeyName(incoming.Key)} (harmony {harmony:0.0}), {bars} bar{(bars == 1 ? string.Empty : "s")}" +
                      (swapAt is not null ? ", bass swap halfway" : string.Empty),
        };
    }
}
