namespace HumeMusic.Core.Playback;

/// <summary>
/// A track's loudness from Plex's analysis: its level across its length (dB, evenly spaced), its gain,
/// and, when it has synced lyrics, when the singing starts and ends.
/// </summary>
public sealed record TrackLoudness(IReadOnlyList<double> Levels, double? GainDb, VocalSpan? Vocals = null);

/// <summary>When a song's singing starts (its first lyric line) and ends (the end of its last).</summary>
public sealed record VocalSpan(TimeSpan First, TimeSpan LastEnd)
{
    /// <summary>A last line with no end time is taken to last this long.</summary>
    private static readonly TimeSpan LineLength = TimeSpan.FromSeconds(4);

    /// <summary>From synced lyric lines (start, and end when known), or null without any.</summary>
    public static VocalSpan? From(IReadOnlyList<(TimeSpan Start, TimeSpan? End)> lines)
    {
        if (lines.Count == 0) return null;
        var ordered = lines.OrderBy(l => l.Start).ToList();
        var last = ordered[^1];
        var end = last.End is { } e && e > last.Start ? e : last.Start + LineLength;
        return new VocalSpan(ordered[0].Start, end);
    }
}

/// <summary>
/// How the two tracks' volumes move while they overlap: the incoming track comes up over
/// <see cref="InFade"/>, the outgoing one holds until <see cref="OutFadeStart"/> and then goes down over
/// <see cref="OutFade"/>, and after <see cref="Overlap"/> it stops. Equal-power curves throughout.
/// </summary>
public sealed record CrossfadeShape(TimeSpan Overlap, TimeSpan InFade, TimeSpan OutFadeStart, TimeSpan OutFade)
{
    /// <summary>The classic crossfade: both tracks cross evenly across the whole overlap.</summary>
    public static CrossfadeShape Even(TimeSpan overlap) => new(overlap, overlap, TimeSpan.Zero, overlap);

    /// <summary>Each track's gain at <paramref name="elapsed"/> into the overlap (outgoing, incoming).</summary>
    public (double Outgoing, double Incoming) Gains(TimeSpan elapsed)
    {
        var incoming = InFade <= TimeSpan.Zero ? 1 : Math.Sin(Math.Clamp(elapsed / InFade, 0, 1) * Math.PI / 2);
        var outgoing = elapsed < OutFadeStart ? 1
            : OutFade <= TimeSpan.Zero ? 0
            : Math.Cos(Math.Clamp((elapsed - OutFadeStart) / OutFade, 0, 1) * Math.PI / 2);
        return (outgoing, incoming);
    }
}

/// <summary>
/// A handover from one track to the next: when the next one comes in (a position in the outgoing
/// track), where it starts (past any silence before its first sound), and how the two overlap.
/// </summary>
public sealed record Transition(TimeSpan StartAt, TimeSpan IncomingStart, CrossfadeShape Shape)
{
    public TimeSpan Fade => Shape.Overlap;
}

/// <summary>
/// AutoMix, after Plexamp's Sweet Fades and further: every pair of songs gets its own handover,
/// worked out from Plex's loudness analysis of how one ends and the next begins.
/// <list type="bullet">
/// <item>Each possible overlap (half a second to twenty) is played out on the two loudness envelopes
/// and scored: both songs loud at once is a clash, the music sagging between them is a dip, and two
/// voices singing together (when synced lyrics are known) is the worst of all. The best one wins.</item>
/// <item>The songs' own fades do the work: an outro that fades is left to fade, a quiet intro comes in
/// at full volume under it; only what is still loud is eased. A song that ends cold into one that
/// starts loud gets a tight, DJ-style cut.</item>
/// <item>The next song starts at its first sound, past any silence.</item>
/// <item>An album played in order isn't mixed at all (the caller decides); its tracks follow on as the
/// album runs.</item>
/// </list>
/// Apple's AutoMix also matches tempo; Plex has no beat analysis to do that.
/// </summary>
public static class AutoMix
{
    /// <summary>Levels at or below this are silence (Plex's analysis bottoms out at about -40 dB).</summary>
    public const double SilenceDb = -36;
    /// <summary>Within this of a song's usual level, it is "loud".</summary>
    public const double LoudDb = 6;
    /// <summary>Both songs heard above this (below their usual level) at once is a clash, more so the louder the quieter one.</summary>
    public const double ClashDb = 10;
    /// <summary>The music sagging this far below the songs' usual level is a dip.</summary>
    public const double DipDb = 14;

    public static readonly TimeSpan ShortestOverlap = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan LongestOverlap = TimeSpan.FromSeconds(20);
    /// <summary>The longest stretch of silence skipped at the start of the next song.</summary>
    public static readonly TimeSpan LongestLeadIn = TimeSpan.FromSeconds(8);
    /// <summary>The incoming song's quickest rise (a click-free start).</summary>
    public static readonly TimeSpan QuickestRise = TimeSpan.FromSeconds(0.4);
    /// <summary>The outgoing song's last moments are eased away over at most this.</summary>
    public static readonly TimeSpan LongestFadeAway = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(0.25);

    // How much each fault counts, per second (dB above or below the line).
    private const double ClashWeight = 1.0;
    private const double DipWeight = 0.6;
    private const double VocalClashWeight = 40;
    // A gentle pull towards longer, smoother overlaps when nothing is lost by them.
    private const double SmoothnessPerSecond = 0.15;

    /// <summary>
    /// The handover from one song into the next, or null when the outgoing song hasn't been analysed
    /// (the caller falls back to an ordinary crossfade). An unanalysed next song is taken to start loud.
    /// </summary>
    public static Transition? Plan(TrackLoudness? outgoing, TimeSpan outgoingDuration, TrackLoudness? incoming, TimeSpan incomingDuration)
    {
        if (outgoing is not { Levels.Count: >= 16 } || outgoingDuration < TimeSpan.FromSeconds(30)) return null;
        var a = new Envelope(outgoing, outgoingDuration);
        if (a.SoundEnd <= TimeSpan.Zero) return null;
        var b = incoming is { Levels.Count: >= 16 } && incomingDuration > TimeSpan.Zero ? new Envelope(incoming, incomingDuration) : null;
        var leadIn = LeadIn(incoming, incomingDuration);

        // The window judged: the last twenty seconds of the outgoing song's sound and a moment beyond.
        var windowStart = a.SoundEnd - LongestOverlap;
        var windowEnd = a.SoundEnd + TimeSpan.FromSeconds(2);
        var longest = Min(LongestOverlap, a.SoundEnd - outgoingDuration / 2); // never before the middle
        if (longest < ShortestOverlap) return null;

        var best = ShortestOverlap;
        var bestCost = double.MaxValue;
        for (var overlap = ShortestOverlap; overlap <= longest; overlap += Step)
        {
            var shape = Shape(a, b, leadIn, overlap);
            var cost = Cost(a, b, leadIn, shape, windowStart, windowEnd, outgoing.Vocals, incoming?.Vocals) - SmoothnessPerSecond * overlap.TotalSeconds;
            if (cost < bestCost - 1e-9)
            {
                bestCost = cost;
                best = overlap;
            }
        }
        return new Transition(a.SoundEnd - best, leadIn, Shape(a, b, leadIn, best));
    }

    /// <summary>
    /// Faults in a candidate handover: the next song entering where the shape's overlap says (ending as
    /// the outgoing song's sound does), judged across the window as it would be heard, fades and all.
    /// </summary>
    private static double Cost(Envelope a, Envelope? b, TimeSpan leadIn, CrossfadeShape shape, TimeSpan from, TimeSpan to,
        VocalSpan? outVocals, VocalSpan? inVocals)
    {
        var entry = a.SoundEnd - shape.Overlap;
        double cost = 0;
        var dt = Step.TotalSeconds;
        for (var t = from; t < to; t += Step)
        {
            var inB = t >= entry;
            var (ga, gb) = inB ? shape.Gains(t - entry) : (1, 0);
            var la = t <= a.SoundEnd ? a.Relative(t) + Db(ga) : double.NegativeInfinity;
            var bAt = leadIn + (t - entry);
            var lb = inB ? (b?.Relative(bAt) ?? 0) + Db(gb) : double.NegativeInfinity;

            // Both heard at once: the louder the quieter of the two, the more they muddle.
            if (la > -ClashDb && lb > -ClashDb) cost += ClashWeight * (Math.Min(la, lb) + ClashDb) * dt;
            // The music sagging between them.
            var together = 10 * Math.Log10(Math.Pow(10, la / 10) + Math.Pow(10, lb / 10));
            if (together < -DipDb) cost += DipWeight * Math.Min(-DipDb - together, 30) * dt;
            // Two voices at once.
            if (la > -20 && lb > -20 && outVocals is { } ov && inVocals is { } iv && t <= ov.LastEnd && bAt >= iv.First && bAt <= iv.LastEnd)
                cost += VocalClashWeight * dt;
        }
        return cost;
    }

    /// <summary>
    /// How the volumes move once the overlap is chosen: the next song rises as quickly as its own
    /// intro allows (a quiet intro comes in at once, a loud start over a moment), and the outgoing song
    /// is eased away only over its last few seconds, or from where the next song turns loud.
    /// </summary>
    private static CrossfadeShape Shape(Envelope a, Envelope? b, TimeSpan leadIn, TimeSpan overlap)
    {
        // Where the next song reaches its usual level (unknown: at once).
        var full = b?.FirstLoud(leadIn) - leadIn ?? TimeSpan.Zero;
        var inFade = full > overlap / 2 ? QuickestRise : Max(QuickestRise, Min(overlap / 2, TimeSpan.FromSeconds(2)));
        var fadeAway = Min(overlap, LongestFadeAway);
        var outStart = overlap - fadeAway;
        // If the next song turns loud before then, start easing the outgoing one there instead.
        if (full > TimeSpan.Zero && full < outStart) outStart = full;
        if (full == TimeSpan.Zero) outStart = TimeSpan.Zero;
        return new CrossfadeShape(overlap, Min(inFade, overlap), outStart, overlap - outStart);
    }

    /// <summary>Where a song's sound begins: past the silence before it (up to a few seconds), or its start.</summary>
    public static TimeSpan LeadIn(TrackLoudness? track, TimeSpan duration)
    {
        if (track is not { Levels.Count: >= 16 } || duration <= TimeSpan.Zero) return TimeSpan.Zero;
        var first = -1;
        for (var i = 0; i < track.Levels.Count; i++)
            if (track.Levels[i] > SilenceDb) { first = i; break; }
        if (first <= 0) return TimeSpan.Zero;
        var at = duration / track.Levels.Count * first;
        return at > LongestLeadIn ? TimeSpan.Zero : at;
    }

    /// <summary>
    /// The volume (0-1) that brings a song to the shared reference level (Sound Check): Plex's gain,
    /// only ever turning songs down so none is pushed into distortion, and never below a quarter.
    /// </summary>
    public static double LevelFor(TrackLoudness? track) =>
        track?.GainDb is { } gain ? Math.Clamp(Math.Pow(10, Math.Min(0, gain) / 20), 0.25, 1) : 1;

    /// <summary>
    /// Whether the next track simply continues the album being played (its next track, same disc or
    /// the next): those follow on as the album runs rather than being mixed.
    /// </summary>
    public static bool ContinuesAlbum(string? album, int? disc, int? number, string? nextAlbum, int? nextDisc, int? nextNumber)
    {
        if (album is null || album != nextAlbum || number is null || nextNumber is null) return false;
        var d = disc ?? 1;
        var nd = nextDisc ?? 1;
        return (nd == d && nextNumber == number + 1) || (nd == d + 1 && nextNumber == 1);
    }

    private static double Db(double gain) => gain <= 0 ? double.NegativeInfinity : 20 * Math.Log10(gain);

    private static TimeSpan Min(TimeSpan x, TimeSpan y) => x < y ? x : y;
    private static TimeSpan Max(TimeSpan x, TimeSpan y) => x > y ? x : y;

    /// <summary>A song's loudness envelope, relative to its own usual level, sampled by time.</summary>
    private sealed class Envelope
    {
        private readonly IReadOnlyList<double> _levels;
        private readonly TimeSpan _step;
        private readonly double _usual;

        public Envelope(TrackLoudness loudness, TimeSpan duration)
        {
            _levels = loudness.Levels;
            _step = duration / _levels.Count;
            var sounding = _levels.Where(l => l > SilenceDb).OrderBy(l => l).ToList();
            // The song's usual level: the upper middle of its sound (choruses count, quiet bits don't).
            _usual = sounding.Count == 0 ? SilenceDb : sounding[(int)(sounding.Count * 0.7)];
            var last = -1;
            for (var i = _levels.Count - 1; i >= 0; i--)
                if (_levels[i] > SilenceDb) { last = i; break; }
            SoundEnd = last < 0 ? TimeSpan.Zero : _step * (last + 1);
        }

        /// <summary>Where the song's sound ends (anything after is silence).</summary>
        public TimeSpan SoundEnd { get; }

        /// <summary>The level at a moment, relative to the song's usual level (0 = usual, negative = quieter).</summary>
        public double Relative(TimeSpan at)
        {
            if (at < TimeSpan.Zero) return double.NegativeInfinity;
            // Linear between points, so a quarter-second step reads a smooth curve.
            var x = Math.Clamp(at / _step - 0.5, 0, _levels.Count - 1);
            var i = Math.Min((int)x, _levels.Count - 2);
            var f = x - i;
            var level = _levels[i] * (1 - f) + _levels[i + 1] * f;
            return level <= SilenceDb ? double.NegativeInfinity : level - _usual;
        }

        /// <summary>The first moment from <paramref name="from"/> that the song is at its usual loudness.</summary>
        public TimeSpan FirstLoud(TimeSpan from)
        {
            for (var t = from; t < from + LongestOverlap; t += Step)
                if (Relative(t) > -LoudDb) return t;
            return from + LongestOverlap;
        }
    }
}
