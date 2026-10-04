using HumeMusic.Core.Models;

namespace HumeMusic.Core.Mixes;

/// <summary>A multi-dimensional slice of the listener's taste.</summary>
public sealed record NicheMixRecipe(string? Genre, string? Mood, int? Decade, double Support = 0)
{
    public const string RatingKeyPrefix = "mix:niche:";

    public int Dimensions => (Genre is null ? 0 : 1) + (Mood is null ? 0 : 1) + (Decade is null ? 0 : 1);

    public string Title
    {
        get
        {
            var parts = new List<string>(3);
            if (Mood is { Length: > 0 }) parts.Add(Mood);
            if (Decade is { } decade) parts.Add($"{decade}s");
            if (Genre is { Length: > 0 }) parts.Add(Genre);
            return string.Join(' ', parts) + " Mix";
        }
    }

    /// <summary>The stable card/playback object for this recipe.</summary>
    public NicheMix ToMix() => new()
    {
        RatingKey = RatingKeyPrefix
            + "g=" + Uri.EscapeDataString(Genre ?? string.Empty)
            + "&m=" + Uri.EscapeDataString(Mood ?? string.Empty)
            + "&d=" + (Decade?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
        Title = this.Title,
        Genre = Genre,
        Mood = Mood,
        Decade = Decade,
        Support = Support,
    };
}

/// <summary>
/// Builds niche concepts from combinations that actually co-occur in recent listening. It never
/// invents an adjective or crosses unrelated tags just to make a clever title.
/// </summary>
public static class NicheMixes
{
    private const int MinOccurrences = 2;
    private const double MinSupport = 0.30;
    private static readonly HashSet<string> NoiseMoods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArtistPopular",
    };

    private sealed record RecipeKey(string? Genre, string? Mood, int? Decade)
    {
        public int Dimensions => (Genre is null ? 0 : 1) + (Mood is null ? 0 : 1) + (Decade is null ? 0 : 1);
    }

    private sealed class RecipeKeyComparer : IEqualityComparer<RecipeKey>
    {
        public static readonly RecipeKeyComparer Instance = new();

        public bool Equals(RecipeKey? x, RecipeKey? y) =>
            ReferenceEquals(x, y) || x is not null && y is not null
            && x.Decade == y.Decade
            && string.Equals(x.Genre, y.Genre, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Mood, y.Mood, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(RecipeKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Genre, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Mood, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Decade);
            return hash.ToHashCode();
        }
    }

    private sealed class Evidence
    {
        public double Weight { get; set; }
        public HashSet<string> Tracks { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Artists { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the strongest grounded recipes, with light diversity limits so one dominant tag does
    /// not fill the whole row. If the listener only has a narrow taste, a second pass fills the row.
    /// </summary>
    public static IReadOnlyList<NicheMixRecipe> Generate(IEnumerable<Track> recent, DateTimeOffset now, int count = 10)
    {
        if (count <= 0) return [];
        var evidence = new Dictionary<RecipeKey, Evidence>(RecipeKeyComparer.Instance);

        foreach (var track in recent)
        {
            var genres = Clean(track.Genres).ToList();
            var moods = Clean(track.Moods).Where(m => !NoiseMoods.Contains(m)).ToList();
            var decade = track.Year is >= 1900 and < 2100 ? track.Year.Value / 10 * 10 : (int?)null;
            if (genres.Count == 0 && moods.Count == 0) continue;

            var weight = ListeningProfile.Weight(track.LastViewedAt, now)
                * (1 + Math.Min(0.8, Math.Log2(Math.Max(1, track.ViewCount) + 1) * 0.12));
            var artist = track.ArtistRatingKey ?? track.ArtistTitle ?? track.RatingKey;

            foreach (var genre in genres)
            {
                foreach (var mood in moods)
                {
                    // A tag appearing in both fields is one idea, not a useful two-dimensional niche.
                    if (string.Equals(genre, mood, StringComparison.OrdinalIgnoreCase)) continue;
                    Add(new RecipeKey(genre, mood, null), track.RatingKey, artist, weight);
                    if (decade is { } d) Add(new RecipeKey(genre, mood, d), track.RatingKey, artist, weight);
                }
                if (decade is { } d2) Add(new RecipeKey(genre, null, d2), track.RatingKey, artist, weight);
            }
            if (decade is { } d3)
                foreach (var mood in moods)
                    Add(new RecipeKey(null, mood, d3), track.RatingKey, artist, weight);
        }

        var ranked = evidence
            .Where(x => x.Key.Dimensions >= 2
                        && x.Value.Tracks.Count >= MinOccurrences
                        && x.Value.Artists.Count >= 2
                        && x.Value.Weight >= MinSupport)
            .Select(x => new NicheMixRecipe(x.Key.Genre, x.Key.Mood, x.Key.Decade,
                x.Value.Weight * (1 + 0.14 * (x.Key.Dimensions - 2))
                + Math.Min(0.5, x.Value.Tracks.Count * 0.035)
                + Math.Min(0.35, x.Value.Artists.Count * 0.04)))
            .OrderByDescending(x => x.Support)
            .ThenByDescending(x => x.Dimensions)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = new List<NicheMixRecipe>(count);
        foreach (var candidate in ranked)
        {
            if (selected.Count >= count) break;
            if (TooMuchOfSame(candidate, selected)) continue;
            selected.Add(candidate);
        }
        foreach (var candidate in ranked)
        {
            if (selected.Count >= count) break;
            if (!selected.Contains(candidate)) selected.Add(candidate);
        }
        return selected;

        void Add(RecipeKey key, string trackKey, string artistKey, double weight)
        {
            if (!evidence.TryGetValue(key, out var item)) evidence[key] = item = new Evidence();
            // Each track contributes once to a recipe even if Plex returned duplicate tag text.
            if (!item.Tracks.Add(trackKey)) return;
            item.Weight += weight;
            item.Artists.Add(artistKey);
        }
    }

    private static bool TooMuchOfSame(NicheMixRecipe candidate, IReadOnlyList<NicheMixRecipe> selected)
    {
        if (candidate.Genre is { } genre && selected.Count(x => string.Equals(x.Genre, genre, StringComparison.OrdinalIgnoreCase)) >= 2) return true;
        if (candidate.Mood is { } mood && selected.Count(x => string.Equals(x.Mood, mood, StringComparison.OrdinalIgnoreCase)) >= 2) return true;
        if (candidate.Decade is { } decade && selected.Count(x => x.Decade == decade) >= 2) return true;
        return false;
    }

    private static IEnumerable<string> Clean(IEnumerable<string> tags) =>
        tags.Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
