using System.Globalization;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Meloday;

/// <summary>The name and description of a Meloday, and what they were drawn from.</summary>
public sealed record MelodayName(
    string Title,
    string Description,
    string DominantMood,
    string Descriptor,
    string DominantGenre,
    string? EraLabel,
    string NextUpdate,
    IReadOnlyList<(string Genre, double Share)> TopGenres,
    IReadOnlyList<(string Mood, double Share)> TopMoods);

/// <summary>
/// Names a Meloday from its tracks, as the script does: "Meloday for {descriptor} {genre} {day}
/// {period}", with a descriptor from the mood map and an era nickname when one decade dominates,
/// and a description like "A dreamy, breezy mix of Pop and R&amp;B for your evening."
/// </summary>
public static class MelodayNaming
{
    public const string Prefix = "Meloday for ";

    public static MelodayName Name(MelodayPeriod period, IReadOnlyList<Track> tracks, Func<Track, AlbumTags?> album,
        IReadOnlyDictionary<string, string[]> moodMap, IReadOnlyDictionary<string, string[]> eraMap,
        Random random, DateTime now, string listener)
    {
        var genreWeight = new Dictionary<string, double>(StringComparer.Ordinal);
        var moodWeight = new Dictionary<string, double>(StringComparer.Ordinal);
        var yearWeight = new Dictionary<int, double>();
        var total = 0.0;
        foreach (var t in tracks)
        {
            // Longer songs weigh more, up to six minutes.
            var score = Math.Min(t.DurationMs, 360_000) / 60_000.0;
            var tags = album(t);
            foreach (var g in tags?.Genres ?? (IEnumerable<string>)t.Genres) MelodayProfile.Add(genreWeight, g, score);
            foreach (var m in tags?.Moods ?? (IEnumerable<string>)t.Moods) MelodayProfile.Add(moodWeight, m, score);
            if ((tags?.Year ?? t.Year) is int year && year > 1900 && year <= now.Year) yearWeight[year] = yearWeight.GetValueOrDefault(year) + score;
            total += score;
        }

        List<string> Above(Dictionary<string, double> weights) =>
            weights.Where(w => w.Value >= 0.03 * total).OrderByDescending(w => w.Value).Select(w => w.Key).ToList();
        var topGenres = Above(genreWeight);
        var topMoods = Above(moodWeight);

        var dominantGenre = topGenres.FirstOrDefault() ?? "Eclectic";
        var dominantMood = topMoods.FirstOrDefault() ?? "Vibes";
        var descriptors = moodMap.TryGetValue(dominantMood, out var d) && d.Length > 0 ? d : ["Vibrant"];
        var descriptor = descriptors[random.Next(descriptors.Length)];

        var eraWeight = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (year, w) in yearWeight) MelodayProfile.Add(eraWeight, $"{year / 10 * 10}s", w);
        var era = MelodayProfile.Top(eraWeight);
        var eraOptions = era is null ? [] : eraMap.TryGetValue(era, out var nicknames) && nicknames.Length > 0 ? nicknames : [era];
        var eraLabel = eraOptions.Length > 0 ? eraOptions[random.Next(eraOptions.Length)] : null;
        var eraRatio = era is not null && total > 0 ? eraWeight[era] / total : 0;
        var includeEra = eraLabel is not null && eraRatio >= 0.65;

        var candidates = new List<string>();
        if (includeEra) candidates.Add($"{Prefix}{eraLabel} {dominantGenre}");
        candidates.Add($"{Prefix}{descriptor} {dominantGenre}");
        candidates.Add($"{Prefix}{dominantMood} {dominantGenre}");
        candidates.Add($"{Prefix}{descriptor} {dominantMood} {dominantGenre}");
        if (includeEra) candidates.Add($"{Prefix}{descriptor} {eraLabel} {dominantGenre}");

        var day = now.ToString("dddd", CultureInfo.InvariantCulture);
        var suffix = $"{day} {period.Name}";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var title = candidates
            .Where(c =>
            {
                var phrase = c.ToLowerInvariant().Replace("meloday for", string.Empty).Trim();
                return phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6 && seen.Add(phrase);
            })
            .Select(c => $"{c} {suffix}")
            .FirstOrDefault() ?? $"{Prefix}{descriptor} {dominantGenre} {suffix}";

        // "A dreamy, breezy mix of Pop and R&B for your evening.": the title's descriptor, then one
        // more from the second mood (or the same mood), the two top genres and the period.
        var words = new List<string> { descriptor };
        var secondMood = topMoods.Count > 1 ? topMoods[1] : null;
        var secondPool = moodMap.TryGetValue(secondMood ?? dominantMood, out var pool) ? pool : [];
        var alternatives = secondPool.Where(w => !w.Equals(descriptor, StringComparison.OrdinalIgnoreCase)).ToList();
        if (alternatives.Count > 0) words.Add(alternatives[random.Next(alternatives.Count)]);
        var descPhrase = words.Count >= 2 ? $"{words[0].ToLowerInvariant()}, {words[1].ToLowerInvariant()}" : words[0].ToLowerInvariant();

        var genrePair = (topGenres.Count > 0 ? topGenres : [dominantGenre]).Take(2).ToList();
        var genrePhrase = genrePair.Count >= 2 ? $"{genrePair[0]} and {genrePair[1]}" : genrePair[0];
        var article = descPhrase.Length > 0 && "aeiou".Contains(descPhrase[0]) ? "An" : "A";
        var description = $"{article} {descPhrase} mix of {genrePhrase} for your {period.Name.ToLowerInvariant()}.";

        // A few more of its tags, not already named.
        var highlight = topGenres.Skip(2).Concat(topMoods)
            .Where(s => s != dominantGenre && s != dominantMood && !genrePair.Contains(s))
            .Distinct()
            .Take(3)
            .ToList();
        if (highlight.Count > 1) description += $" Here's some {string.Join(", ", highlight.Take(highlight.Count - 1))}, and {highlight[^1]} tracks as well.";
        else if (highlight.Count == 1) description += $" Here's some {highlight[0]} tracks as well.";

        var nextUpdate = period.NextChange(now).ToString("h:mm tt", CultureInfo.InvariantCulture);
        description += $"\n\nMade for {listener} • Next update at {nextUpdate}.";

        IReadOnlyList<(string, double)> Shares(Dictionary<string, double> weights) =>
            weights.OrderByDescending(w => w.Value).Take(5).Select(w => (w.Key, total > 0 ? w.Value / total : 0)).ToList();
        return new MelodayName(title, description, dominantMood, descriptor, dominantGenre, eraLabel, nextUpdate,
            Shares(genreWeight), Shares(moodWeight));
    }

    /// <summary>The script's prompt for an AI title in the style of a daylist.</summary>
    public static string AiPrompt(MelodayName name, MelodayPeriod period, DateTime now, IReadOnlyCollection<string> avoidWords)
    {
        var banned = avoidWords.Count > 0 ? $"Never use any of these words: {string.Join(", ", avoidWords)}." : string.Empty;
        return $"""
            You are naming a playlist in the style of Spotify Daylist.
            Titles are playful, layered, and inspired by subcultures, aesthetics, music styles, and timestamps.

            Mood: {name.DominantMood}
            Genre: {name.DominantGenre}
            Descriptor: {name.Descriptor}
            Era: {name.EraLabel ?? "modern"}
            Day: {now.ToString("dddd", CultureInfo.InvariantCulture)}
            Period: {period.Name}

            Listening trends centre on {name.DominantGenre} and {name.DominantMood}. The playlist reflects recent plays and adjacent sonic exploration.

            Rules:
            Start with exactly: Meloday for
            Use no more than 6 words total after "Meloday for"
            Do NOT include days of the week or time words like morning, afternoon, evening, night
            Avoid basic genre words and the word music
            Title should make grammatical sense
            {banned}
            Return a single title only
            """;
    }

    /// <summary>
    /// The full title from an AI answer ("Meloday for ..." plus the day and period), or null when the
    /// answer isn't a usable title (it must start "Meloday for" and stay short).
    /// </summary>
    public static string? FromAi(string? answer, MelodayPeriod period, DateTime now, IReadOnlyCollection<string> avoidWords)
    {
        var text = answer?.Trim().Trim('"', '“', '”').Trim();
        if (string.IsNullOrEmpty(text) || !text.StartsWith("Meloday for", StringComparison.OrdinalIgnoreCase) || text.Contains('\n')) return null;
        var words = text[11..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 6) return null;
        if (avoidWords.Any(w => words.Any(x => x.Trim(',', '.', '!', '?').Equals(w, StringComparison.OrdinalIgnoreCase)))) return null;
        return $"{Prefix}{string.Join(' ', words)} {now.ToString("dddd", CultureInfo.InvariantCulture)} {period.Name}";
    }
}
