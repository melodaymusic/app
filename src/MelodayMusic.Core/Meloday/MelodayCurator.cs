using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Meloday;

public static class MelodayCurator
{
    public const int MaxTracks = 50;
    public const int ArtistLimit = 3;
    public const int ArtistGap = 3;
    public const double HistoricalRatio = 0.3;

    public static IReadOnlyList<string> DefaultSkippedGenres { get; } = ["NoMeloday", "Christmas", "Soundtrack", "Lofi", "Chillwave"];

    public static bool IsSkipped(IEnumerable<string> genres, IReadOnlyCollection<string> skipped) =>
        skipped.Count > 0 && genres.Any(g => skipped.Any(s => g.Contains(s, StringComparison.OrdinalIgnoreCase)));

    public static List<string> SampleHistory(IReadOnlyList<string> candidates, PeriodProfile profile, IReadOnlyDictionary<string, int> playCounts,
        IReadOnlyDictionary<string, double> affinity, Random random, int size = MaxTracks)
    {
        if (candidates.Count == 0) return [];
        var defining = profile.DefiningKeys.ToHashSet(StringComparer.Ordinal);
        var weights = candidates.Select(k =>
        {
            var plays = playCounts.GetValueOrDefault(k, 1);
            var rarity = plays <= 2 ? 1.5 : plays <= 5 ? 1.2 : 1.0;
            return affinity.GetValueOrDefault(k, 0.01) * (defining.Contains(k) ? 2.0 : 1.0) * rarity;
        }).ToArray();
        var sampleSize = Math.Min(candidates.Count, size);
        var total = weights.Sum();
        if (total <= 0) return candidates.Take(size).ToList();

        var chosen = new List<string>();
        var seen = new HashSet<int>();
        for (var draw = 0; draw < sampleSize * 3 && chosen.Count < sampleSize; draw++)
        {
            var target = random.NextDouble() * total;
            var i = 0;
            for (var acc = weights[0]; acc < target && i < weights.Length - 1; acc += weights[++i]) { }
            if (seen.Add(i)) chosen.Add(candidates[i]);
        }
        return chosen.Count < sampleSize ? candidates.Take(sampleSize).ToList() : chosen;
    }

    public static List<Track> CapGenre(List<Track> tracks, Func<Track, IReadOnlyCollection<string>> genresOf, int maxTracks = MaxTracks)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tracks)
            foreach (var g in genresOf(t)) counts[g] = counts.GetValueOrDefault(g) + 1;
        if (counts.Count == 0) return tracks;
        var (top, count) = counts.OrderByDescending(c => c.Value).Select(c => (c.Key, c.Value)).First();
        var cap = (int)(maxTracks * 0.3);
        if (count <= cap) return tracks;
        var inGenre = tracks.Where(t => genresOf(t).Contains(top)).Take(cap);
        var outGenre = tracks.Where(t => !genresOf(t).Contains(top)).Take(cap);
        return outGenre.Concat(inGenre).ToList();
    }

    public static List<Track> BiasToTheme(IEnumerable<Track> tracks, string? genre, string? mood, Func<Track, AlbumTags?> album, double weight = 1.5) =>
        tracks.Select(t =>
        {
            var tags = album(t);
            var genres = tags?.Genres ?? (IEnumerable<string>)t.Genres;
            var moods = tags?.Moods ?? (IEnumerable<string>)t.Moods;
            var score = 1.0;
            if (genre is not null && genres.Any(g => g.Equals(genre, StringComparison.OrdinalIgnoreCase))) score *= weight;
            if (mood is not null && moods.Any(m => m.Equals(mood, StringComparison.OrdinalIgnoreCase))) score *= weight;
            return (Score: score, Track: t);
        })
        .OrderByDescending(x => x.Score)
        .Select(x => x.Track)
        .ToList();

    public static string ArtistOf(Track t) => (t.ArtistTitle ?? t.TrackArtist ?? string.Empty).ToLowerInvariant();

    public static List<Track> CapArtists(IEnumerable<Track> tracks, int limit = ArtistLimit, Dictionary<string, int>? counts = null)
    {
        counts ??= new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new List<Track>();
        foreach (var t in tracks)
        {
            var artist = ArtistOf(t);
            if (counts.GetValueOrDefault(artist) >= limit) continue;
            kept.Add(t);
            counts[artist] = counts.GetValueOrDefault(artist) + 1;
        }
        return kept;
    }

    public static List<Track> SpreadArtists(IReadOnlyList<Track> tracks, int minGap = ArtistGap)
    {
        var pool = tracks.ToList();
        var result = new List<Track>(pool.Count);
        var recent = new List<string>();
        while (pool.Count > 0)
        {
            var window = recent.Skip(Math.Max(0, recent.Count - minGap)).ToHashSet(StringComparer.Ordinal);
            var index = pool.FindIndex(t => !window.Contains(ArtistOf(t)));
            if (index < 0) index = 0;
            var chosen = pool[index];
            pool.RemoveAt(index);
            result.Add(chosen);
            recent.Add(ArtistOf(chosen));
        }
        return result;
    }

    public static List<Track> Pathfind(IReadOnlyList<Track> pool, IReadOnlyDictionary<string, IReadOnlyList<string>> similar, int maxTracks = MaxTracks)
    {
        if (pool.Count == 0) return [];
        var remaining = pool.Skip(1).ToList();
        var inPool = pool.Select(p => p.RatingKey).ToHashSet(StringComparer.Ordinal);
        var ranks = pool.DistinctBy(t => t.RatingKey).ToDictionary(t => t.RatingKey, t =>
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            var list = similar.GetValueOrDefault(t.RatingKey) ?? [];
            for (var i = 0; i < list.Count; i++)
                if (inPool.Contains(list[i])) map.TryAdd(list[i], i);
            return map;
        }, StringComparer.Ordinal);

        var current = pool[0];
        var ordered = new List<Track> { current };
        while (remaining.Count > 0 && ordered.Count < maxTracks)
        {
            var near = ranks.GetValueOrDefault(current.RatingKey) ?? [];
            var next = remaining.MinBy(t => near.GetValueOrDefault(t.RatingKey, 999))!;
            remaining.Remove(next);
            ordered.Add(next);
            current = next;
        }
        return ordered;
    }
}
