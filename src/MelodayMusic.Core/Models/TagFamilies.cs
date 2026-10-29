namespace MelodayMusic.Core.Models;

public static class TagFamilies
{
    private static readonly (string Name, string[] Words)[] Genres =
    [
        ("Hip-Hop", ["hip hop", "hip-hop", "hiphop", "rap", "trap", "drill", "grime", "boom bap", "crunk", "phonk", "*hop", "hyphy", "pluggnb", "r&drill", "new orleans bounce", "afroswing", "bounce", "dirty south", "turntablism"]),
        ("R&B & Soul", ["r&b", "rnb", "r and b", "soul", "funk", "motown", "new jack swing", "quiet storm", "doo wop", "urban"]),
        ("Alternative", ["alternative", "indie", "alt", "shoegaze", "dream pop", "post-punk", "new wave", "lo-fi", "lofi", "emo", "*gaze", "slowcore", "austindie", "darkwave", "cold wave", "coldwave", "escape room", "bubblegrunge", "beatlesque", "mellow gold", "neo mellow", "southern gothic", "indie*"]),
        ("Latin", ["latin", "reggaeton", "salsa", "bachata", "cumbia", "urbano", "samba", "tango", "bossa nova", "mpb", "corrido", "corridos", "banda", "norteño", "mariachi", "merengue", "tropical", "cha cha cha", "mambo", "chihuahuense", "sierreño", "chicha", "kizomba", "cuban", "plena", "puerto rican", "tropicalia"]),
        ("Pop", ["pop", "k-pop", "j-pop", "c-pop", "teen", "boy band", "girl group", "*pop", "pop*", "idol", "kayokyoku", "shibuya-kei", "denpa-kei", "variété", "talent show", "europop", "adult contemporary", "girl groups", "celebrity"]),
        ("Metal", ["metal", "metalcore", "deathcore", "djent", "doom", "sludge"]),
        ("Rock", ["rock", "punk", "grunge", "hardcore", "garage", "psychedelic", "surf", "rockabilly", "britpop", "aor", "psychobilly", "psychedelia", "jam bands", "*punk", "american underground"]),
        ("Electronic", ["electronic", "electronica", "edm", "house", "techno", "trance", "dubstep", "drum and bass", "drum & bass", "dnb", "jungle", "breakbeat", "breaks", "dance", "disco", "electro", "synth", "synthwave", "idm", "ambient", "downtempo", "trip hop", "trip-hop", "chillwave", "hardstyle", "bass", "uk garage", "club", "eurodance", "hi-nrg", "big room", "future", "*tronica", "*step", "*core", "*techno", "*trance", "electro*", "big beat", "bassline", "balearic", "complextro", "ghettotech", "moombahton", "melbourne bounce", "new rave", "new french touch", "vaporwave", "vapor", "chill out", "chillout", "beats", "chillsynth", "cyberpunk", "pixel", "uk funky", "ukg", "krushclub", "groove room", "high vibe", "hopebeat", "bossbeat", "slowed", "ballroom", "*wave", "*synth*", "broken beat", "downbeat", "rave"]),
        ("Jazz", ["jazz", "swing", "bebop", "bop", "big band", "fusion", "*jazz", "improvisation", "modern creative", "modern free"]),
        ("Blues", ["blues"]),
        ("Country", ["country", "americana", "bluegrass", "honky tonk", "outlaw", "western", "red dirt"]),
        ("Folk", ["folk", "singer-songwriter", "singer/songwriter", "acoustic", "celtic", "folk*"]),
        ("Reggae", ["reggae", "dancehall", "ska", "dub", "rocksteady", "ragga"]),
        ("Classical", ["classical", "orchestral", "opera", "baroque", "chamber", "symphony", "choral", "romantic", "contemporary classical", "minimalism", "piano", "neoclassical", "modern composition", "contemporary instrumental"]),
        ("Soundtrack", ["soundtrack", "soundtracks", "score", "film", "musical", "musicals", "show tunes", "video game", "tv", "cast recordings"]),
        ("Gospel & Christian", ["gospel", "christian", "worship", "ccm", "spiritual", "inspirational"]),
        ("World", ["world", "african", "afrobeat", "afrobeats", "afropop", "amapiano", "highlife", "bollywood", "indian", "arabic", "flamenco", "fado", "k", "j", "brazilian", "caribbean", "calypso", "soca", "zouk", "afro", "afro*", "alte", "alté", "azeri", "chutney", "qawwali", "mizrahi", "malay", "vietnamese", "hiplife", "chanson", "french", "traditions", "congolese", "ghanaian", "nigerian", "gypsy", "middle eastern", "turkish", "worldbeat"]),
        ("Easy Listening", ["easy listening", "standards", "adult standards", "lounge", "vocal", "exotica", "crooner", "cabaret", "new age", "a cappella", "acappella"]),
        ("Holiday", ["christmas", "holiday", "holidays"]),
        ("Kids & Family", ["children", "children's", "kids", "family"]),
        ("Comedy & Spoken", ["comedy", "spoken", "spoken word", "audiobook", "poetry"]),
        ("Experimental", ["experimental", "avant-garde", "avant", "noise", "industrial", "musique concrete", "sound art"]),
    ];

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
        var rules = kind switch
        {
            TagKind.Genre or TagKind.Style => Genres,
            TagKind.Mood => Moods,
            _ => null,
        };
        if (rules is null) return tags;

        var families = new Dictionary<string, List<TagItem>>();
        var loose = new List<TagItem>();
        foreach (var tag in tags)
        {
            if (FamilyOf(tag.Title, rules) is { } family)
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

    public static string? FamilyOf(string title, TagKind kind) => kind switch
    {
        TagKind.Genre or TagKind.Style => FamilyOf(title, Genres),
        TagKind.Mood => FamilyOf(title, Moods),
        _ => null,
    };

    private static string? FamilyOf(string title, (string Name, string[] Words)[] rules)
    {
        var words = Words(title);
        foreach (var (name, keywords) in rules)
            foreach (var keyword in keywords)
                if (Contains(words, Words(keyword))) return name;
        return null;
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        foreach (var raw in text.ToLowerInvariant().Split([' ', '/', ',', '(', ')', '.', ':', ';', '+'], StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add(raw);
            if (raw.Contains('-'))
                words.AddRange(raw.Split('-', StringSplitOptions.RemoveEmptyEntries));
        }
        return words;
    }

    private static bool Contains(List<string> words, List<string> phrase)
    {
        if (phrase.Count == 0) return false;
        if (phrase.Count == 1 && phrase[0].Contains('*'))
        {
            var stem = phrase[0].Trim('*');
            var start = phrase[0].EndsWith('*');
            var end = phrase[0].StartsWith('*');
            return words.Any(w => w.Length > stem.Length && (start && end ? w.Contains(stem) : start ? w.StartsWith(stem) : w.EndsWith(stem)));
        }
        for (var i = 0; i + phrase.Count <= words.Count; i++)
        {
            var match = true;
            for (var j = 0; j < phrase.Count && match; j++) match = words[i + j] == phrase[j];
            if (match) return true;
        }
        return false;
    }
}
