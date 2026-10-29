using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Radio;

public sealed record HeadlinerPools(IReadOnlyList<Track> Played, IReadOnlyList<Track> Fresh, IReadOnlyList<Track> Trending);

public static class HeadlinerProgram
{
    public const double FreshBonus = 0.8, TrendingBonus = 1.1;
    public const int NewRunLength = 3, GenreLength = 4;

    private enum Segment { Artist, New, Genre, Rotation }

    private static readonly Segment[][] Rounds =
    [
        [Segment.New, Segment.Genre, Segment.Rotation, Segment.Genre],
        [Segment.Genre, Segment.Rotation, Segment.New, Segment.Rotation],
        [Segment.Genre, Segment.Rotation, Segment.Artist, Segment.New, Segment.Rotation],
    ];

    public static IReadOnlyList<BlockPick> Build(
        HeadlinerPools pools, int round, Random random,
        Taste.TasteEngine? taste = null,
        IReadOnlySet<string>? excludeTracks = null,
        IReadOnlySet<string>? restedArtists = null,
        DateTimeOffset? now = null, Func<Track, double>? rotationPenalty = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var fresh = pools.Fresh.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        var trending = pools.Trending.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        double Score(Track t) =>
            ArtistBlocks.HitScore(t, taste) + (trending.Contains(t.RatingKey) ? TrendingBonus : 0) + (fresh.Contains(t.RatingKey) ? FreshBonus : 0)
            - Freshness.Penalty(t.LastViewedAt, at) - (rotationPenalty?.Invoke(t) ?? 0);

        var all = pools.Played.Concat(pools.Fresh).Concat(pools.Trending)
            .Where(t => t.ArtistRatingKey is not null && excludeTracks?.Contains(t.RatingKey) != true && !ListeningProfile.IsPlaceholderArtist(t.DisplayArtist))
            .DistinctBy(t => t.RatingKey)
            .DistinctBy(t => (t.Title.Trim().ToLowerInvariant(), t.ArtistRatingKey))
            .Select(t => (Track: t, Score: Score(t)))
            .ToList();

        var used = new HashSet<string>(StringComparer.Ordinal);
        var artists = new HashSet<string>(StringComparer.Ordinal);
        var picks = new List<BlockPick>();
        var block = 0;

        foreach (var segment in Rounds[round % Rounds.Length])
        {
            switch (segment)
            {
                case Segment.Artist:
                    if (ArtistBlock(all, used, artists, restedArtists, random) is { } songs)
                    {
                        block++;
                        picks.AddRange(songs.Select(t => new BlockPick(t, block)));
                    }
                    break;
                case Segment.New:
                    var newRun = NewRun(all, fresh, trending, used, artists, random);
                    var hasNewMusic = newRun.Count > 0;
                    if (!hasNewMusic) newRun = Distinct(all.Where(x => !used.Contains(x.Track.RatingKey)).ToList(), used, artists, NewRunLength, random);
                    picks.AddRange(newRun.Select(t => new BlockPick(t, 0, hasNewMusic ? "New Music" : null)));
                    break;
                case Segment.Rotation:
                    picks.AddRange(Distinct(all.Where(x => !used.Contains(x.Track.RatingKey)).ToList(), used, artists, GenreLength, random).Select(t => new BlockPick(t, 0)));
                    break;
                default:
                    var (genre, genreRun) = GenreRun(all, used, artists, random);
                    picks.AddRange(genreRun.Select(t => new BlockPick(t, 0, genre + " Takeover")));
                    break;
            }
        }
        return picks;
    }

    private static List<Track>? ArtistBlock(List<(Track Track, double Score)> all, HashSet<string> used, HashSet<string> artists, IReadOnlySet<string>? rested, Random random)
    {
        var candidates = all
            .Where(x => !used.Contains(x.Track.RatingKey) && !artists.Contains(x.Track.ArtistRatingKey!) && rested?.Contains(x.Track.ArtistRatingKey!) != true)
            .GroupBy(x => x.Track.ArtistRatingKey!)
            .Where(g => g.Count() >= ArtistBlocks.MinLength)
            .Select(g =>
            {
                var top = g.OrderByDescending(x => x.Score + random.NextDouble() * 0.3).Take(ArtistBlocks.Length).OrderByDescending(x => x.Score).ToList();
                return (Artist: g.Key, Top: top, Score: top.Average(x => x.Score));
            })
            .OrderByDescending(a => a.Score)
            .Take(12)
            .ToList();
        if (candidates.Count == 0) return null;
        var chosen = WeightedPick(candidates, a => a.Score, random);
        artists.Add(chosen.Artist);
        foreach (var x in chosen.Top) used.Add(x.Track.RatingKey);
        return chosen.Top.Select(x => x.Track).ToList();
    }

    private static List<Track> NewRun(List<(Track Track, double Score)> all, HashSet<string> fresh, HashSet<string> trending, HashSet<string> used, HashSet<string> artists, Random random)
    {
        var pool = all.Where(x => (fresh.Contains(x.Track.RatingKey) || trending.Contains(x.Track.RatingKey)) && !used.Contains(x.Track.RatingKey)).ToList();
        return Distinct(pool, used, artists, NewRunLength, random);
    }

    private static (string? Genre, List<Track> Tracks) GenreRun(List<(Track Track, double Score)> all, HashSet<string> used, HashSet<string> artists, Random random)
    {
        var free = all.Where(x => !used.Contains(x.Track.RatingKey)).ToList();
        var genres = free
            .SelectMany(x => x.Track.Genres.Select(g => (Genre: g.Trim(), x)))
            .Where(p => p.Genre.Length > 0)
            .GroupBy(p => p.Genre, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(p => p.x.Track.ArtistRatingKey).Distinct().Count() >= 3)
            .Select(g => (Genre: g.Key, Songs: g.Select(p => p.x).ToList()))
            .ToList();
        if (genres.Count == 0) return (null, []);
        var chosen = WeightedPick(genres, g => Math.Sqrt(g.Songs.Count), random);
        return (chosen.Genre, Distinct(chosen.Songs, used, artists, GenreLength, random));
    }

    private static List<Track> Distinct(List<(Track Track, double Score)> pool, HashSet<string> used, HashSet<string> artists, int count, Random random)
    {
        var picked = new List<Track>();
        var here = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in pool.OrderByDescending(x => x.Score + random.NextDouble() * 0.8))
        {
            var artist = x.Track.ArtistRatingKey!;
            if (artists.Contains(artist) || here.Contains(artist)) continue;
            picked.Add(x.Track);
            here.Add(artist);
            used.Add(x.Track.RatingKey);
            if (picked.Count == count) break;
        }
        foreach (var a in here) artists.Add(a);
        return picked;
    }

    private static T WeightedPick<T>(IReadOnlyList<T> items, Func<T, double> weight, Random random)
    {
        var weights = items.Select(weight).ToList();
        var floor = weights.Min();
        var shifted = weights.Select(w => w - floor + 0.4).ToList();
        var at = random.NextDouble() * shifted.Sum();
        for (var i = 0; i < items.Count; i++)
        {
            at -= shifted[i];
            if (at <= 0) return items[i];
        }
        return items[^1];
    }
}
