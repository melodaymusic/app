using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Radio;

public sealed record RotationPlay(string Key, string Artist, string Song, DateTimeOffset At, bool Featured = false, bool Canadian = false);

public static class LiveRotation
{
    public static readonly TimeSpan SongRest = TimeSpan.FromHours(4);
    public static readonly TimeSpan ArtistRest = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan BlockRest = TimeSpan.FromMinutes(90);
    public const int SongGap = 60, ArtistGap = 5, BlockGap = 20, Remembered = 2000;

    public static bool IsResting(Track track, IReadOnlyList<RotationPlay> history, DateTimeOffset at)
    {
        var plays = history.Where(p => p.At <= at).ToList();
        var song = plays.FindLastIndex(p => p.Key == track.RatingKey || p.Song == SongOf(track));
        var artist = plays.FindLastIndex(p => p.Artist == ArtistOf(track));
        return song >= 0 && (plays.Count - song - 1 < SongGap || at - plays[song].At < SongRest)
            || artist >= 0 && (plays.Count - artist - 1 < ArtistGap || at - plays[artist].At < ArtistRest);
    }

    public static double CandidatePenalty(Track track, IReadOnlyList<RotationPlay> history, DateTimeOffset at)
    {
        var recent = history.Where(p => p.At <= at && p.At > at.AddDays(-1)).ToList();
        var song = recent.LastOrDefault(p => p.Key == track.RatingKey || p.Song == SongOf(track));
        var artist = recent.Where(p => p.Artist == ArtistOf(track)).ToList();
        return (song is null ? 0 : 3 * (1 - (at - song.At).TotalHours / 24)) + Math.Min(2, artist.Count * 0.35);
    }

    public static string ArtistOf(Track t) =>
        !string.IsNullOrWhiteSpace(t.ArtistRatingKey) ? "id:" + t.ArtistRatingKey : "name:" + t.DisplayArtist.Trim().ToLowerInvariant();
    public static string SongOf(Track t) => ArtistOf(t) + "|" + t.Title.Trim().ToLowerInvariant();

    public static IReadOnlyList<BlockPick> Pick(IEnumerable<BlockPick> candidates, List<RotationPlay> history,
        DateTimeOffset startsAt, int count = 20, Random? random = null, Func<Track, bool>? canadian = null, bool preserveOrder = false)
    {
        random ??= Random.Shared;
        var available = candidates.DistinctBy(p => p.Track.RatingKey).DistinctBy(p => SongOf(p.Track)).ToList();
        var result = new List<BlockPick>();
        var at = startsAt;
        var block = 0;
        while (available.Count > 0 && result.Count < count)
        {
            List<BlockPick>? chosen = null;
            for (var relaxation = 0; relaxation < 4 && chosen is null; relaxation++)
            {
                var eligible = new List<List<BlockPick>>();
                var singleArtist = available.Select(p => ArtistOf(p.Track)).Distinct().Take(2).Count() == 1;
                foreach (var candidate in available)
                {
                    if (!Eligible(candidate.Track, history, at, relaxation, singleArtist)) continue;
                    var members = candidate.Group > 0 ? available.Where(p => p.Group == candidate.Group).ToList() : [];
                    if (members.Count >= ArtistBlocks.MinLength && result.Count + members.Count <= count
                        && CanFeature(candidate.Track, history, at)
                        && members.All(p => Eligible(p.Track, history, at, relaxation, singleArtist)))
                    {
                        if (ReferenceEquals(candidate, members[0])) eligible.Add(members);
                    }
                    else eligible.Add([candidate with { Group = 0 }]);
                }
                if (eligible.Count > 0)
                {
                    if (canadian is not null)
                    {
                        var recent = history.Where(p => p.Canadian && p.At > at.AddHours(-1) && p.At <= at).ToList();
                        var due = recent.Count < 5 && (recent.Count == 0 || at - recent[^1].At >= TimeSpan.FromMinutes(12));
                        var preferred = eligible.Where(g => due ? g.Count == 1 && canadian(g[0].Track) : !g.Any(p => canadian(p.Track))).ToList();
                        if (preferred.Count > 0) eligible = preferred;
                    }
                    chosen = preserveOrder ? eligible[0] : eligible.OrderBy(g => LastSong(g[0].Track, history)?.At ?? DateTimeOffset.MinValue)
                        .ThenBy(_ => random.NextDouble()).First();
                }
            }
            if (chosen is null) break;
            var group = chosen.Count > 1 ? ++block : 0;
            foreach (var pick in chosen)
            {
                result.Add(pick with { Group = group });
                history.Add(new RotationPlay(pick.Track.RatingKey, ArtistOf(pick.Track), SongOf(pick.Track), at, group > 0, canadian?.Invoke(pick.Track) == true));
                at = at.AddMilliseconds(pick.Track.DurationMs > 0 ? pick.Track.DurationMs : LiveSchedule.FallbackDurationMs);
                available.RemoveAll(p => p.Track.RatingKey == pick.Track.RatingKey || SongOf(p.Track) == SongOf(pick.Track));
            }
        }
        if (history.Count > Remembered) history.RemoveRange(0, history.Count - Remembered);
        return result;
    }

    private static RotationPlay? LastSong(Track t, List<RotationPlay> history) =>
        history.LastOrDefault(p => p.Key == t.RatingKey || p.Song == SongOf(t));

    private static bool Eligible(Track t, List<RotationPlay> history, DateTimeOffset at, int level, bool singleArtist)
    {
        var song = history.FindLastIndex(p => p.Key == t.RatingKey || p.Song == SongOf(t));
        if (song == history.Count - 1 && song >= 0) return false;
        if (song >= 0)
        {
            var gap = history.Count - song - 1;
            var rest = at - history[song].At;
            if (level < 3 && (gap < (level < 2 ? SongGap : 10) || rest < (level < 2 ? SongRest : TimeSpan.FromMinutes(30)))) return false;
        }
        var artist = history.FindLastIndex(p => p.Artist == ArtistOf(t));
        if (artist < 0) return true;
        var artistGap = history.Count - artist - 1;
        var artistRest = at - history[artist].At;
        return level switch
        {
            0 => artistGap >= ArtistGap && artistRest >= ArtistRest,
            1 => artistGap >= 2 && artistRest >= TimeSpan.FromMinutes(8),
            _ => artistGap >= 1 || level == 3 && singleArtist,
        };
    }

    private static bool CanFeature(Track t, List<RotationPlay> history, DateTimeOffset at)
    {
        var last = history.FindLastIndex(p => p.Featured);
        if (last >= 0 && (history.Count - last - 1 < BlockGap || at - history[last].At < BlockRest)) return false;
        var artist = history.LastOrDefault(p => p.Featured && p.Artist == ArtistOf(t));
        return artist is null || at - artist.At >= HeadlinerHistory.Rest;
    }
}
