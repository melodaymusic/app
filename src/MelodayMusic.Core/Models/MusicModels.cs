namespace MelodayMusic.Core.Models;

public enum MediaKind
{
    Unknown,
    Artist,
    Album,
    Track,
    Playlist,
    Collection,
    Tag,
    Mix,
    Station,
}

public sealed record ArtworkPalette(uint TopLeft, uint TopRight, uint BottomRight, uint BottomLeft)
{
    public static ArtworkPalette? Parse(string? tl, string? tr, string? br, string? bl)
    {
        static uint? Hex(string? s) =>
            s is { Length: 6 } && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
        var a = Hex(tl); var b = Hex(tr); var c = Hex(br); var d = Hex(bl);
        return a is null || b is null || c is null || d is null ? null : new ArtworkPalette(a.Value, b.Value, c.Value, d.Value);
    }

    public static string Hex(uint rgb) => (rgb & 0xFFFFFF).ToString("x6", System.Globalization.CultureInfo.InvariantCulture);
}

public abstract class MediaItem
{
    public string RatingKey { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Thumb { get; init; }
    public string? Art { get; init; }
    public string? Logo { get; init; }
    public ArtworkPalette? Palette { get; init; }
    public abstract MediaKind Kind { get; }

    public virtual string Subtitle => string.Empty;

    public override string ToString() => string.IsNullOrEmpty(Subtitle) ? Title : $"{Title}, {Subtitle}";
}

public sealed class Artist : MediaItem
{
    public override MediaKind Kind => MediaKind.Artist;
    public string? TitleSort { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Styles { get; init; } = [];
    public IReadOnlyList<string> Moods { get; init; } = [];
    public IReadOnlyList<string> Countries { get; init; } = [];
    public IReadOnlyList<string> SimilarArtists { get; init; } = [];
    public IReadOnlyList<string> Collections { get; init; } = [];
    public int SkipCount { get; init; }
    public IReadOnlyList<TagRef> GenreRefs { get; init; } = [];
    public IReadOnlyList<TagRef> StyleRefs { get; init; } = [];
    public IReadOnlyList<TagRef> MoodRefs { get; init; } = [];
    public string? MusicBrainzId { get; init; }
    public int ViewCount { get; init; }
    public double? UserRating { get; init; }
    public DateTimeOffset? AddedAt { get; init; }
    public DateTimeOffset? LastViewedAt { get; init; }
    public int? AlbumCount { get; init; }
    public bool HasSonicAnalysis { get; init; }

    public override string Subtitle => Genres.Count > 0 ? string.Join(", ", Genres.Take(2)) : "Artist";
}

public sealed class Album : MediaItem
{
    public override MediaKind Kind => MediaKind.Album;
    public string? ArtistTitle { get; init; }
    public string? ArtistRatingKey { get; init; }
    public string? ArtistThumb { get; init; }
    public int? Year { get; init; }
    public DateTimeOffset? ReleaseDate { get; init; }
    public string? Summary { get; init; }
    public string? Studio { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Styles { get; init; } = [];
    public IReadOnlyList<string> Moods { get; init; } = [];
    public IReadOnlyList<string> Formats { get; init; } = [];
    public IReadOnlyList<string> Subformats { get; init; } = [];
    public int? TrackCount { get; init; }
    public int ViewCount { get; init; }
    public double? UserRating { get; init; }
    public double? CriticRating { get; init; }
    public DateTimeOffset? AddedAt { get; init; }
    public DateTimeOffset? LastViewedAt { get; init; }
    public bool HasSonicAnalysis { get; init; }

    public override string Subtitle =>
        Year is > 0 && !string.IsNullOrEmpty(ArtistTitle) ? $"{ArtistTitle} · {Year}"
        : ArtistTitle ?? (Year?.ToString() ?? string.Empty);
}

public sealed class AudioMediaInfo
{
    public string? Codec { get; init; }
    public string? Container { get; init; }
    public int? Bitrate { get; init; }
    public int? Channels { get; init; }
    public int? SampleRate { get; init; }
    public int? BitDepth { get; init; }
    public string? PartKey { get; init; }
    public string? PartId { get; init; }
    public long? Size { get; init; }
    public string? FilePath { get; init; }

    public string Describe()
    {
        var bits = new List<string>();
        if (!string.IsNullOrEmpty(Codec)) bits.Add(Codec.ToUpperInvariant());
        if (BitDepth is > 0 && SampleRate is > 0) bits.Add($"{BitDepth}-bit / {SampleRate / 1000.0:0.#} kHz");
        else if (SampleRate is > 0) bits.Add($"{SampleRate / 1000.0:0.#} kHz");
        if (Bitrate is > 0) bits.Add($"{Bitrate} kbps");
        return string.Join(" · ", bits);
    }
}

public sealed class Track : MediaItem
{
    public override MediaKind Kind => MediaKind.Track;
    public string? Key { get; init; }
    public string? ArtistTitle { get; init; }
    public string? TrackArtist { get; init; }
    public string? ArtistRatingKey { get; init; }
    public string? AlbumTitle { get; init; }
    public string? AlbumRatingKey { get; init; }
    public string? AlbumThumb { get; init; }
    public string? ArtistThumb { get; init; }
    public int? TrackNumber { get; init; }
    public int? DiscNumber { get; init; }
    public long DurationMs { get; init; }
    public int? Year { get; init; }
    public double? UserRating { get; init; }
    public int ViewCount { get; init; }
    public int SkipCount { get; init; }
    public int? RatingCount { get; init; }
    public DateTimeOffset? LastViewedAt { get; init; }
    public DateTimeOffset? AddedAt { get; init; }
    public IReadOnlyList<string> Moods { get; init; } = [];
    public IReadOnlyList<string> Genres { get; init; } = [];
    public AudioMediaInfo? Media { get; init; }
    public bool HasSonicAnalysis { get; init; }
    public double? SonicDistance { get; init; }
    public string? PlaylistItemId { get; init; }
    public string? LibrarySectionId { get; init; }

    public string DisplayArtist => !string.IsNullOrWhiteSpace(TrackArtist) ? TrackArtist! : ArtistTitle ?? "Unknown artist";

    public string? DisplayThumb => AlbumThumb ?? Thumb ?? ArtistThumb;

    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

    public override string Subtitle => string.IsNullOrEmpty(AlbumTitle) ? DisplayArtist : $"{DisplayArtist} · {AlbumTitle}";
}

public sealed class Playlist : MediaItem
{
    public override MediaKind Kind => MediaKind.Playlist;
    public string? Key { get; init; }
    public string? Summary { get; init; }
    public string? Composite { get; init; }
    public int? TrackCount { get; init; }
    public long DurationMs { get; init; }
    public bool IsSmart { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? LastViewedAt { get; init; }

    public string? DisplayThumb => Thumb ?? Composite;

    public string? SubtitleOverride { get; init; }

    public override string Subtitle => SubtitleOverride
        ?? (IsSmart ? "Smart playlist" : "Playlist") + (TrackCount is { } n ? (n == 1 ? " · 1 track" : $" · {n} tracks") : string.Empty);
}

public sealed class MusicCollection : MediaItem
{
    public override MediaKind Kind => MediaKind.Collection;
    public string? Key { get; init; }
    public string? Summary { get; init; }
    public int? ChildCount { get; init; }
    public string? Subtype { get; init; }
    public bool IsSmart { get; init; }

    public DateTimeOffset? AddedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public override string Subtitle => ChildCount is { } n ? $"Collection · {n} items" : "Collection";
}

public static class ArtistRanking
{
    public static IReadOnlyList<string> Rank(IEnumerable<string?> artistPerTrack) =>
        artistPerTrack
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim())
            .GroupBy(a => a, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (Name: g.First(), Count: g.Count(), First: g.Key))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Name)
            .ToList();
}

public enum TagKind
{
    Genre,
    Mood,
    Style,
    Decade,
    Year,
    Country,
}

public sealed class TagItem : MediaItem
{
    public override MediaKind Kind => MediaKind.Tag;
    public TagKind TagKind { get; init; }
    public string FilterValue { get; init; } = string.Empty;
    public int? Count { get; init; }
    public IReadOnlyList<TagItem> Members { get; init; } = [];
    public bool IsFamily => Members.Count > 0;
    public override string Subtitle => TagKind.ToString();
}

public sealed class Hub
{
    public string Identifier { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Key { get; init; }
    public string? Type { get; init; }
    public string? Style { get; init; }
    public bool More { get; init; }
    public int? Size { get; init; }
    public IReadOnlyList<MediaItem> Items { get; init; } = [];
}

public sealed class MusicSection
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Uuid { get; init; }
    public string? Agent { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public long? ContentVersion { get; init; }
    public override string ToString() => Title;
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Offset { get; init; }
    public int? TotalSize { get; init; }
    public bool HasMore => TotalSize is { } t ? Offset + Items.Count < t : Items.Count > 0;
}

public sealed class SearchResults
{
    public IReadOnlyList<Artist> Artists { get; init; } = [];
    public IReadOnlyList<Album> Albums { get; init; } = [];
    public IReadOnlyList<Track> Tracks { get; init; } = [];
    public IReadOnlyList<Playlist> Playlists { get; init; } = [];
    public IReadOnlyList<MusicCollection> Collections { get; init; } = [];
    public IReadOnlyList<TagItem> Tags { get; init; } = [];
    public bool IsEmpty => Artists.Count == 0 && Albums.Count == 0 && Tracks.Count == 0 && Playlists.Count == 0 && Collections.Count == 0 && Tags.Count == 0;
}

public sealed class ArtistMix : MediaItem
{
    public override MediaKind Kind => MediaKind.Mix;
    public required Artist Artist { get; init; }
    public override string Subtitle => "Artist mix";
}

public enum MixTagKind
{
    Genre,
    Mood,
}

public class TagMix : MediaItem
{
    public override MediaKind Kind => MediaKind.Mix;
    public required MixTagKind TagKind { get; init; }
    public required string Tag { get; init; }
    public override string Subtitle => RatingKey.StartsWith("mix:niche:", StringComparison.Ordinal)
        ? "Niche mix"
        : TagKind == MixTagKind.Genre ? "Genre mix" : "Mood mix";
}

public sealed record TagRef(string Id, string Title);

public enum StationKind
{
    Library,
    DeepCuts,
    TimeTravel,
    RandomAlbum,
    Genre,
    Style,
    Mood,
    Decade,
    ArtistMixBuilder,
    AlbumMixBuilder,
    Meloday,
    YourDay,
    Flagship,
    Hits,
    Club,
    Chill,
}

public sealed class Station : MediaItem
{
    public override MediaKind Kind => MediaKind.Station;
    public required StationKind StationKind { get; init; }
    public string? Key { get; init; }
    public string? Tag { get; init; }
    public string? Summary { get; init; }
    public string? Colour { get; init; }
    public bool IsBuilder => StationKind is StationKind.ArtistMixBuilder or StationKind.AlbumMixBuilder;
    public bool NeedsPlexPass => Key is not null;
    public bool IsLocal { get; init; }
    public bool IsLive => StationKind is StationKind.Library or StationKind.DeepCuts or StationKind.Flagship or StationKind.Hits or StationKind.Club or StationKind.Chill;
    public override string Subtitle => IsBuilder ? "Radio builder" : IsLive ? "Live" : "Radio";
}

public sealed record StationDirectory(string Title, StationKind Kind, string Key);

public sealed class BuiltMix : MediaItem
{
    public override MediaKind Kind => MediaKind.Mix;
    public IReadOnlyList<string> ArtistKeys { get; init; } = [];
    public IReadOnlyList<string> AlbumKeys { get; init; } = [];
    public override string Subtitle => "Mix";
}

public sealed record ArtworkOption(string Key, string? Thumb, bool Selected, string? Provider);

public enum ArtworkKind
{
    Poster,
    Backdrop,
    Logo,
}
