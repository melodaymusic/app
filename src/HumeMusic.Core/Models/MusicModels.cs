namespace HumeMusic.Core.Models;

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

/// <summary>Four artwork colours (corner samples) supplied by Plex, as 0xRRGGBB values.</summary>
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

/// <summary>Common base for everything a Plex music hub, search result or list can contain.</summary>
public abstract class MediaItem
{
    public string RatingKey { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Thumb { get; init; }
    public string? Art { get; init; }
    /// <summary>The item's logo on the Plex server (a transparent picture of its name, as Plex keeps for the item), when it has one.</summary>
    public string? Logo { get; init; }
    /// <summary>Artwork colours for ambient page tinting, when Plex provides them.</summary>
    public ArtworkPalette? Palette { get; init; }
    public abstract MediaKind Kind { get; }

    /// <summary>Secondary line of text used when this item is displayed in a mixed list.</summary>
    public virtual string Subtitle => string.Empty;

    // Also used as the accessible name of list items that show this item.
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
    /// <summary>The library collections the artist belongs to.</summary>
    public IReadOnlyList<string> Collections { get; init; } = [];
    public int SkipCount { get; init; }
    /// <summary>Genre, style and mood tags with Plex's ids (for tag stations); empty when Plex didn't send ids.</summary>
    public IReadOnlyList<TagRef> GenreRefs { get; init; } = [];
    public IReadOnlyList<TagRef> StyleRefs { get; init; } = [];
    public IReadOnlyList<TagRef> MoodRefs { get; init; } = [];
    /// <summary>The artist's MusicBrainz id (from Plex's external ids), used to find its logo on fanart.tv.</summary>
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
    /// <summary>Where the file is on the server.</summary>
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
    /// <summary>Album artist (grandparentTitle).</summary>
    public string? ArtistTitle { get; init; }
    /// <summary>Per-track artist when it differs from the album artist (originalTitle).</summary>
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

    /// <summary>The artist to show for this track: the track artist when set, otherwise the album artist.</summary>
    public string DisplayArtist => !string.IsNullOrWhiteSpace(TrackArtist) ? TrackArtist! : ArtistTitle ?? "Unknown artist";

    /// <summary>Best available artwork: the album cover, then the track's own thumb, then the artist.</summary>
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

    /// <summary>A subtitle for a particular context (e.g. "6 songs" on an artist's page).</summary>
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
    /// <summary>Smart collections are filled by rules and can't be arranged by hand.</summary>
    public bool IsSmart { get; init; }

    public DateTimeOffset? AddedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public override string Subtitle => ChildCount is { } n ? $"Collection · {n} items" : "Collection";
}

/// <summary>Artists ranked by how many tracks they contribute (for playlist and collection summaries).</summary>
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

/// <summary>A browsable metadata value (genre, mood, style, decade...) inside a music section.</summary>
public sealed class TagItem : MediaItem
{
    public override MediaKind Kind => MediaKind.Tag;
    public TagKind TagKind { get; init; }
    /// <summary>Value used for the section filter (e.g. <c>genre=1234</c>).</summary>
    public string FilterValue { get; init; } = string.Empty;
    public int? Count { get; init; }
    /// <summary>For a family of tags on Browse (Pop, gathering Dance Pop, Soft Pop...), the tags it gathers.</summary>
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
    /// <summary>Changes whenever the library's content does (Plex's contentChangedAt).</summary>
    public long? ContentVersion { get; init; }
    public override string ToString() => Title;
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Offset { get; init; }
    /// <summary>Total number of items matching the query, when the server reported it.</summary>
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

/// <summary>
/// Plex's mix for an artist (the artist's radio station): the artist's own tracks mixed with similar
/// artists'. Played through a Plex play queue, topped up as it plays.
/// </summary>
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

/// <summary>
/// A mix around a genre or mood from the listener's recent history ("Dance Pop Mix", "Campy Mix"):
/// random tracks from the library with that genre or mood, topped up as it plays.
/// NicheMix inherits this transport so old pin/reference plumbing can still carry it safely.
/// </summary>
public class TagMix : MediaItem
{
    public override MediaKind Kind => MediaKind.Mix;
    public required MixTagKind TagKind { get; init; }
    public required string Tag { get; init; }
    public override string Subtitle => RatingKey.StartsWith("mix:niche:", StringComparison.Ordinal)
        ? "Niche mix"
        : TagKind == MixTagKind.Genre ? "Genre mix" : "Mood mix";
}

/// <summary>A tag with Plex's id for it ("Pop-Rap", 95727).</summary>
public sealed record TagRef(string Id, string Title);

public enum StationKind
{
    Library,
    DeepCuts,
    /// <summary>Plex's Time Travel: no longer offered (Hits stands in for it); kept so what was pinned still reads.</summary>
    TimeTravel,
    RandomAlbum,
    Genre,
    Style,
    Mood,
    Decade,
    ArtistMixBuilder,
    AlbumMixBuilder,
    /// <summary>Meloday: a mix for this part of the day, made from what the listener plays at this time.</summary>
    Meloday,
    /// <summary>Your Day: no longer offered (Headliners stands in for it); kept so what was pinned still reads.</summary>
    YourDay,
    /// <summary>Headliners: live, the biggest songs of the library an artist at a time.</summary>
    Flagship,
    /// <summary>Hits: live, the songs that defined the '80s, '90s and 2000s.</summary>
    Hits,
    /// <summary>Club: live dance, electronic and club music.</summary>
    Club,
    /// <summary>Chill: live ambient, downtempo and gentle electronic.</summary>
    Chill,
}

/// <summary>
/// A radio station: one of Plex's stations (Library Radio, Deep Cuts, a genre's radio...), which
/// keeps playing new music, or one of Meloday's mix builders.
/// </summary>
public sealed class Station : MediaItem
{
    public override MediaKind Kind => MediaKind.Station;
    public required StationKind StationKind { get; init; }
    /// <summary>Plex's key for the station (null when Meloday plays it from the library itself).</summary>
    public string? Key { get; init; }
    /// <summary>The genre, style or mood id, or the decade, for tag stations.</summary>
    public string? Tag { get; init; }
    /// <summary>A line about the station (what it plays, or who is in it).</summary>
    public string? Summary { get; init; }
    /// <summary>The Meloday palette family it is shown in, when chosen for it (otherwise by name).</summary>
    public string? Colour { get; init; }
    /// <summary>A mix builder rather than a station: it opens to choose artists or albums.</summary>
    public bool IsBuilder => StationKind is StationKind.ArtistMixBuilder or StationKind.AlbumMixBuilder;
    /// <summary>Plex's own stations are part of Plex Pass; the builders and the library-made stations are Meloday's.</summary>
    public bool NeedsPlexPass => Key is not null;
    /// <summary>Made by Meloday from the library (its own radio) rather than served by Plex.</summary>
    public bool IsLocal { get; init; }
    /// <summary>
    /// A live station: always on, as if it had been playing all along. Tuning in joins it in the middle of
    /// a song and where it has got to by now, instead of starting a fresh radio from its first song.
    /// </summary>
    public bool IsLive => StationKind is StationKind.Library or StationKind.DeepCuts or StationKind.Flagship or StationKind.Hits or StationKind.Club or StationKind.Chill;
    public override string Subtitle => IsBuilder ? "Radio builder" : IsLive ? "Live" : "Radio";
}

/// <summary>A group of stations to choose from (Genre Radio, Style Radio, Mood Radio, Decade Radio).</summary>
public sealed record StationDirectory(string Title, StationKind Kind, string Key);

/// <summary>A mix the listener built from artists or albums: their music, shuffled, topped up as it plays.</summary>
public sealed class BuiltMix : MediaItem
{
    public override MediaKind Kind => MediaKind.Mix;
    public IReadOnlyList<string> ArtistKeys { get; init; } = [];
    public IReadOnlyList<string> AlbumKeys { get; init; } = [];
    public override string Subtitle => "Mix";
}

/// <summary>An image Plex offers for an item (from its agents, or uploaded), and whether it is in use.</summary>
public sealed record ArtworkOption(string Key, string? Thumb, bool Selected, string? Provider);

public enum ArtworkKind
{
    /// <summary>The cover: an artist's photo, an album's or playlist's picture (Plex's poster).</summary>
    Poster,
    /// <summary>The wide backdrop (Plex's art).</summary>
    Backdrop,
    /// <summary>The logo (Plex's clear logo): a transparent picture of the item's name.</summary>
    Logo,
}
