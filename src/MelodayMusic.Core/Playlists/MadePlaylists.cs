using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Playlists;

public enum PlaylistShelf
{
    Mood,
    Morning,
    Afternoon,
    Evening,
    Night,
    Energy,
    Weekend,
    Moment,
    Genre,
}

public sealed record PlaylistRecipe(string Id, string Title, string Description, IReadOnlyList<(string Word, double Weight)> Wants,
    IReadOnlyList<string> Avoid, int? Hour, PlaylistShelf Shelf);

public sealed record PlaylistCandidate<T>(T Item, string Key, string? Artist, float[] Vector);

public static class MadePlaylists
{
    public const int Length = 30;
    public const int MaxPerArtist = 2;
    public const int PoolSize = 300;
    public const int Smallest = 15;
    public const string KeyPrefix = "made:";
    public static readonly double[] AvoidLimits = [0.5, 1.0, 1.5];

    public static IReadOnlyList<PlaylistRecipe> Recipes { get; } =
    [
        new("feel-good", "Feel Good", "Bright, positive songs that lift you up.",
            [("Happy", 1.0), ("Uplifting", 0.8), ("Playful", 0.5), ("Celebration", 0.5), ("Euphoric", 0.4), ("Hopeful", 0.3)],
            ["Sad", "Melancholy", "Lonely", "Dark", "Angry", "Brooding", "Heartbreak"], null, PlaylistShelf.Mood),
        new("relax", "Relax", "Smooth, warm, unhurried songs to unwind to.",
            [("Relaxed", 1.0), ("Mellow", 0.8), ("Peaceful", 0.6), ("Chill Out", 0.6), ("Very Calm", 0.3)],
            ["Intense", "Energetic", "Aggressive", "Tense", "Angry", "Screamed", "Dark"], null, PlaylistShelf.Mood),
        new("energy", "Energy", "Driving, exciting songs that keep you going.",
            [("Energetic", 1.0), ("Intense", 0.6), ("Confident", 0.4), ("Euphoric", 0.4), ("Workout", 0.4), ("Triumphant", 0.3)],
            ["Very Calm", "Relaxed", "Sleep", "Meditation", "Sad", "Lonely"], null, PlaylistShelf.Mood),
        new("focus", "Focus", "Steady, unobtrusive songs with few words to get in the way.",
            [("Focus", 1.0), ("Instrumental", 0.9), ("Steady", 0.5), ("Relaxed", 0.3)],
            ["Rap", "Spoken Word", "Screamed", "Intense", "Aggressive", "Party"], null, PlaylistShelf.Mood),
        new("heartbreak", "Heartbreak", "Songs of love lost, longing and missing someone.",
            [("Heartbreak", 1.0), ("Bittersweet", 0.6), ("Lonely", 0.5), ("Romantic", 0.4), ("Wistful", 0.4)],
            ["Happy", "Celebration", "Party", "Euphoric", "Playful"], null, PlaylistShelf.Mood),
        new("feeling-blue", "Feeling Blue", "Melancholy, lonely and reflective songs for low days.",
            [("Sad", 1.0), ("Melancholy", 0.9), ("Lonely", 0.5), ("Nostalgic", 0.4), ("Brooding", 0.4), ("Wistful", 0.3)],
            ["Happy", "Party", "Euphoric", "Celebration", "Playful", "Energetic"], null, PlaylistShelf.Mood),
        new("romance", "Romance", "Tender, warm songs for falling for someone.",
            [("Romantic", 1.0), ("Sensual", 0.4), ("Mellow", 0.4), ("Duet", 0.3)],
            ["Angry", "Aggressive", "Screamed", "Dark"], null, PlaylistShelf.Mood),
        new("daydream", "Daydream", "Hazy, floating songs to drift off with.",
            [("Dreamy", 1.0), ("Peaceful", 0.4), ("Wistful", 0.3), ("Mellow", 0.3)],
            ["Aggressive", "Angry", "Intense", "Screamed"], null, PlaylistShelf.Mood),
        new("nostalgia", "Nostalgia", "Songs that feel like looking back.",
            [("Nostalgic", 1.0), ("Bittersweet", 0.5), ("Wistful", 0.4), ("Hopeful", 0.2)],
            ["Aggressive", "Angry", "Screamed"], null, PlaylistShelf.Mood),
        new("confidence", "Confidence Boost", "Bold, swaggering songs that make you feel unstoppable.",
            [("Confident", 1.0), ("Triumphant", 0.5), ("Energetic", 0.4), ("Uplifting", 0.3)],
            ["Sad", "Lonely", "Melancholy", "Very Calm"], null, PlaylistShelf.Mood),
        new("playful", "Playful", "Light, cheeky songs that don't take themselves seriously.",
            [("Playful", 1.0), ("Happy", 0.6), ("Celebration", 0.3)],
            ["Dark", "Sad", "Brooding", "Angry", "Lonely"], null, PlaylistShelf.Mood),
        new("euphoria", "Euphoria", "Soaring, hands-in-the-air songs.",
            [("Euphoric", 1.0), ("Uplifting", 0.5), ("Dance Floor", 0.5), ("Energetic", 0.4)],
            ["Sad", "Lonely", "Melancholy", "Very Calm", "Sleep"], null, PlaylistShelf.Mood),
        new("hopeful", "Hopeful", "Songs about better days ahead.",
            [("Hopeful", 1.0), ("Uplifting", 0.6), ("Bittersweet", 0.3), ("Steady", 0.2)],
            ["Dark", "Angry", "Aggressive", "Brooding"], null, PlaylistShelf.Mood),
        new("peaceful", "Peaceful", "Gentle, quiet songs for a still mind.",
            [("Peaceful", 1.0), ("Very Calm", 0.6), ("Piano", 0.3), ("Mellow", 0.3)],
            ["Intense", "Aggressive", "Screamed", "Angry", "Party"], null, PlaylistShelf.Mood),
        new("bittersweet", "Bittersweet", "Songs that are happy and sad at once.",
            [("Bittersweet", 1.0), ("Wistful", 0.6), ("Nostalgic", 0.4), ("Hopeful", 0.3)],
            ["Party", "Aggressive", "Angry", "Celebration"], null, PlaylistShelf.Mood),
        new("slow-burn", "Slow Burn", "Sultry, slow songs for late evenings.",
            [("Sensual", 1.0), ("Romantic", 0.5), ("Late Night", 0.4), ("R&B", 0.3)],
            ["Aggressive", "Screamed", "Angry", "Playful"], null, PlaylistShelf.Mood),
        new("triumphant", "Triumphant", "Big, victorious songs for when you've won.",
            [("Triumphant", 1.0), ("Confident", 0.5), ("Uplifting", 0.4), ("Orchestra", 0.3)],
            ["Sad", "Lonely", "Melancholy", "Very Calm"], null, PlaylistShelf.Mood),
        new("mysterious", "Mysterious", "Shadowy, intriguing songs with something hidden.",
            [("Mysterious", 1.0), ("Tense", 0.4), ("Dark", 0.4), ("Dreamy", 0.3)],
            ["Happy", "Playful", "Celebration", "Party"], null, PlaylistShelf.Mood),
        new("dark-brooding", "Dark & Brooding", "Heavy-hearted, shadowy songs for dark moods.",
            [("Dark", 1.0), ("Brooding", 0.8), ("Tense", 0.4), ("Mysterious", 0.3)],
            ["Happy", "Playful", "Celebration", "Uplifting"], null, PlaylistShelf.Mood),
        new("let-it-out", "Let It Out", "Loud, angry songs to get it out of your system.",
            [("Angry", 1.0), ("Aggressive", 0.8), ("Intense", 0.5), ("Distorted Guitar", 0.3)],
            ["Very Calm", "Peaceful", "Relaxed", "Sleep", "Romantic"], null, PlaylistShelf.Mood),

        new("wake-up", "Wake Up", "Bright, easy songs to start the day with.",
            [("Morning", 1.0), ("Uplifting", 0.6), ("Hopeful", 0.5), ("Steady", 0.3)],
            ["Sad", "Melancholy", "Lonely", "Dark", "Aggressive", "Angry", "Sleep"], 7, PlaylistShelf.Morning),
        new("sunny-afternoon", "Sunny Afternoon", "Warm, feel-good songs for the sunny part of the day.",
            [("Summer", 1.0), ("Happy", 0.6), ("Beach", 0.4), ("Playful", 0.4)],
            ["Sad", "Melancholy", "Lonely", "Dark", "Brooding"], 14, PlaylistShelf.Afternoon),
        new("dinner", "Dinner", "Warm, mellow songs for the table, never a downer.",
            [("Dinner", 1.0), ("Mellow", 0.7), ("Relaxed", 0.4), ("Romantic", 0.3)],
            ["Sad", "Melancholy", "Lonely", "Heartbreak", "Dark", "Aggressive", "Intense", "Angry"], 19, PlaylistShelf.Evening),
        new("late-night", "Late Night", "Dreamy, low-lit songs for after dark.",
            [("Late Night", 1.0), ("Night Drive", 0.6), ("Dreamy", 0.5), ("Sensual", 0.3)],
            ["Morning", "Workout", "Celebration", "Playful"], 23, PlaylistShelf.Night),
        new("adrenaline", "Adrenaline", "The most intense, hard-hitting songs you have.",
            [("Intense", 1.0), ("Aggressive", 0.6), ("Energetic", 0.6), ("Drums", 0.3)],
            ["Very Calm", "Relaxed", "Sleep", "Peaceful", "Meditation"], 17, PlaylistShelf.Energy),
        new("workout", "Workout", "High energy songs to keep moving to.",
            [("Workout", 1.0), ("Energetic", 0.8), ("Intense", 0.5), ("Confident", 0.4)],
            ["Very Calm", "Relaxed", "Sleep", "Meditation", "Peaceful", "Sad", "Lonely"], 18, PlaylistShelf.Energy),
        new("party", "Party", "Loud, fun songs for a full room.",
            [("Party", 1.0), ("Dance Floor", 0.7), ("Celebration", 0.4), ("Energetic", 0.3)],
            ["Sad", "Lonely", "Very Calm", "Sleep", "Meditation", "Melancholy"], 22, PlaylistShelf.Energy),
        new("dance-floor", "Dance Floor", "Songs built for dancing.",
            [("Dance Floor", 1.0), ("House", 0.4), ("Disco", 0.4), ("Euphoric", 0.3)],
            ["Very Calm", "Sleep", "Sad", "Lonely", "Spoken Word"], 23, PlaylistShelf.Energy),
        new("get-moving", "Get Moving", "Upbeat songs to get you going.",
            [("Energetic", 1.0), ("Uplifting", 0.5), ("Confident", 0.4), ("Steady", 0.3)],
            ["Sad", "Lonely", "Sleep", "Very Calm", "Melancholy"], 9, PlaylistShelf.Energy),
        new("easy-going", "Easy Going", "Steady, mid-tempo songs that never rush.",
            [("Steady", 1.0), ("Mellow", 0.5), ("Happy", 0.3), ("Relaxed", 0.3)],
            ["Intense", "Aggressive", "Screamed", "Angry"], 13, PlaylistShelf.Energy),
        new("slow-down", "Slow Down", "Calm, slow songs to bring the pace right down.",
            [("Very Calm", 1.0), ("Relaxed", 0.6), ("Peaceful", 0.5), ("Soft Vocals", 0.3)],
            ["Intense", "Energetic", "Aggressive", "Party", "Screamed"], 21, PlaylistShelf.Energy),
        new("meditate", "Meditate", "Still, spacious music to breathe to.",
            [("Meditation", 1.0), ("Very Calm", 0.6), ("Ambient", 0.5), ("Peaceful", 0.4)],
            ["Rap", "Intense", "Aggressive", "Party", "Screamed"], 7, PlaylistShelf.Energy),
        new("sleep", "Sleep", "The softest, slowest songs you have.",
            [("Sleep", 1.0), ("Very Calm", 0.8), ("Ambient", 0.4), ("Peaceful", 0.4)],
            ["Energetic", "Intense", "Aggressive", "Party", "Drums", "Screamed"], 23, PlaylistShelf.Energy),
        new("road-trip", "Road Trip", "Big, open songs for the drive.",
            [("Road Trip", 1.0), ("Uplifting", 0.4), ("Energetic", 0.4), ("Nostalgic", 0.3), ("Summer", 0.2)],
            ["Sleep", "Very Calm", "Meditation", "Sad", "Lonely"], 12, PlaylistShelf.Weekend),
        new("rainy-day", "Rainy Day", "Soft, wistful songs for grey weather.",
            [("Rainy Day", 1.0), ("Mellow", 0.5), ("Wistful", 0.4), ("Melancholy", 0.3)],
            ["Energetic", "Party", "Aggressive", "Intense", "Celebration"], 15, PlaylistShelf.Moment),
        new("sunday-morning", "Sunday Morning", "Soft, slow songs for a lazy start.",
            [("Morning", 0.8), ("Peaceful", 0.6), ("Acoustic Guitar", 0.4), ("Mellow", 0.4)],
            ["Intense", "Aggressive", "Party", "Screamed", "Dark"], 10, PlaylistShelf.Weekend),
        new("coffee-shop", "Coffee Shop", "Acoustic, easy songs like a quiet café.",
            [("Acoustic Guitar", 0.8), ("Soft Vocals", 0.7), ("Mellow", 0.6), ("Singer-Songwriter", 0.4)],
            ["Aggressive", "Screamed", "Party", "Intense", "Distorted Guitar"], 11, PlaylistShelf.Moment),
        new("beach-day", "Beach Day", "Sunny, carefree songs for the sand.",
            [("Beach", 1.0), ("Summer", 0.7), ("Happy", 0.4), ("Reggae", 0.2)],
            ["Winter", "Dark", "Sad", "Brooding", "Lonely"], 13, PlaylistShelf.Afternoon),
        new("chill-out", "Chill Out", "Laid-back songs to switch off to.",
            [("Chill Out", 1.0), ("Relaxed", 0.6), ("Lo-Fi", 0.3), ("Mellow", 0.3)],
            ["Intense", "Aggressive", "Screamed", "Angry"], 16, PlaylistShelf.Moment),
        new("winter-warmers", "Winter Warmers", "Cosy songs for cold days.",
            [("Winter", 1.0), ("Mellow", 0.5), ("Nostalgic", 0.3), ("Acoustic Guitar", 0.3)],
            ["Summer", "Beach", "Party", "Aggressive"], 18, PlaylistShelf.Evening),
        new("golden-hour", "Golden Hour", "Warm, glowing songs for the end of the day.",
            [("Dreamy", 0.7), ("Summer", 0.6), ("Hopeful", 0.5), ("Mellow", 0.4)],
            ["Dark", "Angry", "Aggressive", "Tense"], 19, PlaylistShelf.Evening),
        new("getting-ready", "Getting Ready", "Confident, upbeat songs for heading out.",
            [("Confident", 0.7), ("Party", 0.5), ("Pop", 0.4), ("Energetic", 0.4)],
            ["Sad", "Sleep", "Very Calm", "Lonely"], 20, PlaylistShelf.Weekend),
        new("celebrate", "Celebrate", "Joyful songs for big occasions.",
            [("Celebration", 1.0), ("Happy", 0.6), ("Party", 0.5), ("Triumphant", 0.3)],
            ["Sad", "Lonely", "Melancholy", "Heartbreak", "Dark"], 20, PlaylistShelf.Weekend),
        new("campfire", "Campfire", "Acoustic, sing-along songs for around the fire.",
            [("Acoustic Guitar", 1.0), ("Folk", 0.7), ("Nostalgic", 0.4), ("Harmonies", 0.3)],
            ["Synthesizer", "Drum Machine", "Aggressive", "Screamed", "Dance Floor"], 21, PlaylistShelf.Evening),
        new("slow-dance", "Slow Dance", "Slow, close songs for two.",
            [("Romantic", 0.9), ("Duet", 0.5), ("Mellow", 0.5), ("Soul", 0.3)],
            ["Aggressive", "Angry", "Screamed", "Intense"], 22, PlaylistShelf.Evening),
        new("night-drive", "Night Drive", "Moody, glowing songs for empty roads.",
            [("Night Drive", 1.0), ("Late Night", 0.4), ("Synthesizer", 0.3), ("Dreamy", 0.3)],
            ["Morning", "Sleep", "Celebration", "Playful"], 22, PlaylistShelf.Night),
        new("stargazing", "Stargazing", "Vast, dreamy songs for clear nights.",
            [("Dreamy", 0.8), ("Ambient", 0.6), ("Peaceful", 0.5), ("Late Night", 0.5)],
            ["Party", "Aggressive", "Screamed", "Workout"], 0, PlaylistShelf.Night),
    ];

    private static readonly Dictionary<string, string> MoodSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cheerful"] = "feel-good", ["Upbeat"] = "feel-good", ["Fun"] = "feel-good", ["Joyous"] = "feel-good", ["Joyful"] = "feel-good",
        ["Exuberant"] = "feel-good", ["Carefree"] = "feel-good", ["Feel Good"] = "feel-good", ["Good Mood"] = "feel-good",
        ["Calm"] = "relax", ["Laid-Back"] = "relax", ["Laid Back"] = "relax", ["Chill"] = "relax", ["Chilled"] = "relax",
        ["Soothing"] = "relax", ["Relaxing"] = "relax", ["Serene"] = "relax", ["Tranquil"] = "relax", ["Easygoing"] = "relax",
        ["Energizing"] = "energy", ["Energising"] = "energy", ["Rousing"] = "energy", ["Exciting"] = "energy", ["Pumped"] = "energy",
        ["High Energy"] = "energy",
        ["Gloomy"] = "feeling-blue", ["Somber"] = "feeling-blue", ["Sombre"] = "feeling-blue", ["Depressing"] = "feeling-blue",
        ["Mournful"] = "feeling-blue", ["Downbeat"] = "feeling-blue", ["Sorrowful"] = "feeling-blue", ["Blue"] = "feeling-blue",
        ["Heartbroken"] = "heartbreak", ["Heartache"] = "heartbreak", ["Yearning"] = "heartbreak", ["Longing"] = "heartbreak",
        ["Focused"] = "focus", ["Concentration"] = "focus", ["Studying"] = "focus",
    };

    public static PlaylistRecipe? MoodRecipeFor(string mood)
    {
        var word = mood.Trim();
        if (MoodSynonyms.TryGetValue(word, out var id)) return Recipes.First(r => r.Id == id);
        var moods = Recipes.Where(r => r.Shelf == PlaylistShelf.Mood).ToList();
        return moods.FirstOrDefault(r => string.Equals(r.Title, word, StringComparison.OrdinalIgnoreCase))
            ?? moods.FirstOrDefault(r => r.Wants.Any(w => w.Weight >= 0.5 && string.Equals(w.Word, word, StringComparison.OrdinalIgnoreCase)));
    }

    public static string KeyFor(PlaylistRecipe recipe) => KeyPrefix + recipe.Id;
    public static bool IsKey(string? ratingKey) => ratingKey?.StartsWith(KeyPrefix, StringComparison.Ordinal) == true;
    public static PlaylistRecipe? RecipeOf(string? ratingKey) =>
        IsKey(ratingKey) ? Recipes.FirstOrDefault(r => r.Id == ratingKey![KeyPrefix.Length..]) ?? GenreEssentials.RecipeOf(ratingKey![KeyPrefix.Length..]) : null;

    public static DateOnly WeekOf(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.DateTime);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }

    public static IReadOnlyList<(SoundTerm Term, double Weight)> Words(PlaylistRecipe recipe, SoundVocabulary vocabulary) =>
        recipe.Wants.Select(w => (Term: vocabulary.Find(w.Word), w.Weight)).Where(w => w.Term is not null).Select(w => (w.Term!, w.Weight)).ToList();

    public static IReadOnlyList<SoundTerm> Avoided(PlaylistRecipe recipe, SoundVocabulary vocabulary) =>
        recipe.Avoid.Select(vocabulary.Find).OfType<SoundTerm>().ToList();

    public static IReadOnlyList<(PlaylistCandidate<T> Candidate, double Fit)> Pool<T>(PlaylistRecipe recipe, IReadOnlyList<PlaylistCandidate<T>> library,
        SoundVocabulary vocabulary, SoundCalibration? calibration, IReadOnlySet<string>? exclude = null, CancellationToken ct = default)
    {
        var wants = Words(recipe, vocabulary);
        if (wants.Count == 0) return [];
        var avoid = Avoided(recipe, vocabulary);
        var terms = wants.Select(w => w.Term).Concat(avoid).ToList();
        var total = wants.Sum(w => Math.Abs(w.Weight));
        var byKey = library.Where(c => exclude?.Contains(c.Key) != true).ToDictionary(c => c.Key, StringComparer.Ordinal);
        var measured = vocabulary.Measure(terms, byKey.Values.Select(c => (c.Key, c.Vector)), calibration, ct)
            .Select(m =>
            {
                double fit = 0;
                for (var i = 0; i < wants.Count; i++) fit += wants[i].Weight * m.Z[i];
                var worst = avoid.Count == 0 ? double.NegativeInfinity : m.Z.Skip(wants.Count).Max();
                return (m.Key, Fit: fit / total, Worst: worst);
            })
            .Where(m => m.Fit > 0)
            .OrderByDescending(m => m.Fit)
            .ToList();
        foreach (var limit in AvoidLimits)
        {
            var kept = measured.Where(m => m.Worst <= limit).Take(PoolSize).ToList();
            if (kept.Count >= Smallest * 2) return kept.Select(m => (byKey[m.Key], m.Fit)).ToList();
        }
        return measured.OrderBy(m => m.Worst).Take(PoolSize).OrderByDescending(m => m.Fit).Select(m => (byKey[m.Key], m.Fit)).ToList();
    }

    public static IReadOnlyList<T> Pick<T>(IReadOnlyList<(PlaylistCandidate<T> Candidate, double Fit)> pool, Func<T, double> taste, int seed,
        int length = Length)
    {
        if (pool.Count < Smallest) return [];
        var random = new Random(seed);
        var top = pool.Max(p => p.Fit);
        var bottom = pool.Min(p => p.Fit);
        var span = Math.Max(1e-6, top - bottom);
        var ranked = pool
            .Select(p => (p.Candidate, Score: (p.Fit - bottom) / span + 0.8 * taste(p.Candidate.Item) + 0.3 * random.NextDouble()))
            .OrderByDescending(p => p.Score)
            .ToList();

        var chosen = new List<PlaylistCandidate<T>>();
        var perArtist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (candidate, _) in ranked)
        {
            if (chosen.Count >= length) break;
            var artist = candidate.Artist ?? candidate.Key;
            if (perArtist.GetValueOrDefault(artist) >= MaxPerArtist) continue;
            perArtist[artist] = perArtist.GetValueOrDefault(artist) + 1;
            chosen.Add(candidate);
        }
        return chosen.Count < Smallest ? [] : Flow(chosen).Select(c => c.Item).ToList();
    }

    public static IReadOnlyList<PlaylistCandidate<T>> Flow<T>(IReadOnlyList<PlaylistCandidate<T>> songs)
    {
        if (songs.Count <= 2) return songs;
        var left = songs.Skip(1).ToList();
        var order = new List<PlaylistCandidate<T>> { songs[0] };
        while (left.Count > 0)
        {
            var last = order[^1];
            var next = left
                .OrderBy(c => c.Artist is not null && string.Equals(c.Artist, last.Artist, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(c => MusicSimilarity.Cosine(last.Vector, c.Vector))
                .First();
            order.Add(next);
            left.Remove(next);
        }
        return order;
    }

    public static int Rail(PlaylistShelf shelf) => shelf switch
    {
        PlaylistShelf.Mood => 0,
        PlaylistShelf.Energy => 1,
        PlaylistShelf.Genre => 3,
        _ => 2,
    };

    public static uint CoverColour(PlaylistRecipe recipe)
    {
        var rail = Recipes.Where(r => Rail(r.Shelf) == Rail(recipe.Shelf)).ToList();
        var index = rail.FindIndex(r => r.Id == recipe.Id);
        var families = MelodayPalette.Families.OrderBy(f => Stable(f.Name)).ToList();
        var family = families[(index < 0 ? Math.Abs(Stable(recipe.Id)) : index + Rail(recipe.Shelf) * 9) % families.Count];
        var readable = family.Shades.Where(c => DominantColor.Luminance(c) <= 0.55).ToList();
        return readable.Count == 0 ? family.Darker : readable[Math.Abs(Stable(recipe.Id)) % readable.Count];
    }

    public static int Stable(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text) hash = (hash ^ c) * 16777619;
            return hash & int.MaxValue;
        }
    }

    public static int Seed(PlaylistRecipe recipe, DateOnly week) => Stable($"{recipe.Id}|{week:yyyy-MM-dd}");
}
