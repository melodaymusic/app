using System.Globalization;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex.Dto;

namespace HumeMusic.Core.Plex;

/// <summary>Converts Plex wire DTOs into the application's typed music models.</summary>
public static class PlexMapper
{
    public static MediaItem? ToMediaItem(MetadataDto dto)
    {
        if (string.IsNullOrEmpty(dto.RatingKey)) return null;
        return dto.Type switch
        {
            "artist" => ToArtist(dto),
            "album" => ToAlbum(dto),
            "track" => ToTrack(dto),
            "playlist" => ToPlaylist(dto),
            "collection" => ToCollection(dto),
            _ => null,
        };
    }

    public static Artist ToArtist(MetadataDto d) => new()
    {
        RatingKey = d.RatingKey ?? string.Empty,
        Title = CleanTitle(d.Title) ?? "Unknown artist",
        TitleSort = d.TitleSort,
        Thumb = d.Thumb,
        Art = d.Art,
        Logo = LogoOf(d),
        Palette = Palette(d),
        Summary = NullIfEmpty(d.Summary),
        Genres = Tags(d.Genre),
        Styles = Tags(d.Style),
        Moods = Tags(d.Mood),
        GenreRefs = TagRefs(d.Genre),
        StyleRefs = TagRefs(d.Style),
        MoodRefs = TagRefs(d.Mood),
        Countries = Tags(d.Country),
        SimilarArtists = Tags(d.Similar),
        Collections = Tags(d.Collection),
        ViewCount = d.ViewCount ?? 0,
        SkipCount = d.SkipCount ?? 0,
        UserRating = d.UserRating,
        AddedAt = FromUnix(d.AddedAt),
        LastViewedAt = FromUnix(d.LastViewedAt),
        AlbumCount = d.ChildCount,
        HasSonicAnalysis = d.MusicAnalysisVersion is > 0,
        MusicBrainzId = MusicBrainzId(d.Guids),
    };

    /// <summary>The MusicBrainz id among an item's external ids ("mbid://5a8e07d5-…").</summary>
    public static string? MusicBrainzId(IEnumerable<TagDto>? guids) =>
        guids?.Select(g => g.Id).FirstOrDefault(id => id?.StartsWith("mbid://", StringComparison.OrdinalIgnoreCase) == true)?["mbid://".Length..]
            is { Length: > 0 } id && System.Guid.TryParse(id, out _) ? id : null;

    public static Album ToAlbum(MetadataDto d) => new()
    {
        RatingKey = d.RatingKey ?? string.Empty,
        Title = CleanTitle(d.Title) ?? "Unknown album",
        Thumb = d.Thumb,
        Art = d.Art,
        Logo = LogoOf(d),
        Palette = Palette(d),
        ArtistTitle = d.ParentTitle,
        ArtistRatingKey = d.ParentRatingKey,
        ArtistThumb = d.ParentThumb,
        Year = d.Year ?? d.ParentYear,
        ReleaseDate = ParseDate(d.OriginallyAvailableAt),
        Summary = NullIfEmpty(d.Summary),
        Studio = d.Studio,
        Genres = Tags(d.Genre),
        Styles = Tags(d.Style),
        Moods = Tags(d.Mood),
        Formats = Tags(d.Format),
        Subformats = Tags(d.Subformat),
        TrackCount = d.LeafCount,
        ViewCount = d.ViewCount ?? 0,
        UserRating = d.UserRating,
        CriticRating = d.Rating,
        AddedAt = FromUnix(d.AddedAt),
        LastViewedAt = FromUnix(d.LastViewedAt),
        HasSonicAnalysis = d.MusicAnalysisVersion is > 0,
    };

    public static Track ToTrack(MetadataDto d)
    {
        var media = d.Media?.FirstOrDefault();
        var part = media?.Part?.FirstOrDefault();
        var audio = part?.Stream?.FirstOrDefault(s => s.StreamType == 2);
        long duration = d.Duration ?? media?.Duration ?? part?.Duration ?? 0;
        return new Track
        {
            RatingKey = d.RatingKey ?? string.Empty,
            Key = d.Key,
            Title = CleanTitle(d.Title) ?? "Unknown track",
            Thumb = d.Thumb,
            Art = d.Art ?? d.GrandparentArt,
            ArtistTitle = d.GrandparentTitle,
            TrackArtist = NullIfEmpty(d.OriginalTitle),
            ArtistRatingKey = d.GrandparentRatingKey,
            ArtistThumb = d.GrandparentThumb,
            AlbumTitle = d.ParentTitle,
            AlbumRatingKey = d.ParentRatingKey,
            AlbumThumb = d.ParentThumb,
            TrackNumber = d.Index,
            DiscNumber = d.ParentIndex,
            DurationMs = duration,
            Year = d.Year ?? d.ParentYear,
            UserRating = d.UserRating,
            ViewCount = d.ViewCount ?? 0,
            SkipCount = d.SkipCount ?? 0,
            RatingCount = d.RatingCount,
            LastViewedAt = FromUnix(d.LastViewedAt),
            AddedAt = FromUnix(d.AddedAt),
            Moods = Tags(d.Mood),
            Genres = Tags(d.Genre),
            HasSonicAnalysis = d.MusicAnalysisVersion is > 0,
            SonicDistance = d.Distance,
            PlaylistItemId = d.PlaylistItemID,
            LibrarySectionId = d.LibrarySectionID,
            Media = media is null ? null : new AudioMediaInfo
            {
                Codec = audio?.Codec ?? media.AudioCodec,
                Container = part?.Container ?? media.Container,
                Bitrate = audio?.Bitrate ?? media.Bitrate,
                Channels = audio?.Channels ?? media.AudioChannels,
                SampleRate = audio?.SamplingRate,
                BitDepth = audio?.BitDepth,
                PartKey = part?.Key,
                PartId = part?.Id,
                Size = part?.Size,
                FilePath = part?.File,
            },
        };
    }

    /// <summary>The item's logo on the server: the picture Plex lists among its images as a clear logo.</summary>
    public static string? LogoOf(MetadataDto d) =>
        d.Image?.FirstOrDefault(i => string.Equals(i.Type, "clearLogo", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(i.Url))?.Url;

    public static Playlist ToPlaylist(MetadataDto d) => new()
    {
        RatingKey = d.RatingKey ?? string.Empty,
        Key = d.Key,
        Title = CleanTitle(d.Title) ?? "Untitled playlist",
        Thumb = d.Thumb,
        Art = d.Art,
        Logo = LogoOf(d),
        Composite = d.Composite,
        Palette = Palette(d),
        Summary = NullIfEmpty(d.Summary),
        TrackCount = d.LeafCount,
        DurationMs = d.Duration ?? 0,
        IsSmart = d.Smart ?? false,
        UpdatedAt = FromUnix(d.UpdatedAt),
        LastViewedAt = FromUnix(d.LastViewedAt),
    };

    /// <summary>
    /// Titles without leading or trailing invisible characters (Hangul fillers, zero-width spaces)
    /// that some tools add to steer Plex's sort order; they render as a blank gap.
    /// </summary>
    public static string? CleanTitle(string? title)
    {
        if (title is null) return null;
        static bool Invisible(char c) => char.IsWhiteSpace(c) || c is 'ㅤ' or 'ᅟ' or 'ᅠ' or 'ﾠ' or '​' or '‌' or '‍' or '⁠' or '﻿';
        var start = 0;
        var end = title.Length;
        while (start < end && Invisible(title[start])) start++;
        while (end > start && Invisible(title[end - 1])) end--;
        return start == end ? title : title[start..end];
    }

    public static MusicCollection ToCollection(MetadataDto d) => new()
    {
        RatingKey = d.RatingKey ?? string.Empty,
        Key = d.Key,
        Title = CleanTitle(d.Title) ?? "Collection",
        Thumb = d.Thumb,
        Art = d.Art,
        Logo = LogoOf(d),
        Summary = NullIfEmpty(d.Summary),
        ChildCount = d.ChildCount,
        Subtype = d.Subtype,
        AddedAt = FromUnix(d.AddedAt),
        UpdatedAt = FromUnix(d.UpdatedAt),
        IsSmart = d.Smart ?? false,
    };

    public static TagItem? ToTag(DirectoryDto d, TagKind kind)
    {
        // Tag directories expose the filter value in "key" (e.g. "1234") and fastKey
        // (e.g. "/library/sections/3/all?genre=1234"). Search results use "id"/"tag".
        var value = d.Key ?? d.Id;
        var title = d.Title ?? d.Tag;
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(title)) return null;
        if (value.Contains('/') && !string.IsNullOrEmpty(d.FastKey)) value = ExtractFilterValue(d.FastKey) ?? value;
        return new TagItem
        {
            RatingKey = $"{kind}:{value}",
            Title = title,
            Thumb = d.Thumb,
            TagKind = kind,
            FilterValue = value,
            Count = d.Count,
        };
    }

    public static MusicSection ToSection(DirectoryDto d) => new()
    {
        ContentVersion = d.ContentChangedAt,
        Key = d.Key ?? string.Empty,
        Title = d.Title ?? "Music",
        Uuid = d.Uuid,
        Agent = d.Agent,
        UpdatedAt = FromUnix(d.UpdatedAt),
    };

    public static Hub ToHub(HubDto d)
    {
        var items = new List<MediaItem>();
        if (d.Metadata is not null)
        {
            foreach (var m in d.Metadata)
            {
                if (ToMediaItem(m) is { } item) items.Add(item);
            }
        }
        return new Hub
        {
            Identifier = d.HubIdentifier ?? string.Empty,
            Title = d.Title ?? string.Empty,
            Key = d.Key ?? d.HubKey,
            Type = d.Type,
            Style = d.Style,
            More = d.More ?? false,
            Size = d.Size,
            Items = items,
        };
    }

    private static ArtworkPalette? Palette(MetadataDto d) =>
        d.UltraBlurColors is { } u ? ArtworkPalette.Parse(u.TopLeft, u.TopRight, u.BottomRight, u.BottomLeft) : null;

    public static IReadOnlyList<TagRef> TagRefs(List<TagDto>? tags) =>
        tags is null || tags.Count == 0
            ? []
            : tags.Where(t => !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Tag)).Select(t => new TagRef(t.Id!, t.Tag!)).ToList();

    public static IReadOnlyList<string> Tags(List<TagDto>? tags) =>
        tags is null || tags.Count == 0
            ? []
            : tags.Select(t => t.Tag).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();

    public static DateTimeOffset? FromUnix(long? seconds) =>
        seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;

    private static DateTimeOffset? ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? ExtractFilterValue(string fastKey)
    {
        var q = fastKey.IndexOf('?');
        if (q < 0) return null;
        foreach (var pair in fastKey[(q + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] != "type") return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }
}
