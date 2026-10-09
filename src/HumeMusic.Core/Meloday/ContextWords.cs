using System.Text.RegularExpressions;

namespace HumeMusic.Core.Meloday;

/// <summary>
/// The words and phrases that say a playlist belongs to a time of day or a season, so "Morning Mix", "Waking Up" or "Evening Pop
/// Mix" are offered on the time-of-day shelf, and "Summer Vibes" or "Snow Day" on the seasonal one, whatever the listening
/// history says. A playlist is matched by its name (whole words or phrases, never a part of a word: "Mourning" is not "morning").
/// </summary>
public static partial class ContextWords
{
    // Keyed by the names of MelodayPeriods. Each holds the period's own name and what people call playlists for it.
    private static readonly Dictionary<string, string[]> Periods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dawn"] = ["dawn", "sunrise", "daybreak", "first light", "waking up", "wake up", "wakeup", "rise and shine", "rise & shine", "good morning", "early bird", "early morning", "morning"],
        ["Early Morning"] = ["early morning", "morning", "waking up", "wake up", "wakeup", "rise and shine", "rise & shine", "good morning", "sunrise", "breakfast", "coffee", "start your day", "start the day", "morning routine", "morning commute", "early bird", "sunday morning"],
        ["Morning"] = ["morning", "good morning", "waking up", "wake up", "wakeup", "rise and shine", "rise & shine", "breakfast", "brunch", "coffee", "start your day", "start the day", "morning routine", "morning commute", "morning coffee", "sunday morning", "a.m.", "am playlist"],
        ["Afternoon"] = ["afternoon", "midday", "mid-day", "lunch", "lunchtime", "daytime", "sunny", "golden afternoon", "sunday afternoon", "work day", "workday", "focus", "study", "productive", "pick me up", "pick-me-up"],
        ["Evening"] = ["evening", "sunset", "golden hour", "dinner", "dinner party", "wind down", "winding down", "unwind", "after work", "twilight", "dusk", "cocktail", "cocktails", "happy hour", "chill", "chilled", "relax", "relaxing", "mellow", "pregame", "pre-game", "pre game", "getting ready", "sunday evening", "tonight", "this evening"],
        ["Night"] = ["night", "nighttime", "tonight", "night out", "night drive", "going out", "party", "club", "after dark", "evening", "nightlife", "pregame", "pre-game", "pre game", "getting ready", "saturday night", "friday night", "midnight"],
        ["Late Night"] = ["late night", "late-night", "midnight", "after dark", "after hours", "2am", "3am", "1am", "insomnia", "can't sleep", "cant sleep", "sleep", "sleepy", "bedtime", "lullaby", "night owl", "night drive", "dreamy", "nocturnal", "nighttime", "night"],
    };

    private static readonly Dictionary<string, string[]> Seasons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Winter"] = ["winter", "snow", "snowy", "snow day", "cold", "frost", "frosty", "cozy", "cosy", "fireplace", "hot chocolate", "christmas", "xmas", "holiday", "holidays", "festive", "december", "january", "february", "new year", "new year's", "nye", "hibernate", "ski"],
        ["Spring"] = ["spring", "springtime", "bloom", "blossom", "blossoms", "fresh start", "april", "easter", "spring showers", "garden", "spring cleaning", "renewal"],
        ["Summer"] = ["summer", "summertime", "beach", "pool", "poolside", "sunshine", "sunny", "road trip", "roadtrip", "bbq", "barbecue", "vacation", "heatwave", "heat wave", "tropical", "swim", "festival", "june", "july", "august", "cookout", "boat", "sunscreen", "summer vibes"],
        ["Autumn"] = ["autumn", "fall vibes", "fall mix", "fall playlist", "fall favorites", "fall favourites", "fall season", "halloween", "spooky", "october", "september", "november", "harvest", "thanksgiving", "pumpkin", "cozy", "cosy", "rainy", "rainy day", "leaves", "back to school", "sweater weather", "cider"],
    };

    /// <summary>The words that mark a playlist for this part of the day (by <see cref="MelodayPeriods"/> name); none for a name that isn't one.</summary>
    public static IReadOnlyList<string> ForPeriod(string period) => Periods.TryGetValue(period, out var words) ? words : [];

    /// <summary>The words that mark a playlist for this season ("Winter", "Spring", "Summer", "Autumn").</summary>
    public static IReadOnlyList<string> ForSeason(string season) => Seasons.TryGetValue(season, out var words) ? words : [];

    /// <summary>Whether a playlist's name holds any of the words or phrases as whole words ("Morning Mix", "my waking-up mix").</summary>
    public static bool Names(string? title, IEnumerable<string> words)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var text = " " + Separators().Replace(title.ToLowerInvariant().Replace('’', '\''), " ").Trim() + " ";
        foreach (var word in words)
        {
            var phrase = " " + Separators().Replace(word.ToLowerInvariant(), " ").Trim() + " ";
            if (text.Contains(phrase, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // Punctuation and hyphens count as spaces, apostrophes are dropped ("can't" and "cant" meet); "&" and "and" are the same.
    [GeneratedRegex(@"[\s\-_/.,:;!?()\[\]""|+*~]+")]
    private static partial Regex Separators();
}
