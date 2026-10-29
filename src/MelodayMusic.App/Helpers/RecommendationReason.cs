using MelodayMusic.Core.Models;

namespace MelodayMusic.Helpers;

public static class RecommendationReason
{
    public static string For(Track? track)
    {
        if (track is null) return string.Empty;

        var tag = track.Moods.FirstOrDefault(m => !string.Equals(m, "ArtistPopular", StringComparison.OrdinalIgnoreCase))
                  ?? track.Genres.FirstOrDefault();
        var tagged = tag is { Length: > 0 } ? $" Plex tags it as {tag}." : string.Empty;

        if (track.ViewCount == 0)
            return "Why this song? You haven't played it yet." + tagged;

        if (track.LastViewedAt is { } last)
        {
            var age = DateTimeOffset.Now - last;
            if (age >= TimeSpan.FromDays(365))
            {
                var years = Math.Max(1, (int)Math.Round(age.TotalDays / 365.25));
                return $"Why this song? You haven't played it in about {years} year{Plural(years)}." + tagged;
            }
            if (age >= TimeSpan.FromDays(60))
            {
                var months = Math.Max(2, (int)Math.Round(age.TotalDays / 30.4));
                return $"Why this song? You haven't played it in about {months} months." + tagged;
            }
        }

        if (track.ViewCount <= 2)
            return $"Why this song? You've only played it {track.ViewCount} time{Plural(track.ViewCount)}." + tagged;

        if (track.UserRating is >= 8)
            return "Why this song? You rated it highly." + tagged;

        if (tag is { Length: > 0 })
            return $"Why this song? Plex tags it as {tag}.";

        return "Why this song? It is part of your Plex library and listening history.";
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
