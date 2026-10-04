using HumeMusic.Core.Models;

namespace HumeMusic.Core.Mixes;

/// <summary>An artist in the listener's recent history, with its weight (recent plays count most).</summary>
public sealed record ProfileArtist(string RatingKey, string Title, string? Thumb, double Weight);

/// <summary>A genre or mood in the listener's recent history, with its weight.</summary>
public sealed record ProfileTag(string Tag, double Weight);

/// <summary>
/// What the listener has been playing lately, for mixes that follow their habits: artists and genres
/// from recently played tracks, each play weighted by how recent it is (a play a week ago counts
/// about a third as much as one today), so the mixes shift as listening shifts.
/// </summary>
public static class ListeningProfile
{
    /// <summary>Recency weight of a play: halves roughly every five days.</summary>
    public static double Weight(DateTimeOffset? playedAt, DateTimeOffset now)
    {
        if (playedAt is not { } at) return 0.1;
        var days = Math.Max(0, (now - at).TotalDays);
        return Math.Exp(-days / 7);
    }

    /// <summary>"Various Artists" and friends are compilation placeholders, not artists to mix.</summary>
    public static bool IsPlaceholderArtist(string? title) =>
        string.IsNullOrWhiteSpace(title)
        || title.Trim().Equals("Various Artists", StringComparison.OrdinalIgnoreCase)
        || title.Trim().Equals("Various", StringComparison.OrdinalIgnoreCase)
        || title.Trim().Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase);

    /// <summary>Artists from recently played tracks, strongest first; placeholders and excluded artists left out.</summary>
    public static IReadOnlyList<ProfileArtist> Artists(IEnumerable<Track> recent, DateTimeOffset now, Func<string, bool>? excluded = null)
    {
        return recent
            .Where(t => t.ArtistRatingKey is not null && !IsPlaceholderArtist(t.ArtistTitle) && excluded?.Invoke(t.ArtistRatingKey!) != true)
            .GroupBy(t => t.ArtistRatingKey!)
            .Select(g => new ProfileArtist(g.Key, g.First().ArtistTitle!, g.Select(t => t.ArtistThumb).FirstOrDefault(p => p is not null),
                g.Sum(t => Weight(t.LastViewedAt, now))))
            .OrderByDescending(a => a.Weight)
            .ToList();
    }

    /// <summary>Genres of recently played tracks, strongest first (case-insensitive; Plex's own casing kept).</summary>
    public static IReadOnlyList<ProfileTag> Genres(IEnumerable<Track> recent, DateTimeOffset now) =>
        Rank(recent.SelectMany(t => t.Genres.Select(g => (g, Weight(t.LastViewedAt, now)))));

    /// <summary>Ranks weighted tags (e.g. the moods of recently played artists, weighted by those artists).</summary>
    public static IReadOnlyList<ProfileTag> Rank(IEnumerable<(string Tag, double Weight)> tags) =>
        tags.Where(t => !string.IsNullOrWhiteSpace(t.Tag))
            .GroupBy(t => t.Tag.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProfileTag(g.First().Tag.Trim(), g.Sum(x => x.Weight)))
            .OrderByDescending(t => t.Weight)
            .ToList();
}
