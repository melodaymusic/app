using HumeMusic.Core.Mixes;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Radio;

/// <summary>A song in an artist's block: which block of its batch it belongs to (1, 2, 3...).</summary>
public sealed record BlockPick(Track Track, int Group);

/// <summary>
/// Headliners' way of programming: the station plays an artist at a time. A block is a few of one
/// artist's biggest songs back to back, led by the artist the Now Playing screen features, then the
/// next artist. Biggest means what the listener plays and rates most, helped by how widely the song is known.
/// </summary>
public static class ArtistBlocks
{
    /// <summary>Songs in a block.</summary>
    public const int Length = 3;
    /// <summary>An artist with fewer songs than this has no block.</summary>
    public const int MinLength = 2;

    /// <summary>How big a hit a song is for this listener (higher is bigger): plays, rating and how widely it is known, less what they skip.</summary>
    public static double HitScore(Track t)
    {
        var score = Math.Min(1.0, Math.Log2(1 + t.ViewCount) / 5);
        if (t.UserRating is { } rating) score += rating >= 8 ? 0.6 : rating >= 6 ? 0.3 : 0;
        if (t.RatingCount is { } known and > 0) score += Math.Min(0.8, Math.Log10(1 + known) / 8);
        var events = t.SkipCount + t.ViewCount;
        if (t.SkipCount >= 2 && events > 0) score -= 0.8 * t.SkipCount / events;
        return score;
    }

    /// <summary>
    /// Up to <paramref name="blocks"/> blocks from the pool, in a shuffled order: each of
    /// <paramref name="length"/> songs by one artist, the biggest first. An artist with too few songs, one in
    /// <paramref name="excludeArtists"/> (featured lately) and a song in <paramref name="excludeTracks"/> are left out.
    /// </summary>
    public static IReadOnlyList<BlockPick> Build(
        IEnumerable<Track> pool, int blocks, Random random,
        Func<Track, double>? skip = null,
        IReadOnlySet<string>? excludeTracks = null,
        IReadOnlySet<string>? excludeArtists = null,
        int length = Length)
    {
        var scored = pool
            .Where(t => t.ArtistRatingKey is not null && excludeTracks?.Contains(t.RatingKey) != true && !ListeningProfile.IsPlaceholderArtist(t.DisplayArtist))
            .DistinctBy(t => t.RatingKey)
            // The same song twice (an album and its deluxe edition) is one song.
            .DistinctBy(t => (t.Title.Trim().ToLowerInvariant(), t.ArtistRatingKey))
            .Select(t => (Track: t, Score: HitScore(t) - 1.2 * (skip?.Invoke(t) ?? 0)))
            .ToList();

        var chosen = scored
            .GroupBy(x => x.Track.ArtistRatingKey!)
            .Where(g => g.Count() >= MinLength && excludeArtists?.Contains(g.Key) != true)
            .Select(g =>
            {
                var top = g.OrderByDescending(x => x.Score + random.NextDouble() * 0.3).Take(length).OrderByDescending(x => x.Score).ToList();
                return (Top: top, Score: top.Average(x => x.Score) + random.NextDouble() * 0.5);
            })
            .OrderByDescending(a => a.Score)
            .Take(blocks)
            .OrderBy(_ => random.Next())
            .ToList();

        var picks = new List<BlockPick>();
        for (var i = 0; i < chosen.Count; i++)
            picks.AddRange(chosen[i].Top.Select(x => new BlockPick(x.Track, i + 1)));
        return picks;
    }
}
