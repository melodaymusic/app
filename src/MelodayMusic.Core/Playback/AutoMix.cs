namespace MelodayMusic.Core.Playback;

public sealed record TrackLoudness(IReadOnlyList<double> Levels, double? GainDb, VocalSpan? Vocals = null);

public sealed record VocalSpan(TimeSpan First, TimeSpan LastEnd)
{
    private static readonly TimeSpan LineLength = TimeSpan.FromSeconds(4);

    public static VocalSpan? From(IReadOnlyList<(TimeSpan Start, TimeSpan? End)> lines)
    {
        if (lines.Count == 0) return null;
        var ordered = lines.OrderBy(l => l.Start).ToList();
        var last = ordered[^1];
        var end = last.End is { } e && e > last.Start ? e : last.Start + LineLength;
        return new VocalSpan(ordered[0].Start, end);
    }
}

public sealed record CrossfadeShape(TimeSpan Overlap, TimeSpan InFade, TimeSpan OutFadeStart, TimeSpan OutFade)
{
    public static CrossfadeShape Even(TimeSpan overlap) => new(overlap, overlap, TimeSpan.Zero, overlap);

    public (double Outgoing, double Incoming) Gains(TimeSpan elapsed)
    {
        var incoming = InFade <= TimeSpan.Zero ? 1 : Math.Sin(Math.Clamp(elapsed / InFade, 0, 1) * Math.PI / 2);
        var outgoing = elapsed < OutFadeStart ? 1
            : OutFade <= TimeSpan.Zero ? 0
            : Math.Cos(Math.Clamp((elapsed - OutFadeStart) / OutFade, 0, 1) * Math.PI / 2);
        return (outgoing, incoming);
    }
}

public sealed record Transition(TimeSpan StartAt, TimeSpan IncomingStart, CrossfadeShape Shape)
{
    public TimeSpan Fade => Shape.Overlap;
}

public static class AutoMix
{
    public const double SilenceDb = -36;
    public const double LoudDb = 6;
    public const double ClashDb = 10;
    public const double DipDb = 14;

    public static readonly TimeSpan ShortestOverlap = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan LongestOverlap = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan LongestLeadIn = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan QuickestRise = TimeSpan.FromSeconds(0.4);
    public static readonly TimeSpan LongestFadeAway = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(0.25);

    private const double ClashWeight = 1.0;
    private const double DipWeight = 0.6;
    private const double VocalClashWeight = 40;
    private const double SmoothnessPerSecond = 0.15;

    public static Transition? Plan(TrackLoudness? outgoing, TimeSpan outgoingDuration, TrackLoudness? incoming, TimeSpan incomingDuration)
    {
        if (outgoing is not { Levels.Count: >= 16 } || outgoingDuration < TimeSpan.FromSeconds(30)) return null;
        var a = new Envelope(outgoing, outgoingDuration);
        if (a.SoundEnd <= TimeSpan.Zero) return null;
        var b = incoming is { Levels.Count: >= 16 } && incomingDuration > TimeSpan.Zero ? new Envelope(incoming, incomingDuration) : null;
        var leadIn = LeadIn(incoming, incomingDuration);

        var windowStart = a.SoundEnd - LongestOverlap;
        var windowEnd = a.SoundEnd + TimeSpan.FromSeconds(2);
        var longest = Min(LongestOverlap, a.SoundEnd - outgoingDuration / 2);
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

            if (la > -ClashDb && lb > -ClashDb) cost += ClashWeight * (Math.Min(la, lb) + ClashDb) * dt;
            var together = 10 * Math.Log10(Math.Pow(10, la / 10) + Math.Pow(10, lb / 10));
            if (together < -DipDb) cost += DipWeight * Math.Min(-DipDb - together, 30) * dt;
            if (la > -20 && lb > -20 && outVocals is { } ov && inVocals is { } iv && t <= ov.LastEnd && bAt >= iv.First && bAt <= iv.LastEnd)
                cost += VocalClashWeight * dt;
        }
        return cost;
    }

    private static CrossfadeShape Shape(Envelope a, Envelope? b, TimeSpan leadIn, TimeSpan overlap)
    {
        var full = b?.FirstLoud(leadIn) - leadIn ?? TimeSpan.Zero;
        var inFade = full > overlap / 2 ? QuickestRise : Max(QuickestRise, Min(overlap / 2, TimeSpan.FromSeconds(2)));
        var fadeAway = Min(overlap, LongestFadeAway);
        var outStart = overlap - fadeAway;
        if (full > TimeSpan.Zero && full < outStart) outStart = full;
        if (full == TimeSpan.Zero) outStart = TimeSpan.Zero;
        return new CrossfadeShape(overlap, Min(inFade, overlap), outStart, overlap - outStart);
    }

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

    public static double LevelFor(TrackLoudness? track) =>
        track?.GainDb is { } gain ? Math.Clamp(Math.Pow(10, Math.Min(0, gain) / 20), 0.25, 1) : 1;

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
            _usual = sounding.Count == 0 ? SilenceDb : sounding[(int)(sounding.Count * 0.7)];
            var last = -1;
            for (var i = _levels.Count - 1; i >= 0; i--)
                if (_levels[i] > SilenceDb) { last = i; break; }
            SoundEnd = last < 0 ? TimeSpan.Zero : _step * (last + 1);
        }

        public TimeSpan SoundEnd { get; }

        public double Relative(TimeSpan at)
        {
            if (at < TimeSpan.Zero) return double.NegativeInfinity;
            var x = Math.Clamp(at / _step - 0.5, 0, _levels.Count - 1);
            var i = Math.Min((int)x, _levels.Count - 2);
            var f = x - i;
            var level = _levels[i] * (1 - f) + _levels[i + 1] * f;
            return level <= SilenceDb ? double.NegativeInfinity : level - _usual;
        }

        public TimeSpan FirstLoud(TimeSpan from)
        {
            for (var t = from; t < from + LongestOverlap; t += Step)
                if (Relative(t) > -LoudDb) return t;
            return from + LongestOverlap;
        }
    }
}
