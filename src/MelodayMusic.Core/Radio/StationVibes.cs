using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Radio;

public static class StationVibes
{
    public static readonly IReadOnlyList<string> HitsDecades = ["1980", "1990", "2000"];

    private static readonly string[] ClubWords =
    [
        "dance", "electro", "house", "techno", "trance", "edm", "disco", "club", "rave", "breakbeat",
        "drum and bass", "drum & bass", "drum'n'bass", "dubstep", "garage", "eurodance", "hi-nrg", "bassline",
        "bass", "big room", "big beat", "moombahton", "complextro", "brostep", "neurostep", "amapiano", "ballroom", "funky", "jungle", "kuduro", "aggrotech",
    ];

    private static readonly string[] NotClub = ["rock", "punk", "metal", "country", "folk", "blues", "soundtrack", "dancehall", "beach house", "experimental", "field recording", "powerpop", "witch house",
        "dance pop", "dance-pop", "pop dance", "electropop", "electro-pop", "electro pop"];

    private static readonly string[] ChillWords =
    [
        "ambient", "chill", "downtempo", "trip hop", "trip-hop", "lounge", "new age", "lo-fi", "lofi", "easy listening",
        "relax", "meditat", "sleep", "atmospheric", "electronica", "dream", "idm", "slow core", "slowcore",
        "vapor", "mellow", "study", "focus", "float", "escape room", "slowed",
    ];

    private static readonly string[] ChillMoods =
    [
        "relaxed", "calm", "mellow", "peaceful", "serene", "soothing", "dreamy", "gentle", "laid-back", "laid back", "languid",
        "tranquil", "sleepy", "hypnotic", "quiet", "easygoing", "meditative", "ethereal", "reassuring",
    ];

    private static bool Has(string tag, string[] words) => words.Any(w => tag.Contains(w, StringComparison.OrdinalIgnoreCase));

    public static bool IsChillGenre(string genre) => Has(genre, ChillWords);

    public static bool IsClubGenre(string genre) =>
        !IsChillGenre(genre) && !Has(genre, NotClub) && (Has(genre, ClubWords) || genre.Contains("electronic", StringComparison.OrdinalIgnoreCase));

    public static bool IsChillMood(string mood) => Has(mood, ChillMoods);

    public static bool IsGenreOf(StationKind kind, string genre) => kind switch
    {
        StationKind.Club => IsClubGenre(genre),
        StationKind.Chill => IsChillGenre(genre),
        _ => false,
    };

    public static int? DecadeOf(string decade)
    {
        var digits = new string(decade.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var year)) return null;
        if (digits.Length == 2) year += year < 30 ? 2000 : 1900;
        return year / 10 * 10;
    }

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

    public static IReadOnlySet<StationKind> Everything { get; } = StationBrand.All.Select(b => b.Kind).ToHashSet();
}
