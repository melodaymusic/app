using HumeMusic.Core.Models;
using Microsoft.UI.Xaml;

namespace HumeMusic.Helpers;

/// <summary>Formatting helpers usable from x:Bind function bindings.</summary>
public static class Format
{
    /// <summary>A station's name as its cover sets it: without the "Radio" that the cover's label says ("Library Radio" is "Library").</summary>
    public static string StationCoverName(Core.Models.Station station) =>
        station.Title.EndsWith(" Radio", StringComparison.Ordinal) ? station.Title[..^6] : station.Title;

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    public static string DurationMs(long ms) => Duration(TimeSpan.FromMilliseconds(ms));

    /// <summary>A size on disk ("840 KB", "12.4 MB", "3.1 GB").</summary>
    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB",
    };

    public static string LongDuration(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return $"{Math.Max(0, (int)t.TotalSeconds)} sec";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} min";
        return $"{(int)t.TotalHours} hr {t.Minutes} min";
    }

    public static string TrackNumber(int? n) => n is > 0 ? n.Value.ToString() : "–";

    public static string Plays(int count) => count switch
    {
        0 => string.Empty,
        1 => "1 play",
        _ => $"{count:N0} plays",
    };

    public static string Count(int? count, string singular, string plural) =>
        count is null ? string.Empty : count == 1 ? $"1 {singular}" : $"{count:N0} {plural}";

    /// <summary>Genres, styles and decades are counted in albums; moods are tagged on tracks.</summary>
    public static string TagCount(int? count, TagKind kind) =>
        kind == TagKind.Mood ? Count(count, "track", "tracks") : Count(count, "album", "albums");

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfText(string? s) => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfNotNull(object? o) => o is null ? Visibility.Collapsed : Visibility.Visible;
    public static string Upper(string? s) => s?.ToUpperInvariant() ?? string.Empty;
    public static bool Not(bool value) => !value;

    /// <summary>When something last happened, as people say it: "Today", "Yesterday", "3 days ago", "12 March 2024".</summary>
    public static string Ago(DateTimeOffset when)
    {
        var days = (DateTime.Now.Date - when.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => $"{days} days ago",
            < 14 => "Last week",
            < 31 => $"{days / 7} weeks ago",
            _ => when.ToLocalTime().ToString(when.Year == DateTime.Now.Year ? "d MMMM" : "d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture),
        };
    }

    // Sidebar pinned items: artists round, everything else a small rounded square.
    public static CornerRadius NavArtRadius(MediaItem item) => (item is PinnedCover c ? c.Target : item) is Artist ? new CornerRadius(999) : new CornerRadius(4);
    public static string? NavThumb(MediaItem item) => item switch { Playlist p => p.DisplayThumb, Track t => t.DisplayThumb, _ => item.Thumb };
    public static string PlaceholderIcon(MediaItem item) => (item is PinnedCover c ? c.Target : item) switch
    {
        Artist => "MicVocal",
        Playlist => "ListMusic",
        MusicCollection => "Layers",
        Track => "Music",
        Station or ArtistMix or TagMix or BuiltMix => "Radio",
        TagItem => "Tags",
        _ => "Disc3",
    };
    /// <summary>Full opacity when on, half when off (e.g. a hidden Home section in Settings).</summary>
    public static double DimIfOff(bool on) => on ? 1 : 0.5;

    public static string Join(IReadOnlyList<string>? values, int max = 3) =>
        values is null || values.Count == 0 ? string.Empty : string.Join(", ", values.Take(max));

    public static string AlbumMeta(Album? a)
    {
        if (a is null) return string.Empty;
        var parts = new List<string>();
        if (a.Year is > 0) parts.Add(a.Year.Value.ToString());
        if (a.Genres.Count > 0) parts.Add(a.Genres[0]);
        if (a.TrackCount is > 0) parts.Add(a.TrackCount == 1 ? "1 track" : $"{a.TrackCount} tracks");
        return string.Join(" · ", parts);
    }

    public static double StarsFromRating(double? rating) => rating is { } r and >= 0 ? r / 2.0 : -1;
}
