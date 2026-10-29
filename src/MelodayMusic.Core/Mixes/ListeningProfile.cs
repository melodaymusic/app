using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Mixes;

public sealed record ProfileArtist(string RatingKey, string Title, string? Thumb, double Weight);

public sealed record ProfileTag(string Tag, double Weight);

public static class ListeningProfile
{
    public static double Weight(DateTimeOffset? playedAt, DateTimeOffset now)
    {
        if (playedAt is not { } at) return 0.1;
        var days = Math.Max(0, (now - at).TotalDays);
        return Math.Exp(-days / 7);
    }

    public static bool IsPlaceholderArtist(string? title) =>
        string.IsNullOrWhiteSpace(title)
        || title.Trim().Equals("Various Artists", StringComparison.OrdinalIgnoreCase)
        || title.Trim().Equals("Various", StringComparison.OrdinalIgnoreCase)
        || title.Trim().Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase);

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

    public static IReadOnlyList<ProfileTag> Genres(IEnumerable<Track> recent, DateTimeOffset now) =>
        Rank(recent.SelectMany(t => t.Genres.Select(g => (g, Weight(t.LastViewedAt, now)))));

    public static IReadOnlyList<ProfileTag> Rank(IEnumerable<(string Tag, double Weight)> tags) =>
        tags.Where(t => !string.IsNullOrWhiteSpace(t.Tag))
            .GroupBy(t => t.Tag.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProfileTag(g.First().Tag.Trim(), g.Sum(x => x.Weight)))
            .OrderByDescending(t => t.Weight)
            .ToList();
}
