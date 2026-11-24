using System.Collections.Concurrent;

namespace MelodayMusic.Core.Models;

/// <summary>
/// Groups specific genres and moods under broad families ("dance pop" and "k-pop" under Pop).
/// A tag goes to the family whose keyword fits it best: a keyword that is the whole name, then the longest phrase,
/// then a word that sets the context ("christmas", "latin", "christian", "indie"), then the last word of the name
/// ("psychedelic trance" is trance), then an exact word over a wildcard, then the order below.
/// The listener can move any tag to another family; those choices are kept as overrides.
/// </summary>
public static class TagFamilies
{
    // Declared first: the compiled rules below split their keywords with it.
    private static readonly char[] Separators = [' ', '/', ',', '(', ')', '.', ':', ';', '+'];

    private static readonly (string Name, string[] Words)[] Genres =
    [
        ("Hip-Hop", ["hip hop", "hip-hop", "hiphop", "rap", "trap", "drill", "grime", "boom bap", "crunk", "phonk", "*hop", "hyphy", "pluggnb", "r&drill", "new orleans bounce", "bounce", "dirty south", "turntablism",
            "g-funk", "horrorcore", "emo rap", "lo-fi beats", "lofi beats", "lo-fi hip hop", "lofi hip hop", "jazz rap", "gangsta", "uk rap"]),
        ("R&B & Soul", ["r&b", "rnb", "r and b", "soul", "funk", "motown", "new jack swing", "quiet storm", "doo wop", "urban", "rhythm and blues", "rhythm & blues", "neo soul", "neo-soul", "alternative r&b"]),
        ("Alternative", ["alternative", "indie", "alt", "shoegaze", "dream pop", "post-punk", "new wave", "lo-fi", "lofi", "emo", "*gaze", "slowcore", "austindie", "darkwave", "cold wave", "coldwave", "escape room", "bubblegrunge", "beatlesque", "mellow gold", "neo mellow", "southern gothic", "indie*",
            "indie rock", "indie pop", "indie folk", "midwest emo"]),
        ("Latin", ["latin", "reggaeton", "salsa", "bachata", "cumbia", "urbano", "samba", "tango", "bossa nova", "mpb", "corrido", "corridos", "banda", "norteño", "norteno", "mariachi", "merengue", "tropical", "cha cha cha", "mambo", "chihuahuense", "sierreño", "chicha", "cuban", "plena", "puerto rican", "tropicalia",
            "latin trap", "latin hip hop", "dembow", "regional mexican", "musica mexicana", "música mexicana", "mexican", "ranchera", "bolero", "vallenato", "tejano", "grupera", "duranguense", "latin pop", "latin rock"]),
        ("African", ["african", "afrobeat", "afrobeats", "afropop", "afro-pop", "afro", "afro*", "afroswing", "amapiano", "highlife", "hiplife", "alte", "alté", "azonto", "gqom", "kwaito", "bongo flava", "naija", "soukous", "makossa", "mbalax",
            "zouglou", "coupé-décalé", "kizomba", "congolese", "ghanaian", "nigerian", "kenyan", "ethiopian", "malian", "south african", "desert blues", "afro soul"]),
        ("South Asian", ["bollywood", "indian", "desi", "punjabi", "bhangra", "filmi", "tollywood", "kollywood", "qawwali", "ghazal", "carnatic", "hindustani", "sufi", "pakistani", "bangladeshi", "tamil", "telugu", "hindi", "nepali", "sri lankan",
            "desi hip hop", "indian classical"]),
        ("Pop", ["pop", "k-pop", "j-pop", "c-pop", "teen", "boy band", "girl group", "*pop", "pop*", "idol", "kayokyoku", "shibuya-kei", "denpa-kei", "variété", "talent show", "europop", "adult contemporary", "girl groups", "celebrity", "dance pop", "dance-pop"]),
        ("Metal", ["metal", "metalcore", "deathcore", "djent", "doom", "sludge", "grindcore", "mathcore", "*metal", "alternative metal"]),
        ("Rock", ["rock", "punk", "grunge", "hardcore", "garage", "psychedelic", "surf", "rockabilly", "britpop", "aor", "psychobilly", "psychedelia", "jam bands", "*punk", "american underground", "pop rock", "pop punk", "post-rock", "post-hardcore", "math rock", "stoner"]),
        ("Electronic", ["electronic", "electronica", "edm", "house", "techno", "trance", "dubstep", "drum and bass", "drum & bass", "dnb", "jungle", "breakbeat", "breaks", "dance", "disco", "electro", "synth", "synthwave", "idm", "ambient", "downtempo", "trip hop", "trip-hop", "chillwave", "hardstyle", "bass", "uk garage", "club", "eurodance", "hi-nrg", "big room", "future", "*tronica", "*step", "*core", "*techno", "*trance", "electro*", "big beat", "bassline", "balearic", "complextro", "ghettotech", "moombahton", "melbourne bounce", "new rave", "new french touch", "vaporwave", "vapor", "chill out", "chillout", "beats", "chillsynth", "cyberpunk", "pixel", "uk funky", "ukg", "krushclub", "groove room", "high vibe", "hopebeat", "bossbeat", "slowed", "ballroom", "*wave", "*synth*", "broken beat", "downbeat", "rave",
            "happy hardcore", "uk hardcore", "hardcore techno", "psytrance", "psychedelic trance", "goa trance", "french house", "tropical house", "afro house", "chiptune", "garage house", "speed garage", "nu disco", "italo disco", "jersey club", "baltimore club", "footwork", "gabber"]),
        ("Jazz", ["jazz", "swing", "bebop", "bop", "big band", "fusion", "*jazz", "improvisation", "modern creative", "modern free", "jazz funk", "jazz-funk", "jazz piano", "spiritual jazz", "soul jazz", "acid jazz", "jazz fusion"]),
        ("Blues", ["blues", "delta blues", "chicago blues"]),
        ("Country", ["country", "americana", "bluegrass", "honky tonk", "outlaw", "western", "red dirt", "western swing", "alt-country", "alt country", "country pop", "pop country", "country rock", "nashville", "alternative country"]),
        ("Folk", ["folk", "singer-songwriter", "singer/songwriter", "acoustic", "celtic", "folk*"]),
        ("Reggae", ["reggae", "dancehall", "ska", "dub", "rocksteady", "ragga", "reggae fusion", "lovers rock", "roots reggae"]),
        ("Classical", ["classical", "orchestral", "opera", "baroque", "chamber", "symphony", "choral", "romantic", "contemporary classical", "minimalism", "piano", "neoclassical", "modern composition", "contemporary instrumental", "early music", "renaissance"]),
        ("Soundtrack", ["soundtrack", "soundtracks", "score", "film", "musical", "musicals", "show tunes", "video game", "tv", "cast recordings", "anime", "vgm", "video game music", "game soundtrack", "musical theatre", "musical theater", "broadway", "film score"]),
        ("Gospel & Christian", ["gospel", "christian", "worship", "ccm", "spiritual", "inspirational", "christian hip hop", "christian rap", "praise"]),
        ("World", ["world", "arabic", "flamenco", "fado", "brazilian", "caribbean", "calypso", "soca", "zouk", "chutney", "mizrahi", "malay", "vietnamese", "chanson", "french", "traditions", "gypsy", "middle eastern", "turkish", "worldbeat", "azeri",
            "baile funk", "funk carioca", "brazilian funk", "forró", "forro", "sertanejo", "pagode", "axé", "klezmer", "balkan", "greek", "persian", "khaleeji", "rai", "polka"]),
        ("Easy Listening", ["easy listening", "standards", "adult standards", "lounge", "vocal", "exotica", "crooner", "cabaret", "new age", "a cappella", "acappella"]),
        ("Holiday", ["christmas", "holiday", "holidays", "xmas"]),
        ("Kids & Family", ["children", "children's", "kids", "family", "lullaby", "lullabies", "nursery"]),
        ("Comedy & Spoken", ["comedy", "spoken", "spoken word", "audiobook", "poetry", "stand-up", "podcast"]),
        ("Experimental", ["experimental", "avant-garde", "avant", "noise", "industrial", "musique concrete", "sound art"]),
    ];

    // Words that say where or what a song is for outrank the style word next to them, in this order.
    private static readonly string[] Context =
        ["Holiday", "Kids & Family", "Comedy & Spoken", "Soundtrack", "Gospel & Christian", "Latin", "African", "South Asian", "World", "Alternative"];

    private static readonly (string Name, string[] Words)[] Moods =
    [
        ("Happy & Upbeat", ["happy", "cheerful", "joyous", "joyful", "upbeat", "bright", "sunny", "carefree", "exuberant", "euphoric", "celebratory", "party", "partying", "fun", "lively", "rollicking", "raucous", "whimsical", "humorous", "silly", "amiable", "good-natured", "freewheeling", "gleeful", "hopeful", "optimistic", "summery", "light", "springlike", "festive", "agreeable", "giddy", "irreverent", "playful", "quirky", "witty", "sweet", "animated", "bouncy", "cute", "ebullient", "ecstatic", "effervescent", "jovial", "perky", "sprightly", "sugary", "cartoonish", "child-like", "naive", "campy", "extroverted", "sparkling", "sparkly", "spontaneous", "swinging"]),
        ("Energetic & Intense", ["energetic", "aggressive", "angry", "fiery", "intense", "powerful", "rousing", "driving", "boisterous", "rowdy", "hostile", "confrontational", "volatile", "visceral", "brash", "gritty", "harsh", "kinetic", "explosive", "tense", "anxious", "urgent", "frantic", "manic", "rebellious", "defiant", "brassy", "outraged", "wild", "thrilling", "exciting", "bravado", "fierce", "raw", "rambunctious", "spicy", "athletic", "belligerent", "crunchy", "feral", "feverish", "hyper", "reckless", "savage", "turbulent", "uncompromising", "heavy", "pulsing", "motoric", "mechanical", "marching", "tough", "strong", "mighty", "gutsy", "outrageous", "trashy", "vulgar", "messy", "ramshackle", "greasy", "nervous", "jittery", "tight", "striding", "virile"]),
        ("Chill & Calm", ["calm", "relaxed", "laid-back", "laid back", "mellow", "soothing", "peaceful", "serene", "gentle", "tranquil", "soft", "quiet", "airy", "easygoing", "meditative", "pastoral", "lazy", "warm", "comforting", "cozy", "breezy", "delicate", "understated", "restrained", "organic", "rustic", "homespun", "innocent", "unhurried", "languid", "flowing", "graceful", "pure", "plain", "sparse", "reassuring", "consoling", "earthy", "lyrical", "melodic", "meandering", "benevolent", "transparent", "translucent", "refined", "reserved"]),
        ("Romantic & Sensual", ["romantic", "sensual", "sexy", "seductive", "passionate", "tender", "intimate", "lush", "affectionate", "yearning", "sultry", "sophisticated", "smooth", "slick", "steamy", "flirty", "longing", "devotional", "erotic", "sexual", "hedonistic", "indulgent", "opulent", "sleazy"]),
        ("Sad & Reflective", ["sad", "melancholy", "melancholic", "bittersweet", "wistful", "gloomy", "somber", "sombre", "sentimental", "anguished", "distraught", "lonely", "mournful", "regretful", "poignant", "angst-ridden", "angst", "plaintive", "nostalgic", "autumnal", "elegiac", "reflective", "introspective", "contemplative", "rueful", "weary", "resigned", "heartbroken", "vulnerable", "earnest", "sincere", "confessional", "reverent", "solemn", "spiritual", "tragic", "funereal", "serious", "thoughtful", "philosophical", "poetic", "narrative", "searching", "self-conscious", "wintry", "cathartic", "negative"]),
        ("Dark & Brooding", ["dark", "brooding", "ominous", "menacing", "sinister", "eerie", "spooky", "nocturnal", "mysterious", "paranoid", "bleak", "cold", "detached", "nihilistic", "apocalyptic", "gothic", "unsettling", "creepy", "fractured", "bitter", "cynical", "sardonic", "acerbic", "sarcastic", "malevolent", "claustrophobic", "suffocating", "desperate", "hungry", "tortured", "provocative", "ironic", "demonic", "scary", "suspenseful", "threatening", "grim", "severe", "austere", "brittle", "clinical", "insular", "snide", "wry", "satirical", "knotty", "difficult", "angular", "capricious", "eccentric", "scattered", "circular"]),
        ("Dreamy & Atmospheric", ["dreamy", "atmospheric", "ethereal", "hypnotic", "trippy", "psychedelic", "spacey", "spacious", "ambient", "cerebral", "otherworldly", "other-worldly", "surreal", "floating", "hazy", "mystical", "enigmatic", "elaborate", "complex", "intricate", "detailed", "literate", "precious", "ornate", "druggy", "narcotic", "exotic", "exploratory", "sprawling", "magical", "fantastic", "fantasy-like", "shimmering", "rhapsodic", "technical"]),
        ("Confident & Bold", ["confident", "stylish", "cool", "elegant", "swaggering", "sleek", "glamorous", "sassy", "flashy", "ambitious", "theatrical", "dramatic", "epic", "triumphant", "majestic", "grand", "anthemic", "stately", "regal", "proud", "cocky", "boastful", "street-smart", "hip", "trendy", "polished", "bombastic", "uplifting", "inspiring", "heroic", "positive", "determined", "stirring", "dignified", "noble", "monumental", "declamatory", "resolute", "cosmopolitan", "concise"]),
    ];

    public static IReadOnlyList<TagItem> Group(IReadOnlyList<TagItem> tags, TagKind kind)
    {
        if (Group(kind) is null) return tags;

        var families = new Dictionary<string, List<TagItem>>();
        var loose = new List<TagItem>();
        foreach (var tag in tags)
        {
            if (FamilyOf(tag.Title, kind) is { } family)
            {
                if (!families.TryGetValue(family, out var members)) families[family] = members = [];
                members.Add(tag);
            }
            else loose.Add(tag);
        }

        var result = new List<TagItem>();
        foreach (var (name, members) in families)
        {
            if (members.Count == 1)
            {
                result.Add(members[0]);
                continue;
            }
            var ordered = members.OrderByDescending(m => m.Count ?? 0).ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            result.Add(new TagItem
            {
                RatingKey = $"{kind}:family:{name}",
                Title = name,
                TagKind = kind,
                FilterValue = string.Join(',', ordered.Select(m => m.FilterValue)),
                Members = ordered,
            });
        }
        result.AddRange(loose);
        return result.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string? FamilyOf(string title, TagKind kind)
    {
        if (Group(kind) is not { } group) return null;
        if (_overrides.TryGetValue(OverrideKey(title, kind), out var chosen)) return chosen.Length == 0 ? null : chosen;
        return Automatic.GetOrAdd((group, title), static key => Match(key.Title, key.Group == "mood" ? MoodRules : GenreRules));
    }

    /// <summary>The family the rules choose, ignoring anything the listener moved.</summary>
    public static string? AutomaticFamilyOf(string title, TagKind kind) =>
        Group(kind) is { } group ? Automatic.GetOrAdd((group, title), static key => Match(key.Title, key.Group == "mood" ? MoodRules : GenreRules)) : null;

    /// <summary>Every family a tag of this kind can belong to, in name order.</summary>
    public static IReadOnlyList<string> Names(TagKind kind) => (Group(kind) switch
    {
        "genre" => Genres.Select(g => g.Name),
        "mood" => Moods.Select(m => m.Name),
        _ => [],
    }).Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    public static bool CanMove(TagKind kind) => Group(kind) is not null;

    // ---- The listener's own moves ----

    private static IReadOnlyDictionary<string, string> _overrides = new Dictionary<string, string>();

    /// <summary>Raised when the listener's moves change, so grouped views can regroup.</summary>
    public static event EventHandler? Changed;

    /// <summary>The key a move is saved under. A value of "" keeps the tag out of every family.</summary>
    public static string OverrideKey(string title, TagKind kind) => $"{Group(kind)}:{title.Trim().ToLowerInvariant()}";

    public static bool IsMoved(string title, TagKind kind) => _overrides.ContainsKey(OverrideKey(title, kind));

    public static void UseOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        _overrides = new Dictionary<string, string>(overrides ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static string? Group(TagKind kind) => kind switch
    {
        TagKind.Genre or TagKind.Style => "genre",
        TagKind.Mood => "mood",
        _ => null,
    };

    // ---- Matching ----

    private sealed record Keyword(List<string> Phrase, int Size, bool Wildcard);
    private sealed record Rule(string Name, int Context, Keyword[] Keywords);

    private static readonly Rule[] GenreRules = Compile(Genres, Context);
    private static readonly Rule[] MoodRules = Compile(Moods, []);
    private static readonly ConcurrentDictionary<(string Group, string Title), string?> Automatic = new();

    private static Rule[] Compile((string Name, string[] Words)[] families, string[] context) => families.Select(f => new Rule(
        f.Name,
        Array.IndexOf(context, f.Name) is var i and >= 0 ? context.Length - i : 0,
        f.Words.Select(w => new Keyword(Words(w), w.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, w.Contains('*'))).ToArray())).ToArray();

    private static string? Match(string title, Rule[] rules)
    {
        var words = Words(title);
        if (words.Count == 0) return null;
        var tokens = title.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var last = tokens.Length > 0 ? tokens[^1] : string.Empty;
        string? best = null;
        (int Whole, int Size, int Context, int Head, int Exact, int Order) bestScore = default;
        for (var order = 0; order < rules.Length; order++)
        {
            var rule = rules[order];
            foreach (var keyword in rule.Keywords)
            {
                if (Find(words, keyword) is not { } end) continue;
                var whole = !keyword.Wildcard && keyword.Phrase.SequenceEqual(words);
                var head = end == words.Count - 1 || words[end] == last;
                var score = (whole ? 1 : 0, keyword.Size, rule.Context, head ? 1 : 0, keyword.Wildcard ? 0 : 1, -order);
                if (best is null || score.CompareTo(bestScore) > 0)
                {
                    best = rule.Name;
                    bestScore = score;
                }
            }
        }
        return best;
    }


    private static List<string> Words(string text)
    {
        var words = new List<string>();
        foreach (var raw in text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add(raw);
            if (raw.Contains('-'))
                words.AddRange(raw.Split('-', StringSplitOptions.RemoveEmptyEntries));
        }
        return words;
    }

    // The index of the last word the keyword matched, or null.
    private static int? Find(List<string> words, Keyword keyword)
    {
        var phrase = keyword.Phrase;
        if (phrase.Count == 0) return null;
        if (keyword.Wildcard && phrase.Count == 1)
        {
            var stem = phrase[0].Trim('*');
            var start = phrase[0].EndsWith('*');
            var end = phrase[0].StartsWith('*');
            for (var i = words.Count - 1; i >= 0; i--)
            {
                var w = words[i];
                if (w.Length > stem.Length && (start && end ? w.Contains(stem) : start ? w.StartsWith(stem) : w.EndsWith(stem))) return i;
            }
            return null;
        }
        for (var i = words.Count - phrase.Count; i >= 0; i--)
        {
            var match = true;
            for (var j = 0; j < phrase.Count && match; j++) match = words[i + j] == phrase[j];
            if (match) return i + phrase.Count - 1;
        }
        return null;
    }
}
