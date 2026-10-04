using HumeMusic.Core.Artwork;
using HumeMusic.Core.Models;

namespace HumeMusic.Controls;

/// <summary>A station's palette family and the colour field it moves in.</summary>
public static class StationColours
{
    public static PaletteFamily Family(Station station)
    {
        var families = MelodayPalette.Families;
        string? named = station.Colour ?? station.StationKind switch
        {
            StationKind.Library => "Cobalt",
            StationKind.DeepCuts => "Dark Teal",
            StationKind.TimeTravel => "Plum",
            StationKind.RandomAlbum => "Persimmon",
            StationKind.ArtistMixBuilder => "Violet",
            StationKind.AlbumMixBuilder => "Magenta",
            _ => null,
        };
        if (named is not null && families.FirstOrDefault(f => f.Name == named) is { } chosen) return chosen;
        var hash = 0;
        foreach (var c in station.Title) hash = unchecked(hash * 31 + char.ToLowerInvariant(c));
        return families[(int)((uint)hash % families.Count)];
    }

    /// <summary>
    /// The colour field a station moves in (see <see cref="MotionArtwork"/>): Plex's four stations
    /// each have their own; any other takes the field nearest its palette colour.
    /// </summary>
    public static string Field(Station station) => station.StationKind switch
    {
        StationKind.Meloday => "crimson",
        StationKind.Library => "sapphire",
        StationKind.DeepCuts => "lagoon",
        StationKind.TimeTravel => "green",
        _ => MotionArtwork.FieldFor(Family(station)),
    };
}
