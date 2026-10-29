using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Controls;

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

    public static string Field(Station station) =>
        StationBrand.For(station.StationKind)?.Field
        ?? (station.StationKind == StationKind.Meloday ? "crimson" : MotionArtwork.FieldFor(Family(station)));

    public static uint? Tint(Station station) => StationBrand.For(station.StationKind)?.Tint;

    /// <summary>The colour the station's field is laid on, which its name and symbol are set against.</summary>
    public static uint Ground(Station station) => MotionArtwork.GroundFor(Field(station), Tint(station));
}
