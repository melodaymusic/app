using HumeMusic.Core.Models;

namespace HumeMusic.Core.Radio;

/// <summary>
/// A live station's identity: its name, what it plays, and its cover (one of the app's colour fields, moving, and
/// for the stations without a field of their own a neutral one with a colour laid over it). Pure data: the
/// drawing is the app's.
/// </summary>
/// <param name="Field">The colour field the cover moves in ("sapphire", "lagoon", "green", "crimson", or "mono", the neutral one that takes a tint).</param>
/// <param name="Tint">The colour laid over the field (null: the field as it is).</param>
public sealed record StationBrand(StationKind Kind, string Id, string Title, string Summary, string Field, uint? Tint = null)
{
    /// <summary>How much of the tint is laid over the field (0-1).</summary>
    public const double TintOpacity = 0.72;

    /// <summary>The stations in the order the listener meets them.</summary>
    public static IReadOnlyList<StationBrand> All { get; } =
    [
        new(StationKind.Flagship, "headliners", "Headliners Radio", "Your biggest artists, new releases and what's trending, in artist blocks, new runs and genre blocks.", "crimson"),
        new(StationKind.Hits, "hits", "Hits Radio", "The songs that defined the '80s, '90s and 2000s.", "green"),
        new(StationKind.Club, "club", "Club Radio", "Dance, electronic and club music.", "mono", 0x6A35E0),
        new(StationKind.Chill, "chill", "Chill Radio", "Ambient, downtempo and gentle electronic.", "mono", 0xFF6B4A),
        new(StationKind.Library, "library", "Library Radio", "Your whole library as one endless station, leaning to what you play and rate well.", "sapphire"),
        new(StationKind.DeepCuts, "deepcuts", "Deep Cuts Radio", "The songs you rarely reach for, from artists you love.", "lagoon"),
    ];

    /// <summary>The brand of a live station's kind, or null for anything else.</summary>
    public static StationBrand? For(StationKind kind) => All.FirstOrDefault(b => b.Kind == kind);

    /// <summary>The stations only Meloday makes (the rest, Library Radio and Deep Cuts, Plex may serve with Plex Pass).</summary>
    public static IReadOnlyList<StationBrand> Ours { get; } = All.Where(b => b.Kind is StationKind.Flagship or StationKind.Hits or StationKind.Club or StationKind.Chill).ToList();

    /// <summary>The station for this brand: made by Meloday from the library.</summary>
    public Station ToStation() => new()
    {
        RatingKey = "station:local:" + Id,
        Title = Title,
        StationKind = Kind,
        IsLocal = true,
        Summary = Summary,
    };
}
