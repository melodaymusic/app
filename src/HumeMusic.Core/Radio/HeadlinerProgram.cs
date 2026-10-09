using HumeMusic.Core.Mixes;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Radio;

/// <summary>What Headliners programmes from: the songs of the artists the listener plays, the songs released lately, and the songs trending (Last.fm's chart, where the library has them).</summary>
public sealed record HeadlinerPools(IReadOnlyList<Track> Played, IReadOnlyList<Track> Fresh, IReadOnlyList<Track> Trending);

/// <summary>
/// Headliners' programming, after how a flagship radio show is built: not artist after artist, but a mix of kinds of
/// moment. An <b>artist block</b> (three songs of one artist, the biggest first, led by the artist Now Playing features),
/// a <b>new run</b> (songs released lately and songs trending, each by a different artist), and a <b>genre block</b> (a
/// few songs of one genre by different artists). Artists featured lately are rested, so the line-up moves on from one
/// visit to the next, and newer and trending music is given the lead over what was merely played a lot.
/// </summary>
public static class HeadlinerProgram
{
    /// <summary>How much a recent release, and a trending song, count in its favour (a heavily played song scores about 1 to 2).</summary>
    public const double FreshBonus = 0.8, TrendingBonus = 1.1;
    /// <summary>Songs in a new run, and in a genre block.</summary>
    public const int NewRunLength = 3, GenreLength = 4;

    private enum Segment { Artist, New, Genre }

    // The order the segments come in, turn by turn: two artist blocks, a new run (or two) and a genre block each time.
    private static readonly Segment[][] Rounds =
    [
        [Segment.Artist, Segment.New, Segment.Artist, Segment.Genre],
        [Segment.New, Segment.Artist, Segment.Genre, Segment.Artist, Segment.New],
        [Segment.Genre, Segment.Artist, Segment.New, Segment.Artist],
    ];

    /// <summary>
    /// The next stretch of the station: songs with the artist block each belongs to (1, 2, 3...; 0 for a new run or genre block,
    /// which feature no artist). <paramref name="round"/> says which turn of the programme this is.
    /// </summary>
    public static IReadOnlyList<BlockPick> Build(
        HeadlinerPools pools, int round, Random random,
        Func<Track, double>? skip = null,
        IReadOnlySet<string>? excludeTracks = null,
        IReadOnlySet<string>? restedArtists = null,
        DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var fresh = pools.Fresh.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        var trending = pools.Trending.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        double Score(Track t) =>
            ArtistBlocks.HitScore(t) + (trending.Contains(t.RatingKey) ? TrendingBonus : 0) + (fresh.Contains(t.RatingKey) ? FreshBonus : 0) - 1.2 * (skip?.Invoke(t) ?? 0)
            // A song played lately waits (see Freshness), so the line-up moves on from what the listener has just heard.
            - Freshness.Penalty(t.LastViewedAt, at);

        var all = pools.Played.Concat(pools.Fresh).Concat(pools.Trending)
            .Where(t => t.ArtistRatingKey is not null && excludeTracks?.Contains(t.RatingKey) != true && !ListeningProfile.IsPlaceholderArtist(t.DisplayArtist))
            .DistinctBy(t => t.RatingKey)
            // The same song twice (an album and its deluxe edition) is one song.
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
                    picks.AddRange(NewRun(all, fresh, trending, used, artists, random).Select(t => new BlockPick(t, 0)));
                    break;
                default:
                    picks.AddRange(GenreRun(all, used, artists, random).Select(t => new BlockPick(t, 0)));
                    break;
            }
        }
        return picks;
    }

    /// <summary>One artist's three biggest songs: the artist chosen from among the best (so the same few don't come every time), by chance weighted by their score.</summary>
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

    /// <summary>Songs released lately and songs trending, each by a different artist.</summary>
    private static List<Track> NewRun(List<(Track Track, double Score)> all, HashSet<string> fresh, HashSet<string> trending, HashSet<string> used, HashSet<string> artists, Random random)
    {
        var pool = all.Where(x => (fresh.Contains(x.Track.RatingKey) || trending.Contains(x.Track.RatingKey)) && !used.Contains(x.Track.RatingKey)).ToList();
        return Distinct(pool, used, artists, NewRunLength, random);
    }

    /// <summary>A few songs of one genre by different artists, the genre chosen by chance weighted by how much of it there is.</summary>
    private static List<Track> GenreRun(List<(Track Track, double Score)> all, HashSet<string> used, HashSet<string> artists, Random random)
    {
        var free = all.Where(x => !used.Contains(x.Track.RatingKey)).ToList();
        var genres = free
            .SelectMany(x => x.Track.Genres.Select(g => (Genre: g.Trim(), x)))
            .Where(p => p.Genre.Length > 0)
            .GroupBy(p => p.Genre, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(p => p.x.Track.ArtistRatingKey).Distinct().Count() >= 3)
            .Select(g => (Genre: g.Key, Songs: g.Select(p => p.x).ToList()))
            .ToList();
        if (genres.Count == 0) return [];
        var chosen = WeightedPick(genres, g => Math.Sqrt(g.Songs.Count), random);
        return Distinct(chosen.Songs, used, artists, GenreLength, random);
    }

    /// <summary>Up to <paramref name="count"/> songs from the pool, best scores first but with a shake of chance, no artist twice and none of an artist already used in this stretch.</summary>
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

    /// <summary>One of the items by chance, the likelier the higher its weight (weights are lifted so the lowest still has a chance).</summary>
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
