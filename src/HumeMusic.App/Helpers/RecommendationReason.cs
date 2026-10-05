using HumeMusic.Core.Models;

namespace HumeMusic.Helpers;

/// <summary>
/// Short, factual explanations for recommendation surfaces. It only uses evidence carried by the
/// Plex track itself, so the UI never invents a reason the recommendation engine did not observe.
/// </summary>
public static class RecommendationReason
{
    public static string For(Track? track)
    {
        if (track is null) return string.Empty;

        var tag = track.Moods.FirstOrDefault(m => !string.Equals(m, "ArtistPopular", StringComparison.OrdinalIgnoreCase))
                  ?? track.Genres.FirstOrDefault();
        var tagText = tag is { Length: > 0 } ? $" {tag.ToLowerInvariant()}" : string.Empty;

        if (track.ViewCount == 0)
            return $"Why this song? An unplayed{tagText} track from your library.";

        if (track.LastViewedAt is { } last)
        {
            var age = DateTimeOffset.Now - last;
            if (age >= TimeSpan.FromDays(365))
                return $"Why this song? A{tagText} favourite you haven't heard in {Math.Max(1, (int)Math.Round(age.TotalDays / 365.25))} year{Plural(Math.Max(1, (int)Math.Round(age.TotalDays / 365.25)))}.";
            if (age >= TimeSpan.FromDays(60))
            {
                var months = Math.Max(2, (int)Math.Round(age.TotalDays / 30.4));
                return $"Why this song? A{tagText} track you haven't played in about {months} months.";
            }
        }

        if (track.ViewCount <= 2)
            return $"Why this song? A deeper{tagText} cut you've only played {track.ViewCount} time{Plural(track.ViewCount)}.";

        if (track.UserRating is >= 8)
            return $"Why this song? You rated this{tagText} track highly.";

        if (tag is { Length: > 0 })
            return $"Why this song? It matches the {tag} side of your library.";

        return "Why this song? It comes from music already in your library and listening history.";
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
