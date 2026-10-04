using System.Globalization;
using System.Text;

namespace HumeMusic.Core.Playlists;

/// <summary>What a smart playlist rule looks at, and how its value is written.</summary>
public enum SmartFieldKind
{
    Text,
    Number,
    Rating,
    Tag,
    Date,
}

/// <summary>A field a smart playlist rule can test ("Artist", "Plays"...), and Plex's name for it.</summary>
public sealed record SmartField(string Key, string Label, SmartFieldKind Kind, string? TagDirectory = null)
{
    public static readonly IReadOnlyList<SmartField> All =
    [
        new("track.title", "Title", SmartFieldKind.Text),
        new("artist.title", "Artist", SmartFieldKind.Text),
        new("album.title", "Album", SmartFieldKind.Text),
        new("album.genre", "Genre", SmartFieldKind.Tag, "genre"),
        new("album.style", "Style", SmartFieldKind.Tag, "style"),
        new("track.mood", "Mood", SmartFieldKind.Tag, "mood"),
        new("album.year", "Year", SmartFieldKind.Number),
        new("album.decade", "Decade", SmartFieldKind.Number),
        new("track.userRating", "Rating", SmartFieldKind.Rating),
        new("track.viewCount", "Plays", SmartFieldKind.Number),
        new("track.skipCount", "Skips", SmartFieldKind.Number),
        new("track.lastViewedAt", "Last Played", SmartFieldKind.Date),
        new("track.addedAt", "Date Added", SmartFieldKind.Date),
    ];

    public static SmartField? Find(string key) => All.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        // Plex also accepts these without their level.
        ?? key switch
        {
            "genre" => All.First(f => f.Key == "album.genre"),
            "mood" => All.First(f => f.Key == "track.mood"),
            _ => null,
        };

    /// <summary>The comparisons this field offers, as (Plex operator, label).</summary>
    public IReadOnlyList<SmartOperator> Operators => Kind switch
    {
        SmartFieldKind.Text => SmartOperator.Text,
        SmartFieldKind.Tag => SmartOperator.Tag,
        SmartFieldKind.Date => SmartOperator.Date,
        _ => SmartOperator.Number,
    };
}

/// <summary>A comparison: Plex's operator and how it reads ("contains", "is greater than").</summary>
public sealed record SmartOperator(string Symbol, string Label)
{
    public static readonly IReadOnlyList<SmartOperator> Text = [new("=", "contains"), new("!=", "does not contain"), new("<=", "begins with"), new(">=", "ends with")];
    public static readonly IReadOnlyList<SmartOperator> Tag = [new("=", "is"), new("!=", "is not")];
    public static readonly IReadOnlyList<SmartOperator> Number = [new("=", "is"), new("!=", "is not"), new(">>=", "is greater than"), new("<<=", "is less than")];
    public static readonly IReadOnlyList<SmartOperator> Date = [new(">>=", "is in the last"), new("<<=", "is not in the last")];
}

/// <summary>
/// One rule of a smart playlist: a field, a comparison and a value ("Plays is greater than 5"), or a
/// rule Meloday Music can't show as one (a group of alternatives made elsewhere), kept exactly as is.
/// </summary>
public sealed record SmartRule(string Field, string Operator, string Value, string? Raw = null)
{
    public bool IsRaw => Raw is not null;

    public static SmartRule Opaque(string raw) => new(string.Empty, string.Empty, string.Empty, raw);

    /// <summary>The rule as Plex writes it ("track.viewCount>>=5"), its value escaped.</summary>
    public string ToQuery() => Raw ?? Field + Operator + Uri.EscapeDataString(Value);

    /// <summary>A description for people ("Artist rating is not 1 star, or Artist rating is unrated").</summary>
    public string Describe()
    {
        if (Raw is null) return Phrase(Field, Operator, Value);
        var parts = SmartFilter.Tokens(Raw)
            .Where(t => t.Key is not ("push" or "pop" or "or" or "and"))
            .Select(t => Phrase(t.Key, t.Op, t.Value));
        return string.Join(Raw.Contains("or=1", StringComparison.Ordinal) ? ", or " : ", and ", parts);
    }

    private static string Phrase(string key, string op, string value)
    {
        var field = SmartField.Find(key);
        var label = field?.Label ?? key switch
        {
            "artist.userRating" => "Artist rating",
            "album.userRating" => "Album rating",
            _ => key,
        };
        var rating = key.EndsWith("userRating", StringComparison.Ordinal);
        var kind = rating ? SmartFieldKind.Rating : field?.Kind ?? SmartFieldKind.Text;
        var ops = kind switch
        {
            SmartFieldKind.Text => SmartOperator.Text,
            SmartFieldKind.Date => SmartOperator.Date,
            _ => SmartOperator.Number,
        };
        var opLabel = ops.FirstOrDefault(o => o.Symbol == op)?.Label ?? op;
        string shown;
        if (rating)
        {
            if (value == "-1") return $"{label} is {(op == "!=" ? "rated" : "unrated")}";
            var stars = SmartFilter.Stars(value);
            shown = stars == 1 ? "1 star" : $"{stars} stars";
        }
        else if (kind == SmartFieldKind.Date)
        {
            var (amount, unit) = SmartFilter.ParseAge(value);
            var word = unit switch { "w" => "week", "m" => "month", "y" => "year", _ => "day" };
            shown = amount == 1 ? $"1 {word}" : $"{amount} {word}s";
        }
        else shown = kind == SmartFieldKind.Text ? $"“{value}”" : value;
        return $"{label} {opLabel} {shown}";
    }
}

/// <summary>
/// A smart playlist's rules, read from and written to the filter query Plex keeps for it
/// ("type=10&amp;sort=random&amp;limit=50&amp;artist.title=Kylie..."). Rules joined by "or" (inside a
/// push/pop group) make a playlist that matches any of them; a group nested among other rules is
/// kept whole as a single opaque rule, so editing never changes what it doesn't show.
/// </summary>
public sealed class SmartFilter
{
    public bool MatchAll { get; set; } = true;
    public List<SmartRule> Rules { get; } = [];
    /// <summary>Plex's sort ("random", "viewCount:desc"...), or null for the library's order.</summary>
    public string? Sort { get; set; }
    public int? Limit { get; set; }
    /// <summary>Parameters kept as they were (group=, and anything unknown).</summary>
    public List<string> Extra { get; } = [];

    /// <summary>The sorts on offer, as (Plex's sort, label).</summary>
    public static readonly IReadOnlyList<(string? Sort, string Label)> Sorts =
    [
        ("random", "Random"),
        ("titleSort", "Title"),
        ("artist.titleSort,album.titleSort,track.index", "Artist"),
        ("viewCount:desc", "Most Played"),
        ("lastViewedAt:desc", "Recently Played"),
        ("addedAt:desc", "Recently Added"),
        ("userRating:desc", "Highest Rated"),
    ];

    public static SmartFilter Parse(string query)
    {
        var filter = new SmartFilter();
        var tokens = Tokens(query).ToList();
        var top = new List<(string Text, bool Group, bool HasOr)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t.Key)
            {
                case "type":
                    continue;
                case "sort":
                    filter.Sort = t.Value;
                    continue;
                case "limit":
                    filter.Limit = int.TryParse(t.Value, out var l) ? l : null;
                    continue;
                case "group":
                    filter.Extra.Add(t.Text);
                    continue;
                case "push":
                {
                    // A group: everything to its matching pop.
                    var depth = 0;
                    var start = i;
                    var hasOr = false;
                    for (; i < tokens.Count; i++)
                    {
                        if (tokens[i].Key == "push") depth++;
                        else if (tokens[i].Key == "pop" && --depth == 0) break;
                        else if (tokens[i].Key == "or" && depth == 1) hasOr = true;
                    }
                    top.Add((string.Join("&", tokens.Skip(start).Take(i - start + 1).Select(x => x.Text)), true, hasOr));
                    continue;
                }
                case "or":
                case "and":
                case "pop":
                    continue;
                default:
                    top.Add((t.Text, false, false));
                    continue;
            }
        }

        // One group of simple alternatives and nothing else: a playlist matching any of its rules.
        if (top.Count == 1 && top[0].Group && top[0].HasOr)
        {
            var inner = Tokens(top[0].Text).ToList();
            var simple = inner.Skip(1).Take(inner.Count - 2).ToList();
            if (simple.All(x => x.Key is "or" || (x.Key is not ("push" or "pop" or "and") && SmartField.Find(x.Key) is not null)))
            {
                filter.MatchAll = false;
                foreach (var x in simple.Where(x => x.Key != "or")) filter.Rules.Add(RuleFor(x));
                return filter;
            }
        }
        foreach (var (text, group, _) in top)
        {
            if (group) filter.Rules.Add(SmartRule.Opaque(text));
            else
            {
                var token = Tokens(text).First();
                filter.Rules.Add(SmartField.Find(token.Key) is null ? SmartRule.Opaque(text) : RuleFor(token));
            }
        }
        return filter;
    }

    private static SmartRule RuleFor((string Key, string Op, string Value, string Text) t) =>
        new(SmartField.Find(t.Key)!.Key, t.Op, t.Value);

    /// <summary>The query Plex keeps for these rules (for a section's tracks).</summary>
    public string ToQuery()
    {
        var parts = new List<string> { "type=10" };
        if (Sort is { Length: > 0 }) parts.Add("sort=" + Sort);
        if (Limit is > 0) parts.Add("limit=" + Limit.Value.ToString(CultureInfo.InvariantCulture));
        parts.AddRange(Extra);
        var rules = Rules.Where(r => r.IsRaw || (r.Field.Length > 0 && r.Value.Length > 0)).ToList();
        if (MatchAll || rules.Count < 2) parts.AddRange(rules.Select(r => r.ToQuery()));
        else
        {
            var any = new StringBuilder("push=1");
            for (var i = 0; i < rules.Count; i++)
            {
                if (i > 0) any.Append("&or=1");
                any.Append('&').Append(rules[i].ToQuery());
            }
            any.Append("&pop=1");
            parts.Add(any.ToString());
        }
        return string.Join("&", parts);
    }

    /// <summary>
    /// A filter query's parameters: key, operator and (unescaped) value, and the text as written.
    /// Plex's operators are =, !=, ==, !==, &lt;=, &gt;=, &lt;&lt;=, &gt;&gt;=.
    /// </summary>
    public static IEnumerable<(string Key, string Op, string Value, string Text)> Tokens(string query)
    {
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOfAny(['!', '=', '<', '>']);
            if (at <= 0) continue;
            var end = at;
            while (end < part.Length && part[end] is '!' or '=' or '<' or '>') end++;
            yield return (Uri.UnescapeDataString(part[..at]), part[at..end], Uri.UnescapeDataString(part[end..].Replace('+', ' ')), part);
        }
    }

    // ------------------------------------------------------------------ values as people write them

    /// <summary>A date rule's value ("-30d") as an amount and unit (30, "d").</summary>
    public static (int Amount, string Unit) ParseAge(string value)
    {
        var v = value.TrimStart('-');
        var unit = v.Length > 0 && char.IsLetter(v[^1]) ? v[^1..] : "d";
        return (int.TryParse(v.TrimEnd('d', 'w', 'm', 'y'), out var n) ? n : 30, unit);
    }

    public static string Age(int amount, string unit) => "-" + amount.ToString(CultureInfo.InvariantCulture) + unit;

    /// <summary>Plex rates out of 10; people count stars out of 5.</summary>
    public static string RatingValue(int stars) => (stars * 2).ToString(CultureInfo.InvariantCulture);

    public static int Stars(string value) => int.TryParse(value, out var v) ? Math.Clamp((v + 1) / 2, 0, 5) : 0;
}
