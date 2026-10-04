using HumeMusic.Core.Models;

namespace HumeMusic.Core.Meloday;

/// <summary>
/// The steps Meloday takes over a pool of tracks: sampling the period's history, keeping genres out,
/// leaning towards the period's theme, capping and spacing artists, and ordering the result into a
/// sonic gradient. Pure functions of their inputs (and a Random), so they can be tested.
/// </summary>
public static class MelodayCurator
{
    public const int MaxTracks = 50;
    public const int ArtistLimit = 3;
    public const int ArtistGap = 3;
    public const double HistoricalRatio = 0.3;

    /// <summary>The genres Meloday leaves out by default (the script's config), matched as substrings.</summary>
    public static IReadOnlyList<string> DefaultSkippedGenres { get; } = ["NoMeloday", "Christmas", "Soundtrack", "Lofi", "Chillwave"];

    public static bool IsSkipped(IEnumerable<string> genres, IReadOnlyCollection<string> skipped) =>
        skipped.Count > 0 && genres.Any(g => skipped.Any(s => g.Contains(s, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Picks the history tracks for this period: each one's chance is its affinity, doubled for the
    /// tracks that define the period and raised for ones played only a few times; then no genre may
    /// fill more than 30% of the mix.
    /// </summary>
    /// <param name="candidates">History tracks in history order (newest play first), one entry each.</param>
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

    /// <summary>No genre over 30% of the mix: when one does, as many of its tracks as the cap allows follow the others.</summary>
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

    /// <summary>Tracks in the period's dominant genre and mood rise to the top (each match multiplies by the weight).</summary>
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

    /// <summary>The album artist (the script's grandparentTitle), for capping and spacing.</summary>
    public static string ArtistOf(Track t) => (t.ArtistTitle ?? t.TrackArtist ?? string.Empty).ToLowerInvariant();

    /// <summary>At most <paramref name="limit"/> tracks per artist, in order.</summary>
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

    /// <summary>
    /// Keeps an artist from repeating within <paramref name="minGap"/> tracks: each step takes the
    /// earliest track whose artist wasn't among the last few, or the earliest at all when none qualifies.
    /// </summary>
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

    /// <summary>
    /// The gradient pathfinder: starting from the first (strongest) track, each next track is the one
    /// closest to the current one among its sonic neighbours (their rank in its similar list), falling
    /// back to pool order when none of its neighbours is left, so the mix melts from one sound to the next.
    /// </summary>
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
