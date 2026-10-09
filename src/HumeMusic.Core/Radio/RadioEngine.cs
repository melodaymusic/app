using HumeMusic.Core.Mixes;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Radio;

/// <summary>What a radio is built around, and what it knows about the listener.</summary>
public sealed class RadioContext
{
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Genres, moods and styles the radio sounds like (lower case), when it has a seed.</summary>
    public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>();
    /// <summary>The seed's own artists: their music leads.</summary>
    public IReadOnlySet<string> SeedArtists { get; init; } = new HashSet<string>();
    /// <summary>Related artists and how close each is (0-1).</summary>
    public IReadOnlyDictionary<string, double> Affinity { get; init; } = new Dictionary<string, double>();
    /// <summary>How much the listener tends to skip a song (0-1), from <see cref="SkipStore"/>.</summary>
    public Func<Track, double>? Skip { get; init; }
    /// <summary>Songs already queued or just played, never picked again.</summary>
    public IReadOnlySet<string> Exclude { get; init; } = new HashSet<string>();
    /// <summary>Lean to the songs most widely known (a station of hits).</summary>
    public bool LeanToHits { get; init; }
    /// <summary>Artists this station has featured lately (kept between runs of the app): their songs wait.</summary>
    public IReadOnlySet<string> Rested { get; init; } = new HashSet<string>();
}

/// <summary>
/// Meloday's own radio, for when Plex has none to offer (without Plex Pass, or before Plex has
/// analysed the library): from songs the library holds it picks what the listener will likely want:
/// songs they play and rate well, from artists near the seed, sharing its genres and moods, not played
/// lately and not skipped. Then it orders them so one artist doesn't crowd the rest.
/// </summary>
public static class RadioEngine
{
    public static double Score(Track t, RadioContext c, double jitter = 0)
    {
        var score = 0.0;

        // What the listener already likes: rated well, played often.
        if (t.UserRating is { } rating) score += rating >= 8 ? 0.6 : rating >= 6 ? 0.3 : 0;
        score += Math.Min(0.5, Math.Log2(1 + t.ViewCount) / 8);
        // Something new now and then keeps a radio from becoming a loop of favourites.
        if (t.ViewCount == 0) score += 0.1;

        // Not again too soon (see Freshness), nor the artists this station has just featured.
        score -= Freshness.Penalty(t.LastViewedAt, c.Now);
        if (t.ArtistRatingKey is { } featured && c.Rested.Contains(featured)) score -= 0.7;

        // Skipped: here, and in Plex's own count.
        score -= 1.2 * (c.Skip?.Invoke(t) ?? 0);
        var events = t.SkipCount + t.ViewCount;
        if (t.SkipCount >= 2 && events > 0) score -= 0.5 * t.SkipCount / events;

        // Known far and wide: a station of hits plays the songs everyone knows.
        if (c.LeanToHits && t.RatingCount is { } known and > 0) score += Math.Min(0.9, Math.Log10(1 + known) / 7);

        // Near the seed.
        if (t.ArtistRatingKey is { } artist)
        {
            if (c.SeedArtists.Contains(artist)) score += 0.5;
            else if (c.Affinity.TryGetValue(artist, out var near)) score += 0.8 * Math.Clamp(near, 0, 1);
        }
        if (c.Tags.Count > 0) score += 0.6 * Overlap(t, c.Tags);

        return score + jitter;
    }

    /// <summary>How much of the song's own genres and moods the seed shares (0-1).</summary>
    public static double Overlap(Track t, IReadOnlySet<string> tags)
    {
        var own = t.Genres.Concat(t.Moods).Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToHashSet();
        if (own.Count == 0) return 0;
        var shared = own.Count(tags.Contains);
        return shared / (double)Math.Min(own.Count, Math.Max(1, tags.Count));
    }

    /// <summary>
    /// The best <paramref name="count"/> songs of the pool, in an order that spreads artists out (never
    /// the same artist within <paramref name="artistGap"/> songs, no more than <paramref name="maxPerArtist"/>
    /// in a batch).
    /// </summary>
    public static IReadOnlyList<Track> Pick(IEnumerable<Track> pool, RadioContext context, int count, Random? random = null,
        int artistGap = 4, int maxPerArtist = 2)
    {
        random ??= Random.Shared;
        var ranked = pool
            .Where(t => !context.Exclude.Contains(t.RatingKey))
            .DistinctBy(t => t.RatingKey)
            // The same song twice (an album and its deluxe edition) is one song.
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

        // Strictly spaced first; where the pool is too narrow for that (a small artist list), relax the gap.
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
