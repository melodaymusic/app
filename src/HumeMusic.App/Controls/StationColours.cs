using HumeMusic.Core.Artwork;
using HumeMusic.Core.Models;
using HumeMusic.Core.Radio;

namespace HumeMusic.Controls;

/// <summary>A station's palette family and the colour field it moves in.</summary>
public static class StationColours
{
    public static PaletteFamily Family(Station station)
    {
        var families = MelodayPalette.Families;
        string? named = station.Colour ?? station.StationKind switch
        {
            StationKind.Flagship => "Persimmon",
            StationKind.Hits => "Magenta",
            StationKind.Club => "Violet",
            StationKind.Chill => "Cobalt",
            StationKind.Library => "Cobalt",
            StationKind.DeepCuts => "Dark Teal",
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
    /// The colour field a station moves in (see <see cref="MotionArtwork"/>): each live station has its own (Club and
    /// Chill the neutral one, tinted); any other takes the field nearest its palette colour.
    /// </summary>
    public static string Field(Station station) =>
        StationBrand.For(station.StationKind)?.Field
        ?? (station.StationKind == StationKind.Meloday ? "crimson" : MotionArtwork.FieldFor(Family(station)));

    /// <summary>The colour laid over a station's field, or null where the field is as it is.</summary>
    public static uint? Tint(Station station) => StationBrand.For(station.StationKind)?.Tint;
}
