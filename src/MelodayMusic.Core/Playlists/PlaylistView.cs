using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Playlists;

public static class PlaylistView
{
    public const string Order = "order";

    public static IReadOnlyList<(string Label, string Key)> Sorts { get; } =
    [
        ("Playlist order", Order),
        ("Title", "title"),
        ("Artist", "artist"),
        ("Album", "album"),
        ("Recently Added", "added"),
        ("Most Played", "plays"),
        ("Rating", "rating"),
        ("Duration", "duration"),
    ];

    public static IEnumerable<Track> Sort(IEnumerable<Track> tracks, string key)
    {
        static string Text(string? s) => s ?? string.Empty;
        var ci = StringComparer.CurrentCultureIgnoreCase;
        return key switch
        {
            "title" => tracks.OrderBy(t => Text(t.Title), ci),
            "artist" => tracks.OrderBy(t => t.DisplayArtist, ci).ThenBy(t => Text(t.AlbumTitle), ci).ThenBy(t => t.DiscNumber ?? 0).ThenBy(t => t.TrackNumber ?? 0),
            "album" => tracks.OrderBy(t => Text(t.AlbumTitle), ci).ThenBy(t => t.DiscNumber ?? 0).ThenBy(t => t.TrackNumber ?? 0),
            "added" => tracks.OrderByDescending(t => t.AddedAt ?? DateTimeOffset.MinValue),
            "plays" => tracks.OrderByDescending(t => t.ViewCount).ThenBy(t => Text(t.Title), ci),
            "rating" => tracks.OrderByDescending(t => t.UserRating ?? -1).ThenBy(t => Text(t.Title), ci),
            "duration" => tracks.OrderByDescending(t => t.DurationMs),
            _ => tracks,
        };
    }

    public static bool Matches(Track track, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        var text = string.Join(' ', track.Title, track.DisplayArtist, track.ArtistTitle, track.AlbumTitle);
        return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }
}
