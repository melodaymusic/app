using System.Text.Json;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Pins;

public static class ItemRefs
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static PinKind? KindOf(MediaItem item) => item switch
    {
        PinnedCover cover => KindOf(cover.Target),
        Artist => PinKind.Artist,
        Album => PinKind.Album,
        Playlist => PinKind.Playlist,
        MusicCollection => PinKind.Collection,
        Track => PinKind.Track,
        Station { IsBuilder: true } => null,
        Station => PinKind.Station,
        ArtistMix => PinKind.ArtistMix,
        TagMix => PinKind.TagMix,
        BuiltMix => PinKind.BuiltMix,
        TagItem { IsFamily: false } => PinKind.Tag,
        _ => null,
    };

    public static string TitleOf(MediaItem item) => item switch
    {
        Station { StationKind: StationKind.Meloday } => "Meloday",
        _ => item.Title,
    };

    public static bool IsSynthetic(PinKind kind) => kind is PinKind.Station or PinKind.ArtistMix or PinKind.TagMix or PinKind.BuiltMix or PinKind.Tag;

    private sealed record StationData(StationKind Kind, string? Key, string? Tag, string? Colour, bool Local, string? Summary);
    private sealed record ArtistMixData(string ArtistKey, string ArtistTitle, string? ArtistThumb, string? ArtistArt);
    private sealed record TagMixData(MixTagKind Kind, string Tag);
    private sealed record BuiltMixData(string[] ArtistKeys, string[] AlbumKeys);
    private sealed record TagData(TagKind Kind, string Filter, int? Count);

    public static string? DataOf(MediaItem item) => (item is PinnedCover cover ? cover.Target : item) switch
    {
        Station { IsBuilder: false } s => JsonSerializer.Serialize(new StationData(s.StationKind, s.Key, s.Tag, s.Colour, s.IsLocal, s.Summary)),
        ArtistMix a => JsonSerializer.Serialize(new ArtistMixData(a.Artist.RatingKey, a.Artist.Title, a.Artist.Thumb, a.Artist.Art)),
        TagMix t => JsonSerializer.Serialize(new TagMixData(t.TagKind, t.Tag)),
        BuiltMix b => JsonSerializer.Serialize(new BuiltMixData([.. b.ArtistKeys], [.. b.AlbumKeys])),
        TagItem t => JsonSerializer.Serialize(new TagData(t.TagKind, t.FilterValue, t.Count)),
        _ => null,
    };

    public static MediaItem? ToItem(PinKind kind, string ratingKey, string title, string? thumb, string? data)
    {
        try
        {
            switch (kind)
            {
                case PinKind.Artist: return new Artist { RatingKey = ratingKey, Title = title, Thumb = thumb };
                case PinKind.Playlist: return new Playlist { RatingKey = ratingKey, Title = title, Thumb = thumb };
                case PinKind.Collection: return new MusicCollection { RatingKey = ratingKey, Title = title, Thumb = thumb };
                case PinKind.Album: return new Album { RatingKey = ratingKey, Title = title, Thumb = thumb };
                case PinKind.Track: return new Track { RatingKey = ratingKey, Title = title, Thumb = thumb, AlbumThumb = thumb };
                case PinKind.Station when data is not null && JsonSerializer.Deserialize<StationData>(data, Json) is { } s:
                    if (s.Kind == StationKind.YourDay) return Radio.StationBrand.For(StationKind.Flagship)!.ToStation();
                    if (s.Kind == StationKind.TimeTravel) return Radio.StationBrand.For(StationKind.Hits)!.ToStation();
                    if (s.Local && Radio.StationBrand.For(s.Kind) is { } brand) return brand.ToStation();
                    return new Station { RatingKey = ratingKey, Title = title, StationKind = s.Kind, Key = s.Key, Tag = s.Tag, Colour = s.Colour, IsLocal = s.Local, Summary = s.Summary };
                case PinKind.ArtistMix when data is not null && JsonSerializer.Deserialize<ArtistMixData>(data, Json) is { } a:
                    return new ArtistMix
                    {
                        RatingKey = ratingKey,
                        Title = title,
                        Thumb = thumb,
                        Artist = new Artist { RatingKey = a.ArtistKey, Title = a.ArtistTitle, Thumb = a.ArtistThumb, Art = a.ArtistArt },
                    };
                case PinKind.TagMix when data is not null && JsonSerializer.Deserialize<TagMixData>(data, Json) is { } t:
                    return new TagMix { RatingKey = ratingKey, Title = title, TagKind = t.Kind, Tag = t.Tag };
                case PinKind.BuiltMix when data is not null && JsonSerializer.Deserialize<BuiltMixData>(data, Json) is { } b:
                    return new BuiltMix { RatingKey = ratingKey, Title = title, ArtistKeys = b.ArtistKeys, AlbumKeys = b.AlbumKeys };
                case PinKind.Tag when data is not null && JsonSerializer.Deserialize<TagData>(data, Json) is { } g:
                    return new TagItem { RatingKey = ratingKey, Title = title, TagKind = g.Kind, FilterValue = g.Filter, Count = g.Count };
                default: return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
