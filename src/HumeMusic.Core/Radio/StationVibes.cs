using HumeMusic.Core.Models;

namespace HumeMusic.Core.Radio;

/// <summary>
/// What the genre-led live stations draw from: which of the library's own genres and moods belong to Club
/// and to Chill, and which decades Hits plays. A library is only ever offered the stations it can fill.
/// </summary>
public static class StationVibes
{
    /// <summary>The decades Hits plays: the '80s, '90s and 2000s.</summary>
    public static readonly IReadOnlyList<string> HitsDecades = ["1980", "1990", "2000"];

    private static readonly string[] ClubWords =
    [
        "dance", "electro", "house", "techno", "trance", "edm", "disco", "club", "rave", "breakbeat",
        "drum and bass", "drum & bass", "drum'n'bass", "dubstep", "garage", "eurodance", "hi-nrg", "bassline",
        // "Future Bass", "Brazilian Bass", "Bass House" ("experimental bass" is left out below); the big-room and bass-music families.
        "bass", "big room", "big beat", "moombahton", "complextro", "brostep", "neurostep", "amapiano", "ballroom", "funky", "jungle", "kuduro", "aggrotech",
    ];

    // A genre that has a club word in it but isn't club music ("Garage Rock", "Dancehall", "Beach House").
    private static readonly string[] NotClub = ["rock", "punk", "metal", "country", "folk", "blues", "soundtrack", "dancehall", "beach house", "experimental", "field recording", "powerpop", "witch house",
        // Pop with a dance beat is pop ("Dance Pop", "Electropop", "Indie Electropop", "Pop Dance"): too broad for a club.
        "dance pop", "dance-pop", "pop dance", "electropop", "electro-pop", "electro pop"];

    private static readonly string[] ChillWords =
    [
        "ambient", "chill", "downtempo", "trip hop", "trip-hop", "lounge", "new age", "lo-fi", "lofi", "easy listening",
        "relax", "meditat", "sleep", "atmospheric", "electronica", "dream", "idm", "slow core", "slowcore",
        // Vaporwave and "Vapor Soul", "Neo Mellow", "Study Beats", "Focus Beats", "Float House", "Escape Room", "Slowed and Reverb".
        "vapor", "mellow", "study", "focus", "float", "escape room", "slowed",
    ];

    // The calm moods (the library's artist moods follow AllMusic's: "Laid-Back/Mellow", "Calm/Peaceful", "Soft/Quiet"...). Thoughtful
    // or sad ones ("Reflective", "Wistful", "Introspective") aren't here: they describe a lot of singer-songwriter music that isn't Chill.
    private static readonly string[] ChillMoods =
    [
        "relaxed", "calm", "mellow", "peaceful", "serene", "soothing", "dreamy", "gentle", "laid-back", "laid back", "languid",
        "tranquil", "sleepy", "hypnotic", "quiet", "easygoing", "meditative", "ethereal", "reassuring",
    ];

    private static bool Has(string tag, string[] words) => words.Any(w => tag.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a genre belongs to Chill (it wins over Club where a genre is both, as with "chill house").</summary>
    public static bool IsChillGenre(string genre) => Has(genre, ChillWords);

    /// <summary>Whether a genre belongs to Club.</summary>
    public static bool IsClubGenre(string genre) =>
        !IsChillGenre(genre) && !Has(genre, NotClub) && (Has(genre, ClubWords) || genre.Contains("electronic", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether an artist's mood is a quiet one (Chill also draws on the artists with these moods).</summary>
    public static bool IsChillMood(string mood) => Has(mood, ChillMoods);

    /// <summary>Whether a genre belongs to the given genre-led station (false for any other kind).</summary>
    public static bool IsGenreOf(StationKind kind, string genre) => kind switch
    {
        StationKind.Club => IsClubGenre(genre),
        StationKind.Chill => IsChillGenre(genre),
        _ => false,
    };

    /// <summary>The year a decade tag stands for ("1990", "1990s" and "90s" are all the '90s), or null.</summary>
    public static int? DecadeOf(string decade)
    {
        var digits = new string(decade.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var year)) return null;
        if (digits.Length == 2) year += year < 30 ? 2000 : 1900;
        return year / 10 * 10;
    }

    /// <summary>
    /// The live stations the library can fill: Headliners, Library Radio and Deep Cuts always; Hits when it
    /// holds albums from the '80s, '90s or 2000s; Club and Chill when it holds music of those genres (Chill
    /// also by the moods of its artists).
    /// </summary>
    public static IReadOnlySet<StationKind> Available(IEnumerable<string> genres, IEnumerable<string> moods, IEnumerable<string> decades)
    {
        var offered = new HashSet<StationKind> { StationKind.Flagship, StationKind.Library, StationKind.DeepCuts };
        var genreList = genres.ToList();
        var wanted = HitsDecades.Select(d => int.Parse(d)).ToHashSet();
        if (decades.Any(d => DecadeOf(d) is { } year && wanted.Contains(year))) offered.Add(StationKind.Hits);
        if (genreList.Any(IsClubGenre)) offered.Add(StationKind.Club);
        if (genreList.Any(IsChillGenre) || moods.Any(IsChillMood)) offered.Add(StationKind.Chill);
        return offered;
    }

    /// <summary>Every live station (the library couldn't be asked, so none is held back).</summary>
    public static IReadOnlySet<StationKind> Everything { get; } = StationBrand.All.Select(b => b.Kind).ToHashSet();
}
