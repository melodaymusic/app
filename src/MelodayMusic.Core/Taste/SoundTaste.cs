using System.Numerics;
using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Taste;

public readonly record struct SoundSample(string Key, double Weight, double[]? Periods = null);

public sealed class SoundTaste
{
    public const int PeriodCount = 4;
    public const int MinimumSongs = 8;
    public const int MaxClusters = 5;
    private const double MinClusterShare = 0.08;
    private const double MinCoherence = 0.05;
    private const double FitFrom = 0.6;

    private readonly Dictionary<string, int> _index;
    private readonly float[] _general;
    private readonly float[]?[] _periods;

    private SoundTaste(Dictionary<string, int> index, float[] general, float[]?[] periods, int songs, int clusters)
    {
        _index = index;
        _general = general;
        _periods = periods;
        Songs = songs;
        Clusters = clusters;
    }

    public static SoundTaste Empty { get; } = new([], [], new float[]?[PeriodCount], 0, 0);

    public int Songs { get; }
    public int Clusters { get; }
    public int Scored => _index.Count;
    public bool IsEmpty => Clusters == 0;
    public bool HasPeriod(int period) => period is >= 0 and < PeriodCount && _periods[period] is not null;

    public static int PeriodOf(int hour) => Math.Clamp(hour, 0, 23) / 6;

    public double Fit(string key) => _index.TryGetValue(key, out var i) ? _general[i] : 0;

    public double Fit(string key, int hour)
    {
        if (!_index.TryGetValue(key, out var i)) return 0;
        return _periods[PeriodOf(hour)] is { } period ? period[i] : _general[i];
    }

    public double Lift(string key, int hour)
    {
        if (!_index.TryGetValue(key, out var i) || _periods[PeriodOf(hour)] is not { } period) return 0;
        return Math.Clamp(2 * (period[i] - _general[i]), 0, 1);
    }

    public static IReadOnlyList<SoundSample> Samples(TasteProfile profile, TasteMemory? memory)
    {
        var samples = new Dictionary<string, (double Weight, double[]? Periods)>(StringComparer.Ordinal);
        foreach (var (key, weight, periods) in profile.PlayedTracks()) samples[key] = (weight, periods);
        if (memory is not null)
        {
            foreach (var (key, m) in memory.RememberedTracks())
            {
                var (weight, periods) = samples.GetValueOrDefault(key);
                var warmth = 0.5 * m.Warmth * Math.Min(1, m.Finishes + m.Picks);
                var skips = m.SkipRisk * Math.Min(1, m.Skips);
                samples[key] = (weight + warmth - 1.5 * skips, periods);
            }
        }
        return samples.Where(s => Math.Abs(s.Value.Weight) >= 0.05)
            .Select(s => new SoundSample(s.Key, Math.Clamp(s.Value.Weight, -1, 1.5), s.Value.Periods)).ToList();
    }

    public static SoundTaste Build(IReadOnlyDictionary<string, float[]> fingerprints, IEnumerable<SoundSample> samples, CancellationToken ct = default)
    {
        var dims = fingerprints.Values.Where(v => v.Length > 0).GroupBy(v => v.Length).MaxBy(g => g.Count())?.Key ?? 0;
        var library = fingerprints.Where(kv => kv.Value.Length == dims && Finite(kv.Value)).ToList();
        if (dims == 0 || library.Count < MinimumSongs) return Empty;

        var centre = new double[dims];
        foreach (var (_, v) in library)
            for (var d = 0; d < dims; d++) centre[d] += v[d];
        for (var d = 0; d < dims; d++) centre[d] /= library.Count;
        float[] Centred(float[] v) => MusicSimilarity.Normalize(v.Select((x, d) => (float)(x - centre[d])));

        var liked = new List<(float[] Vector, double Weight, double[]? Periods)>();
        var skipped = new List<(float[] Vector, double Weight)>();
        foreach (var s in samples)
        {
            if (!double.IsFinite(s.Weight) || s.Weight == 0 || !fingerprints.TryGetValue(s.Key, out var raw) || raw.Length != dims) continue;
            var v = Centred(raw);
            if (v.Length != dims) continue;
            if (s.Weight > 0) liked.Add((v, s.Weight, s.Periods));
            else skipped.Add((v, -s.Weight));
        }
        if (liked.Count < MinimumSongs) return Empty;
        ct.ThrowIfCancellationRequested();

        var k = liked.Count < 24 ? 1 : liked.Count < 60 ? 3 : liked.Count < 150 ? 4 : MaxClusters;
        var clusters = Cluster(liked.Select(l => (l.Vector, l.Weight)).ToList(), k, ct: ct);
        clusters = Subtract(clusters, liked.Select(l => (l.Vector, l.Weight)).ToList(), skipped);
        if (clusters.Count == 0) return Empty;

        var periods = new float[]?[PeriodCount];
        var total = liked.Sum(l => l.Weight);
        for (var p = 0; p < PeriodCount; p++)
        {
            var then = liked.Where(l => l.Periods is { Length: PeriodCount } h && h.Sum() > 0 && h[p] > 0)
                .Select(l => (l.Vector, Weight: l.Weight * l.Periods![p] / l.Periods.Sum())).ToList();
            if (then.Count < MinimumSongs || then.Sum(t => t.Weight) < 0.1 * total || Centroid(then) is not { Length: > 0 } start) continue;
            if (Subtract([start], then, skipped) is [var centroid]) periods[p] = centroid;
        }
        ct.ThrowIfCancellationRequested();

        var index = new Dictionary<string, int>(library.Count, StringComparer.Ordinal);
        var general = new double[library.Count];
        var byPeriod = periods.Select(c => c is null ? null : new double[library.Count]).ToArray();
        var centreF = centre.Select(x => (float)x).ToArray();
        var centreNorm = Dot(centreF, centreF);
        var clusterCentre = clusters.Select(c => Dot(centreF, c)).ToArray();
        var periodCentre = periods.Select(c => c is null ? 0 : Dot(centreF, c)).ToArray();
        for (var i = 0; i < library.Count; i++)
        {
            if (i % 2048 == 0) ct.ThrowIfCancellationRequested();
            var (key, v) = library[i];
            index[key] = i;
            var length = Math.Sqrt(Math.Max(0, Dot(v, v) - 2 * Dot(v, centreF) + centreNorm));
            if (length < 1e-9) continue;
            var best = double.NegativeInfinity;
            for (var j = 0; j < clusters.Count; j++) best = Math.Max(best, (Dot(v, clusters[j]) - clusterCentre[j]) / length);
            general[i] = best;
            for (var p = 0; p < PeriodCount; p++)
                if (periods[p] is { } centroid) byPeriod[p]![i] = (Dot(v, centroid) - periodCentre[p]) / length;
        }
        return new SoundTaste(index, Ranked(general), byPeriod.Select(b => b is null ? null : Ranked(b)).ToArray(), liked.Count, clusters.Count);
    }

    public static IReadOnlyList<float[]> Cluster(IReadOnlyList<(float[] Vector, double Weight)> points, int k, int iterations = 20, CancellationToken ct = default)
    {
        if (points.Count == 0 || k <= 0) return [];
        k = Math.Min(k, points.Count);
        var centroids = new List<float[]> { points.MaxBy(p => p.Weight).Vector };
        while (centroids.Count < k)
        {
            var next = points.MaxBy(p => p.Weight * (1 - centroids.Max(c => Dot(c, p.Vector))));
            if (centroids.Max(c => Dot(c, next.Vector)) > 0.999) break;
            centroids.Add(next.Vector);
        }
        var assigned = new int[points.Count];
        for (var round = 0; round < iterations; round++)
        {
            ct.ThrowIfCancellationRequested();
            var moved = Assign(points, centroids, assigned);
            centroids = Centroids(points, assigned, centroids.Count);
            if (!moved && round > 0) break;
        }

        Assign(points, centroids, assigned);
        var total = points.Sum(p => p.Weight);
        var mass = Enumerable.Range(0, centroids.Count).Select(c => Enumerable.Range(0, points.Count).Where(i => assigned[i] == c).Sum(i => points[i].Weight)).ToArray();
        var kept = Enumerable.Range(0, centroids.Count).Where(c => mass[c] >= MinClusterShare * total).OrderByDescending(c => mass[c]).Select(c => centroids[c]).ToList();
        if (kept.Count == 0) kept = [Centroid(points)];
        if (kept.Count < centroids.Count)
        {
            Assign(points, kept, assigned);
            kept = Centroids(points, assigned, kept.Count);
        }
        return kept.Where(c => c.Length > 0).ToList();
    }

    private static float[] Centroid(IReadOnlyList<(float[] Vector, double Weight)> points, IReadOnlyList<(float[] Vector, double Weight)>? against = null, double share = 0)
    {
        if (points.Count == 0) return [];
        var dims = points[0].Vector.Length;
        var sum = new double[dims];
        double mass = 0;
        foreach (var (v, w) in points)
        {
            mass += w;
            for (var d = 0; d < dims; d++) sum[d] += w * v[d];
        }
        if (mass <= 0) return [];
        var coherence = Math.Sqrt(sum.Sum(x => x * x)) / mass;
        if (coherence < MinCoherence) return [];
        if (against is { Count: > 0 } && share > 0)
            foreach (var (v, w) in against)
                for (var d = 0; d < dims; d++) sum[d] -= share * w * v[d];
        return MusicSimilarity.Normalize(sum.Select(x => (float)x));
    }

    private static List<float[]> Subtract(IReadOnlyList<float[]> clusters, IReadOnlyList<(float[] Vector, double Weight)> liked, IReadOnlyList<(float[] Vector, double Weight)> skipped)
    {
        var result = new List<float[]>();
        if (clusters.Count == 0) return result;
        var likedAt = new int[liked.Count];
        Assign(liked, clusters, likedAt);
        var skippedAt = new int[skipped.Count];
        Assign(skipped, clusters, skippedAt);
        for (var c = 0; c < clusters.Count; c++)
        {
            var mine = Enumerable.Range(0, liked.Count).Where(i => likedAt[i] == c).Select(i => liked[i]).ToList();
            var against = Enumerable.Range(0, skipped.Count).Where(i => skippedAt[i] == c).Select(i => skipped[i]).ToList();
            var likedMass = mine.Sum(m => m.Weight);
            var skippedMass = against.Sum(s => s.Weight);
            var share = skippedMass <= 0 ? 0 : Math.Min(1, 0.5 * likedMass / skippedMass);
            var centroid = Centroid(mine, against, share);
            if (centroid.Length > 0) result.Add(centroid);
        }
        return result;
    }

    private static bool Assign(IReadOnlyList<(float[] Vector, double Weight)> points, IReadOnlyList<float[]> centroids, int[] assigned)
    {
        var moved = false;
        for (var i = 0; i < points.Count; i++)
        {
            int best = 0;
            var bestScore = double.NegativeInfinity;
            for (var c = 0; c < centroids.Count; c++)
            {
                if (centroids[c].Length != points[i].Vector.Length) continue;
                var score = Dot(centroids[c], points[i].Vector);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (assigned[i] != best) { assigned[i] = best; moved = true; }
        }
        return moved;
    }

    private static double Dot(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        var sum = Vector<float>.Zero;
        var i = 0;
        for (; i <= n - Vector<float>.Count; i += Vector<float>.Count)
            sum += new Vector<float>(a, i) * new Vector<float>(b, i);
        double total = Vector.Sum(sum);
        for (; i < n; i++) total += a[i] * (double)b[i];
        return total;
    }

    private static bool Finite(float[] v)
    {
        foreach (var x in v)
            if (!float.IsFinite(x)) return false;
        return true;
    }

    private static List<float[]> Centroids(IReadOnlyList<(float[] Vector, double Weight)> points, int[] assigned, int count) =>
        Enumerable.Range(0, count).Select(c => Centroid(Enumerable.Range(0, points.Count).Where(i => assigned[i] == c).Select(i => points[i]).ToList()))
            .Where(c => c.Length > 0).ToList();

    private static float[] Ranked(double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
        var fits = new float[values.Length];
        for (var rank = 0; rank < order.Length; rank++)
        {
            var share = order.Length == 1 ? 1 : rank / (double)(order.Length - 1);
            var x = Math.Max(0, (share - FitFrom) / (1 - FitFrom));
            fits[order[rank]] = (float)(x * x);
        }
        return fits;
    }
}
