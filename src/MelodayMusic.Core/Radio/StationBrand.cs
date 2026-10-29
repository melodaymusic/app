using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Radio;

public sealed record StationBrand(StationKind Kind, string Id, string Title, string Summary, string Field, uint? Tint = null)
{
    public static IReadOnlyList<StationBrand> All { get; } =
    [
        new(StationKind.Flagship, "headliners", "Meloday Pulse", "Current music, new releases, chart picks and scheduled genre programs, with occasional artist spotlights.", "crimson"),
        new(StationKind.Hits, "hits", "Hits Radio", "The songs that defined the '80s, '90s and 2000s.", "green"),
        new(StationKind.Club, "club", "Club Radio", "Dance, electronic and club music.", "mono", 0x6A35E0),
        new(StationKind.Chill, "chill", "Chill Radio", "Ambient, downtempo and gentle electronic.", "mono", 0xFF6B4A),
        new(StationKind.Library, "library", "Library Radio", "Your whole library as one endless station, leaning to what you play and rate well.", "sapphire"),
        new(StationKind.DeepCuts, "deepcuts", "Deep Cuts Radio", "The songs you rarely reach for, from artists you love.", "lagoon"),
    ];

    public static StationBrand? For(StationKind kind) => All.FirstOrDefault(b => b.Kind == kind);

    public static IReadOnlyList<StationBrand> Ours { get; } = All.Where(b => b.Kind is StationKind.Flagship or StationKind.Hits or StationKind.Club or StationKind.Chill).ToList();

    public Station ToStation() => new()
    {
        RatingKey = "station:local:" + Id,
        Title = Title,
        StationKind = Kind,
        IsLocal = true,
        Summary = Summary,
    };
}
