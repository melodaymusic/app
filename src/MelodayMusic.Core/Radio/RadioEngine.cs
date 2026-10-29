using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Taste;

namespace MelodayMusic.Core.Radio;

public sealed class RadioContext
{
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> SeedArtists { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, double> Affinity { get; init; } = new Dictionary<string, double>();
    public TasteEngine Taste { get; init; } = TasteEngine.PlexOnly;
    public IReadOnlySet<string> Exclude { get; init; } = new HashSet<string>();
    public bool LeanToHits { get; init; }
    public IReadOnlySet<string> Rested { get; init; } = new HashSet<string>();
}

public static class RadioEngine
{
    public static double Score(Track t, RadioContext c, double jitter = 0)
    {
        var score = 0.0;

        score += c.Taste.Like(t) + 0.3 * c.Taste.Fit(t, c.Now) + 0.3 * c.Taste.Adventure * TasteEngine.Novelty(t, c.Now);

        score -= Freshness.Penalty(t.LastViewedAt, c.Now);
        if (t.ArtistRatingKey is { } featured && c.Rested.Contains(featured)) score -= 0.7;

        if (c.LeanToHits && t.RatingCount is { } known and > 0) score += Math.Min(0.9, Math.Log10(1 + known) / 7);

        if (t.ArtistRatingKey is { } artist)
        {
            if (c.SeedArtists.Contains(artist)) score += 0.5;
            else if (c.Affinity.TryGetValue(artist, out var near)) score += 0.8 * Math.Clamp(near, 0, 1);
        }
        if (c.Tags.Count > 0) score += 0.6 * Overlap(t, c.Tags);

        return score + jitter;
    }

    public static double Overlap(Track t, IReadOnlySet<string> tags)
    {
        var own = t.Genres.Concat(t.Moods).Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToHashSet();
        if (own.Count == 0) return 0;
        var shared = own.Count(tags.Contains);
        return shared / (double)Math.Min(own.Count, Math.Max(1, tags.Count));
    }

    public static IReadOnlyList<Track> Pick(IEnumerable<Track> pool, RadioContext context, int count, Random? random = null,
        int artistGap = 4, int maxPerArtist = 2)
    {
        random ??= Random.Shared;
        var ranked = pool
            .Where(t => !context.Exclude.Contains(t.RatingKey))
            .DistinctBy(t => t.RatingKey)
            .DistinctBy(t => (t.Title.Trim().ToLowerInvariant(), t.ArtistRatingKey))
            .Select(t => (Track: t, Score: Score(t, context, random.NextDouble() * 0.8)))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Track)
            .ToList();

        var picked = new List<Track>();
        var perArtist = new Dictionary<string, int>();
        string ArtistOf(Track t) => t.ArtistRatingKey ?? t.RatingKey;

        bool Take(Track t, bool strict)
        {
            var artist = ArtistOf(t);
            if (perArtist.GetValueOrDefault(artist) >= maxPerArtist) return false;
            if (strict && picked.Skip(Math.Max(0, picked.Count - artistGap)).Any(p => ArtistOf(p) == artist)) return false;
            picked.Add(t);
            perArtist[artist] = perArtist.GetValueOrDefault(artist) + 1;
            return true;
        }

        var left = new List<Track>();
        foreach (var t in ranked)
        {
            if (picked.Count >= count) break;
            if (!Take(t, strict: true)) left.Add(t);
        }
        foreach (var t in left)
        {
            if (picked.Count >= count) break;
            Take(t, strict: false);
        }
        return picked;
    }
}
