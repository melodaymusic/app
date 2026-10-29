using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Taste;

namespace MelodayMusic.Core.Radio;

public sealed record BlockPick(Track Track, int Group, string? Programme = null);

public static class ArtistBlocks
{
    public const int Length = 3;
    public const int MinLength = 2;

    public static double HitScore(Track t, TasteEngine? taste = null)
    {
        var score = Math.Min(0.7, Math.Log2(1 + t.ViewCount) / 7);
        if (t.RatingCount is { } known and > 0) score += Math.Min(0.8, Math.Log10(1 + known) / 8);
        return score + (taste ?? TasteEngine.PlexOnly).Like(t);
    }

    public static IReadOnlyList<BlockPick> Build(
        IEnumerable<Track> pool, int blocks, Random random,
        TasteEngine? taste = null,
        IReadOnlySet<string>? excludeTracks = null,
        IReadOnlySet<string>? excludeArtists = null,
        int length = Length)
    {
        var scored = pool
            .Where(t => t.ArtistRatingKey is not null && excludeTracks?.Contains(t.RatingKey) != true && !ListeningProfile.IsPlaceholderArtist(t.DisplayArtist))
            .DistinctBy(t => t.RatingKey)
            .DistinctBy(t => (t.Title.Trim().ToLowerInvariant(), t.ArtistRatingKey))
            .Select(t => (Track: t, Score: HitScore(t, taste)))
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
