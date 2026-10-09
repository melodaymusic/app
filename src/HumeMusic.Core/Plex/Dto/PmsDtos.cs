using System.Text.Json.Serialization;

namespace HumeMusic.Core.Plex.Dto;

// Wire-format DTOs for Plex Media Server JSON responses (Accept: application/json).
// Every field is optional: Plex omits attributes freely depending on server version,
// agent and library content, so nothing here may be assumed present.

public sealed class PmsResponse
{
    [JsonPropertyName("MediaContainer")] public MediaContainerDto? MediaContainer { get; set; }
}

public sealed class MediaContainerDto
{
    public int? Size { get; set; }
    /// <summary>A transient token (/security/token).</summary>
    public string? Token { get; set; }
    public int? TotalSize { get; set; }
    public int? Offset { get; set; }
    public string? Title1 { get; set; }
    public string? Title2 { get; set; }
    public string? Identifier { get; set; }
    public string? MachineIdentifier { get; set; }
    public string? FriendlyName { get; set; }
    public string? Version { get; set; }
    public string? LibrarySectionID { get; set; }
    public string? LibrarySectionTitle { get; set; }

    // Play queue containers
    public long? PlayQueueID { get; set; }
    public long? PlayQueueSelectedItemID { get; set; }
    public int? PlayQueueTotalCount { get; set; }
    public int? PlayQueueVersion { get; set; }

    [JsonPropertyName("Metadata")] public List<MetadataDto>? Metadata { get; set; }
    [JsonPropertyName("Directory")] public List<DirectoryDto>? Directory { get; set; }
    [JsonPropertyName("Hub")] public List<HubDto>? Hub { get; set; }
    /// <summary>Returned by /services/ultrablur/colors for an arbitrary image.</summary>
    [JsonPropertyName("Level")] public List<LevelDto>? Level { get; set; }
    [JsonPropertyName("Lyrics")] public List<LyricsDto>? Lyrics { get; set; }
    [JsonPropertyName("UltraBlurColors")] public List<UltraBlurColorsDto>? UltraBlurColors { get; set; }
}

public sealed class TagDto
{
    public string? Id { get; set; }
    public string? Tag { get; set; }
    public string? Filter { get; set; }
}

/// <summary>A lyrics stream's content (LyricFind, through Plex Pass): its lines, with times when synced.</summary>
public sealed class LyricsDto
{
    public string? Timed { get; set; }
    [JsonPropertyName("Line")] public List<LyricLineDto>? Line { get; set; }
}

public sealed class LyricLineDto
{
    public long? StartOffset { get; set; }
    public long? EndOffset { get; set; }
    [JsonPropertyName("Span")] public List<LyricSpanDto>? Span { get; set; }
}

public sealed class LyricSpanDto
{
    public string? Text { get; set; }
    /// <summary>When the span is sung (milliseconds into the song), if the lyrics are timed word by word.</summary>
    public long? StartOffset { get; set; }
}

/// <summary>One point of a stream's loudness envelope (/library/streams/{id}/levels).</summary>
public sealed class LevelDto
{
    public double V { get; set; }
}

public sealed class StreamDto
{
    public long? Id { get; set; }
    /// <summary>Lyrics streams: their address, format ("lrc", "txt") and whether they are synced.</summary>
    public string? Key { get; set; }
    public string? Format { get; set; }
    public string? Timed { get; set; }
    public int? StreamType { get; set; }
    public string? Codec { get; set; }
    public int? Bitrate { get; set; }
    public int? Channels { get; set; }
    public int? SamplingRate { get; set; }
    public int? BitDepth { get; set; }
    public string? DisplayTitle { get; set; }
    public string? ExtendedDisplayTitle { get; set; }
    public string? Gain { get; set; }
    public string? AlbumGain { get; set; }
    public string? Loudness { get; set; }
    public string? Peak { get; set; }
    public bool? Selected { get; set; }
}

public sealed class PartDto
{
    public string? Id { get; set; }
    public string? Key { get; set; }
    public long? Duration { get; set; }
    public string? File { get; set; }
    public long? Size { get; set; }
    public string? Container { get; set; }
    public string? AudioProfile { get; set; }
    public bool? Accessible { get; set; }
    public bool? Exists { get; set; }
    [JsonPropertyName("Stream")] public List<StreamDto>? Stream { get; set; }
}

public sealed class MediaDto
{
    public string? Id { get; set; }
    public long? Duration { get; set; }
    public int? Bitrate { get; set; }
    public int? AudioChannels { get; set; }
    public string? AudioCodec { get; set; }
    public string? Container { get; set; }
    [JsonPropertyName("Part")] public List<PartDto>? Part { get; set; }
}

public sealed class ImageDto
{
    public string? Alt { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
}

public sealed class MetadataDto
{
    public string? RatingKey { get; set; }
    public string? Key { get; set; }
    public string? Guid { get; set; }
    public string? Type { get; set; }
    public string? Subtype { get; set; }
    public string? Title { get; set; }
    public string? TitleSort { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Summary { get; set; }
    public string? Studio { get; set; }

    public string? ParentRatingKey { get; set; }
    public string? ParentKey { get; set; }
    public string? ParentTitle { get; set; }
    public string? ParentThumb { get; set; }
    public int? ParentIndex { get; set; }
    public int? ParentYear { get; set; }

    public string? GrandparentRatingKey { get; set; }
    public string? GrandparentKey { get; set; }
    public string? GrandparentTitle { get; set; }
    public string? GrandparentThumb { get; set; }
    public string? GrandparentArt { get; set; }

    public string? Thumb { get; set; }
    public string? Art { get; set; }
    public string? Composite { get; set; }

    public int? Index { get; set; }
    public int? Year { get; set; }
    public string? OriginallyAvailableAt { get; set; }
    public long? Duration { get; set; }
    public int? LeafCount { get; set; }
    public int? ViewedLeafCount { get; set; }
    public int? ChildCount { get; set; }
    public int? ViewCount { get; set; }
    public int? SkipCount { get; set; }
    public long? LastViewedAt { get; set; }
    /// <summary>When a history entry was played (listening history only).</summary>
    public long? ViewedAt { get; set; }
    /// <summary>Whose play a history entry is (the server owner is 1).</summary>
    public long? AccountID { get; set; }
    public long? LastRatedAt { get; set; }
    public long? AddedAt { get; set; }
    public long? UpdatedAt { get; set; }
    public long? ViewOffset { get; set; }
    public double? UserRating { get; set; }
    public double? Rating { get; set; }
    public double? AudienceRating { get; set; }
    public int? RatingCount { get; set; }
    public int? MusicAnalysisVersion { get; set; }
    public double? Distance { get; set; }

    public string? LibrarySectionID { get; set; }
    public string? LibrarySectionTitle { get; set; }

    // Playlist specific
    public string? PlaylistType { get; set; }
    public bool? Smart { get; set; }
    /// <summary>A smart playlist's rules: "library://x/directory/" and its escaped filter path.</summary>
    public string? Content { get; set; }
    public string? PlaylistItemID { get; set; }

    // Play queue specific
    public string? PlayQueueItemID { get; set; }

    // Search result specific
    public double? Score { get; set; }

    // Artwork choices (/posters, /arts)
    public bool? Selected { get; set; }
    public string? Provider { get; set; }

    /// <summary>External ids ("mbid://…" for MusicBrainz); the lower-case "guid" is Plex's own.</summary>
    [JsonPropertyName("Guid")] public List<TagDto>? Guids { get; set; }
    [JsonPropertyName("Genre")] public List<TagDto>? Genre { get; set; }
    [JsonPropertyName("Mood")] public List<TagDto>? Mood { get; set; }
    [JsonPropertyName("Style")] public List<TagDto>? Style { get; set; }
    [JsonPropertyName("Country")] public List<TagDto>? Country { get; set; }
    [JsonPropertyName("Collection")] public List<TagDto>? Collection { get; set; }
    [JsonPropertyName("Similar")] public List<TagDto>? Similar { get; set; }
    [JsonPropertyName("Format")] public List<TagDto>? Format { get; set; }
    [JsonPropertyName("Subformat")] public List<TagDto>? Subformat { get; set; }
    [JsonPropertyName("Label")] public List<TagDto>? Label { get; set; }
    [JsonPropertyName("Media")] public List<MediaDto>? Media { get; set; }
    [JsonPropertyName("Image")] public List<ImageDto>? Image { get; set; }
    [JsonPropertyName("UltraBlurColors")] public UltraBlurColorsDto? UltraBlurColors { get; set; }
    /// <summary>An artist's radio stations (Plex's artist mix), with <c>includeStations=1</c>.</summary>
    [JsonPropertyName("Stations")] public StationsDto? Stations { get; set; }
}

public sealed class StationsDto
{
    [JsonPropertyName("Metadata")] public List<MetadataDto>? Metadata { get; set; }
}

/// <summary>Plex's precomputed artwork palette (hex RGB, no '#'), used for ambient backgrounds.</summary>
public sealed class UltraBlurColorsDto
{
    public string? TopLeft { get; set; }
    public string? TopRight { get; set; }
    public string? BottomRight { get; set; }
    public string? BottomLeft { get; set; }
}

public sealed class DirectoryDto
{
    public string? Key { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? Uuid { get; set; }
    public string? Agent { get; set; }
    public string? Thumb { get; set; }
    public string? Composite { get; set; }
    public string? FastKey { get; set; }
    public string? Filter { get; set; }
    public string? Id { get; set; }
    public string? Tag { get; set; }
    public string? TagType { get; set; }
    public int? Count { get; set; }
    public long? UpdatedAt { get; set; }
    public long? ScannedAt { get; set; }
    /// <summary>A counter Plex raises as the library's content changes (music added, removed, edited).</summary>
    public long? ContentChangedAt { get; set; }
    public string? LibrarySectionID { get; set; }
    public string? RatingKey { get; set; }
}

public sealed class HubDto
{
    public string? HubKey { get; set; }
    public string? Key { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? HubIdentifier { get; set; }
    public string? Context { get; set; }
    public int? Size { get; set; }
    public bool? More { get; set; }
    public string? Style { get; set; }
    public bool? Promoted { get; set; }
    [JsonPropertyName("Metadata")] public List<MetadataDto>? Metadata { get; set; }
    [JsonPropertyName("Directory")] public List<DirectoryDto>? Directory { get; set; }
}
