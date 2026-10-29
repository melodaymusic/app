using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Playback;

public enum Curator
{
    None,
    DoubleUp,
    Twin,
    Drift,
    TimeMachine,
    Smooth,
    Spread,
    Bridge,
}

public static class Curators
{
    public static IReadOnlyList<Curator> All { get; } =
        [Curator.None, Curator.DoubleUp, Curator.Twin, Curator.Drift, Curator.TimeMachine, Curator.Smooth, Curator.Spread, Curator.Bridge];

    public static string Name(Curator curator) => curator switch
    {
        Curator.DoubleUp => "Double Up",
        Curator.Twin => "Twin",
        Curator.Drift => "Drift",
        Curator.TimeMachine => "Time Machine",
        Curator.Smooth => "Smooth",
        Curator.Spread => "Spread",
        Curator.Bridge => "Bridge",
        _ => "Just as queued",
    };

    public static string Description(Curator curator) => curator switch
    {
        Curator.DoubleUp => "Two songs from each artist, back to back.",
        Curator.Twin => "Stays close to the sound of what's playing.",
        Curator.Drift => "Starts close, then slowly wanders somewhere new.",
        Curator.TimeMachine => "Walks through the years, oldest to newest.",
        Curator.Smooth => "Each song flows into the next: tempo, key and energy (AutoMix's favourite).",
        Curator.Spread => "Never the same artist twice in a row.",
        Curator.Bridge => "Between each two songs, five that lead from one to the next, like a sonic adventure.",
        _ => "Plays in the order it was queued.",
    };

    public static bool NeedsAudio(Curator curator) => curator == Curator.Smooth;

    public static double Distance(Track a, Track b)
    {
        var sim = 0.45 * Overlap(a.Genres, b.Genres)
                  + 0.20 * Overlap(a.Moods, b.Moods)
                  + 0.25 * YearCloseness(a.Year, b.Year)
                  + 0.10 * (SameArtist(a, b) ? 1 : 0);
        return 1 - Math.Clamp(sim, 0, 1);
    }

    private static double Overlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.4;
        var x = a.Select(t => t.Trim().ToLowerInvariant()).ToHashSet();
        var y = b.Select(t => t.Trim().ToLowerInvariant()).ToHashSet();
        var shared = x.Count(y.Contains);
        return shared / (double)(x.Count + y.Count - shared);
    }

    private static double YearCloseness(int? a, int? b) =>
        a is > 1900 && b is > 1900 ? 1 - Math.Min(Math.Abs(a.Value - b.Value), 30) / 30.0 : 0.5;

    private static string ArtistOf(Track t) => t.ArtistRatingKey ?? t.DisplayArtist;

    private static bool SameArtist(Track a, Track b) => string.Equals(ArtistOf(a), ArtistOf(b), StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Track> Order(Curator curator, Track? seed, IReadOnlyList<Track> pool, Func<Track, Track, double>? distance = null) => curator switch
    {
        Curator.DoubleUp => DoubleUp(pool),
        Curator.Twin => Chain(seed, pool, towardSeed: 0.5, awayFromSeed: 0, distance ?? Distance),
        Curator.Drift => Chain(seed, pool, towardSeed: 0, awayFromSeed: 0.5, distance ?? Distance),
        Curator.TimeMachine => TimeMachine(pool),
        Curator.Spread => Spread(seed, pool),
        _ => pool,
    };

    private static IReadOnlyList<Track> DoubleUp(IReadOnlyList<Track> pool)
    {
        var byArtist = new List<(string Artist, Queue<Track> Songs)>();
        foreach (var t in pool)
        {
            var artist = ArtistOf(t);
            var entry = byArtist.FindIndex(e => string.Equals(e.Artist, artist, StringComparison.OrdinalIgnoreCase));
            if (entry < 0) byArtist.Add((artist, new Queue<Track>([t])));
            else byArtist[entry].Songs.Enqueue(t);
        }
        var result = new List<Track>(pool.Count);
        while (result.Count < pool.Count)
            foreach (var (_, songs) in byArtist)
                for (var i = 0; i < 2 && songs.Count > 0; i++) result.Add(songs.Dequeue());
        return result;
    }

    private static IReadOnlyList<Track> Chain(Track? seed, IReadOnlyList<Track> pool, double towardSeed, double awayFromSeed, Func<Track, Track, double> distance)
    {
        if (pool.Count < 2) return pool;
        var left = pool.ToList();
        var origin = seed ?? left[0];
        var previous = origin;
        var result = new List<Track>(pool.Count);
        while (left.Count > 0)
        {
            var best = left
                .Select((t, i) => (Track: t, Index: i, Score: (1 - towardSeed) * distance(previous, t) + towardSeed * distance(origin, t)
                    - awayFromSeed * (distance(origin, t) - distance(origin, previous))))
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Index)
                .First();
            result.Add(best.Track);
            previous = best.Track;
            left.RemoveAt(best.Index);
        }
        return result;
    }

    private static IReadOnlyList<Track> TimeMachine(IReadOnlyList<Track> pool) =>
        pool.Select((t, i) => (Track: t, Index: i))
            .OrderBy(x => x.Track.Year is > 1900 ? x.Track.Year!.Value : int.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Track)
            .ToList();

    private static IReadOnlyList<Track> Spread(Track? seed, IReadOnlyList<Track> pool)
    {
        var left = pool.ToList();
        var recent = new List<string>();
        if (seed is not null) recent.Add(ArtistOf(seed));
        var result = new List<Track>(pool.Count);
        while (left.Count > 0)
        {
            var next = left.FindIndex(t => !recent.Contains(ArtistOf(t), StringComparer.OrdinalIgnoreCase));
            if (next < 0) next = left.FindIndex(t => recent.Count == 0 || !string.Equals(ArtistOf(t), recent[^1], StringComparison.OrdinalIgnoreCase));
            if (next < 0) next = 0;
            var track = left[next];
            left.RemoveAt(next);
            result.Add(track);
            recent.Add(ArtistOf(track));
            if (recent.Count > 2) recent.RemoveAt(0);
        }
        return result;
    }
}
