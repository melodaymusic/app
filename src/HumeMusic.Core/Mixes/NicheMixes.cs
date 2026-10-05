using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Mixes;

/// <summary>A multi-dimensional slice of the listener's taste.</summary>
public sealed record NicheMixRecipe(
    string? Genre,
    string? Mood,
    int? Decade,
    double Support = 0,
    string? Context = null,
    NicheFocus Focus = NicheFocus.Blend,
    NicheEnergy Energy = NicheEnergy.Any,
    string? SeedTrackKey = null,
    string? SeedTrackTitle = null,
    IReadOnlyList<string>? ArtistNames = null)
{
    public const string RatingKeyPrefix = "mix:niche:";

    public IReadOnlyList<string> Artists => ArtistNames ?? [];

    public int Dimensions =>
        (Genre is null ? 0 : 1)
        + (Mood is null ? 0 : 1)
        + (Decade is null ? 0 : 1)
        + (Context is null ? 0 : 1)
        + (Focus == NicheFocus.Blend ? 0 : 1)
        + (Energy == NicheEnergy.Any ? 0 : 1)
        + (Artists.Count >= 2 ? 1 : 0);

    public string Title
    {
        get
        {
            if (Focus == NicheFocus.Sonic && SeedTrackTitle is { Length: > 0 } seed)
                return seed + " Neighbourhood Mix";
            if (Artists.Count >= 2)
                return string.Join(" + ", Artists.Take(2)) + " Mix";

            var parts = new List<string>(5);
            if (Context is { Length: > 0 }) parts.Add(Context);
            if (Energy == NicheEnergy.High) parts.Add("High Energy");
            else if (Energy == NicheEnergy.Low) parts.Add("Low-Key");
            else if (Energy == NicheEnergy.Medium) parts.Add("Mid-Energy");
            if (Mood is { Length: > 0 }) parts.Add(Mood);
            if (Decade is { } decade) parts.Add($"{decade}s");
            if (Genre is { Length: > 0 }) parts.Add(Genre);

            var stem = string.Join(' ', parts);
            if (stem.Length == 0) stem = "Personal";
            return Focus switch
            {
                NicheFocus.Lately => "Current " + stem + " Mix",
                NicheFocus.Rediscovery => stem + " Rediscovery Mix",
                NicheFocus.DeepCuts => stem + " Deep Cuts Mix",
                _ => stem + " Mix",
            };
        }
    }

    public string ColourSeed => Mood ?? Genre ?? Context ?? (Decade is { } decade ? $"{decade}s" : Title);

    /// <summary>The stable card/playback object for this recipe.</summary>
    public NicheMix ToMix() => new()
    {
        RatingKey = StableKey,
        Title = Title,
        TagKind = Mood is not null ? MixTagKind.Mood : MixTagKind.Genre,
        Tag = ColourSeed,
        Genre = Genre,
        Mood = Mood,
        Decade = Decade,
        Context = Context,
        Focus = Focus,
        Energy = Energy,
        SeedTrackKey = SeedTrackKey,
        SeedTrackTitle = SeedTrackTitle,
        ArtistNames = Artists,
        Support = Support,
    };

    public string StableKey => RatingKeyPrefix + string.Join('&', new[]
    {
        Pair("g", Genre),
        Pair("m", Mood),
        Pair("d", Decade?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        Pair("c", Context),
        Pair("f", Focus == NicheFocus.Blend ? null : Focus.ToString()),
        Pair("e", Energy == NicheEnergy.Any ? null : Energy.ToString()),
        Pair("s", SeedTrackKey),
        Pair("st", SeedTrackTitle),
        Pair("a", Artists.Count >= 2 ? string.Join('\u001F', Artists) : null),
    });

    private static string Pair(string key, string? value) => key + "=" + Uri.EscapeDataString(value ?? string.Empty);

    /// <summary>
    /// Restores the recipe from its stable key. This also works when a pin/reference rebuilds the item
    /// as a plain TagMix, so contextual niche playback remains finite after an app restart.
    /// </summary>
    public static bool TryFrom(TagMix mix, out NicheMixRecipe recipe)
    {
        if (mix is NicheMix niche)
        {
            recipe = new NicheMixRecipe(niche.Genre, niche.Mood, niche.Decade, niche.Support, niche.Context,
                niche.Focus, niche.Energy, niche.SeedTrackKey, niche.SeedTrackTitle, niche.ArtistNames);
            return recipe.Dimensions >= 2;
        }

        recipe = new NicheMixRecipe(null, null, null);
        if (!mix.RatingKey.StartsWith(RatingKeyPrefix, StringComparison.Ordinal)) return false;

        string? genre = null, mood = null, context = null, seed = null, seedTitle = null;
        int? decade = null;
        var focus = NicheFocus.Blend;
        var energy = NicheEnergy.Any;
        IReadOnlyList<string> artists = [];
        foreach (var part in mix.RatingKey[RatingKeyPrefix.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            if (equals < 0) continue;
            var key = part[..equals];
            var value = Uri.UnescapeDataString(part[(equals + 1)..]);
            switch (key)
            {
                case "g" when value.Length > 0: genre = value; break;
                case "m" when value.Length > 0: mood = value; break;
                case "d" when int.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed): decade = parsed; break;
                case "c" when value.Length > 0: context = value; break;
                case "f" when Enum.TryParse<NicheFocus>(value, true, out var parsedFocus): focus = parsedFocus; break;
                case "e" when Enum.TryParse<NicheEnergy>(value, true, out var parsedEnergy): energy = parsedEnergy; break;
                case "s" when value.Length > 0: seed = value; break;
                case "st" when value.Length > 0: seedTitle = value; break;
                case "a" when value.Length > 0:
                    artists = value.Split('\u001F', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
            }
        }

        recipe = new NicheMixRecipe(genre, mood, decade, 0, context, focus, energy, seed, seedTitle, artists);
        return recipe.Dimensions >= 2;
    }
}

/// <summary>
/// Builds niche concepts from combinations that actually occur in the listener's behaviour. Tags
/// describe the musical corner; history, energy and session patterns explain why that corner matters.
/// </summary>
public static class NicheMixes
{
    private const int MinOccurrences = 2;
    private const double MinSupport = 0.30;
    private static readonly HashSet<string> NoiseMoods = new(StringComparer.OrdinalIgnoreCase) { "ArtistPopular" };

    private sealed record RecipeKey(
        string? Genre,
        string? Mood,
        int? Decade,
        string? Context = null,
        NicheFocus Focus = NicheFocus.Blend,
        NicheEnergy Energy = NicheEnergy.Any,
        string? Artists = null)
    {
        public int Dimensions =>
            (Genre is null ? 0 : 1) + (Mood is null ? 0 : 1) + (Decade is null ? 0 : 1)
            + (Context is null ? 0 : 1) + (Focus == NicheFocus.Blend ? 0 : 1)
            + (Energy == NicheEnergy.Any ? 0 : 1) + (Artists is null ? 0 : 1);
    }

    private sealed class RecipeKeyComparer : IEqualityComparer<RecipeKey>
    {
        public static readonly RecipeKeyComparer Instance = new();
        public bool Equals(RecipeKey? x, RecipeKey? y) =>
            ReferenceEquals(x, y) || x is not null && y is not null
            && x.Decade == y.Decade && x.Focus == y.Focus && x.Energy == y.Energy
            && string.Equals(x.Genre, y.Genre, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Mood, y.Mood, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Context, y.Context, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Artists, y.Artists, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(RecipeKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Genre, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Mood, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Decade);
            hash.Add(obj.Context, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Focus);
            hash.Add(obj.Energy);
            hash.Add(obj.Artists, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed class Evidence
    {
        public double Weight { get; set; }
        public HashSet<string> Tracks { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Artists { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<NicheMixRecipe> Generate(
        IEnumerable<Track> recent,
        DateTimeOffset now,
        int count = 18,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? artistMoods = null,
        IReadOnlyList<HistoryPlay>? history = null,
        IReadOnlyDictionary<string, NicheEnergy>? energy = null)
    {
        if (count <= 0) return [];
        var tracks = recent.ToList();
        var evidence = new Dictionary<RecipeKey, Evidence>(RecipeKeyComparer.Instance);
        var historyByTrack = (history ?? []).GroupBy(p => p.RatingKey).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var circles = ArtistCircles(history ?? []);
        var currentPeriod = MelodayPeriods.At(now.LocalDateTime);
        var dayContext = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Weekend" : "Weekday";

        foreach (var track in tracks)
        {
            var genres = Clean(track.Genres).ToList();
            IEnumerable<string> moodSource = track.Moods;
            if (track.ArtistRatingKey is { } artistKey && artistMoods?.TryGetValue(artistKey, out var inheritedMoods) == true)
                moodSource = moodSource.Concat(inheritedMoods);
            var moods = Clean(moodSource).Where(m => !NoiseMoods.Contains(m)).ToList();
            var decade = track.Year is >= 1900 and < 2100 ? track.Year.Value / 10 * 10 : (int?)null;
            if (genres.Count == 0 && moods.Count == 0 && decade is null) continue;

            var weight = ListeningProfile.Weight(track.LastViewedAt, now)
                * (1 + Math.Min(0.8, Math.Log2(Math.Max(1, track.ViewCount) + 1) * 0.12));
            var artist = track.ArtistRatingKey ?? track.ArtistTitle ?? track.RatingKey;

            // The ordinary tag intersections remain the backbone of a niche.
            foreach (var genre in genres)
            {
                foreach (var mood in moods)
                {
                    if (string.Equals(genre, mood, StringComparison.OrdinalIgnoreCase)) continue;
                    Add(new RecipeKey(genre, mood, null), track, artist, weight);
                    if (decade is { } d) Add(new RecipeKey(genre, mood, d), track, artist, weight * 1.08);
                }
                if (decade is { } d2) Add(new RecipeKey(genre, null, d2), track, artist, weight);
            }
            if (decade is { } d3)
                foreach (var mood in moods) Add(new RecipeKey(null, mood, d3), track, artist, weight);

            // Behavioural recipes use one strong musical anchor plus the learned reason for it.
            var anchor = Anchor(genres, moods, decade);
            if (anchor is null) continue;

            // Deep cuts come from a proven corner of taste, but playback deliberately favours its least-played music.
            Add(anchor with { Focus = NicheFocus.DeepCuts }, track, artist, weight * 0.72);

            if (track.LastViewedAt is { } last)
            {
                var age = now - last;
                if (age <= TimeSpan.FromDays(14)) Add(anchor with { Focus = NicheFocus.Lately }, track, artist, weight * 1.12);
                if (age >= TimeSpan.FromDays(45)) Add(anchor with { Focus = NicheFocus.Rediscovery }, track, artist, weight * 0.9);
            }

            if (energy?.TryGetValue(track.RatingKey, out var band) == true && band != NicheEnergy.Any)
                Add(anchor with { Energy = band }, track, artist, weight * 0.92);

            if (historyByTrack.TryGetValue(track.RatingKey, out var plays))
            {
                if (plays.Any(p => currentPeriod.Hours.Contains(p.ViewedAt.LocalDateTime.Hour)))
                    Add(anchor with { Context = currentPeriod.Name }, track, artist, weight);
                if (plays.Any(p => DayContext(p.ViewedAt) == dayContext))
                    Add(anchor with { Context = dayContext }, track, artist, weight * 0.9);
            }

            if (!string.IsNullOrWhiteSpace(track.ArtistTitle))
            {
                foreach (var circle in circles.Where(c => c.Any(a => a.Equals(track.ArtistTitle, StringComparison.OrdinalIgnoreCase))))
                    Add(anchor with { Artists = string.Join('\u001F', circle) }, track, artist, weight * 0.95);
            }
        }

        var ranked = evidence
            .Where(x => x.Key.Dimensions >= 2
                        && x.Value.Tracks.Count >= MinOccurrences
                        && x.Value.Artists.Count >= 2
                        && x.Value.Weight >= MinSupport)
            .Select(x => ToRecipe(x.Key, x.Value))
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

        void Add(RecipeKey key, Track track, string artistKey, double amount)
        {
            if (!evidence.TryGetValue(key, out var item)) evidence[key] = item = new Evidence();
            if (!item.Tracks.Add(track.RatingKey)) return;
            item.Weight += amount;
            item.Artists.Add(artistKey);
        }
    }

    private static RecipeKey? Anchor(IReadOnlyList<string> genres, IReadOnlyList<string> moods, int? decade)
    {
        if (genres.Count > 0 && moods.Count > 0) return new RecipeKey(genres[0], moods[0], null);
        if (genres.Count > 0 && decade is { } d) return new RecipeKey(genres[0], null, d);
        if (moods.Count > 0 && decade is { } d2) return new RecipeKey(null, moods[0], d2);
        if (genres.Count > 0) return new RecipeKey(genres[0], null, null);
        if (moods.Count > 0) return new RecipeKey(null, moods[0], null);
        return decade is { } d3 ? new RecipeKey(null, null, d3) : null;
    }

    private static NicheMixRecipe ToRecipe(RecipeKey key, Evidence evidence)
    {
        var artists = key.Artists?.Split('\u001F', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var modifier = key.Dimensions - ((key.Genre is null ? 0 : 1) + (key.Mood is null ? 0 : 1) + (key.Decade is null ? 0 : 1));
        var support = evidence.Weight * (1 + 0.12 * Math.Max(0, key.Dimensions - 2))
            + Math.Min(0.5, evidence.Tracks.Count * 0.035)
            + Math.Min(0.35, evidence.Artists.Count * 0.04)
            + Math.Min(0.15, modifier * 0.05);
        return new NicheMixRecipe(key.Genre, key.Mood, key.Decade, support, key.Context, key.Focus, key.Energy, ArtistNames: artists);
    }

    /// <summary>Artist pairs repeatedly heard inside the same 45-minute listening sessions.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> ArtistCircles(IReadOnlyList<HistoryPlay> history)
    {
        if (history.Count < 2) return [];
        var sessions = new List<List<HistoryPlay>>();
        List<HistoryPlay>? current = null;
        HistoryPlay? previous = null;
        foreach (var play in history.OrderBy(p => p.ViewedAt))
        {
            if (current is null || previous is null || play.ViewedAt - previous.ViewedAt > TimeSpan.FromMinutes(45))
            {
                current = [];
                sessions.Add(current);
            }
            current.Add(play);
            previous = play;
        }

        var pairs = new Dictionary<string, (string A, string B, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            var artists = session.Select(p => p.Artist).Where(a => !ListeningProfile.IsPlaceholderArtist(a))
                .Select(a => a!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(8)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            for (var a = 0; a < artists.Count; a++)
                for (var b = a + 1; b < artists.Count; b++)
                {
                    var key = artists[a] + "\u001F" + artists[b];
                    if (pairs.TryGetValue(key, out var pair)) pairs[key] = pair with { Count = pair.Count + 1 };
                    else pairs[key] = (artists[a], artists[b], 1);
                }
        }
        return pairs.Values.Where(p => p.Count >= 2).OrderByDescending(p => p.Count)
            .Take(10).Select(p => (IReadOnlyList<string>)[p.A, p.B]).ToList();
    }

    private static string DayContext(DateTimeOffset at) =>
        at.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Weekend" : "Weekday";

    private static bool TooMuchOfSame(NicheMixRecipe candidate, IReadOnlyList<NicheMixRecipe> selected)
    {
        var genreFamily = candidate.Genre is { } g ? TagFamilies.FamilyOf(g, TagKind.Genre) ?? g : null;
        if (genreFamily is not null && selected.Count(x =>
            string.Equals(x.Genre is { } sg ? TagFamilies.FamilyOf(sg, TagKind.Genre) ?? sg : null, genreFamily, StringComparison.OrdinalIgnoreCase)) >= 3) return true;
        if (candidate.Mood is { } mood && selected.Count(x => string.Equals(x.Mood, mood, StringComparison.OrdinalIgnoreCase)) >= 2) return true;
        if (candidate.Context is { } context && selected.Count(x => string.Equals(x.Context, context, StringComparison.OrdinalIgnoreCase)) >= 2) return true;
        if (candidate.Focus != NicheFocus.Blend && selected.Count(x => x.Focus == candidate.Focus) >= 3) return true;
        if (candidate.Energy != NicheEnergy.Any && selected.Count(x => x.Energy == candidate.Energy) >= 2) return true;
        return false;
    }

    private static IEnumerable<string> Clean(IEnumerable<string> tags) =>
        tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
}
