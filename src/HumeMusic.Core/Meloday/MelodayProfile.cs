namespace HumeMusic.Core.Meloday;

/// <summary>One play from the listening history: the track, its album and artist, and when.</summary>
public sealed record HistoryPlay(string RatingKey, string? AlbumKey, string? Artist, DateTimeOffset ViewedAt);

/// <summary>An album's tags and year: in Plex these live on the album (track tags are almost always empty).</summary>
public sealed record AlbumTags(IReadOnlySet<string> Genres, IReadOnlySet<string> Moods, int? Year)
{
    public static AlbumTags Empty { get; } = new(new HashSet<string>(), new HashSet<string>(), null);
}

/// <summary>
/// What the listener plays at this time of day: weights for genres, moods, artists and eras, how
/// strongly each track belongs here, and the tracks that define the period.
/// </summary>
public sealed class PeriodProfile
{
    public Dictionary<string, double> GenreWeights { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> MoodWeights { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> ArtistWeights { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> EraWeights { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> TrackAffinity { get; } = new(StringComparer.Ordinal);
    public List<string> DefiningKeys { get; } = [];
    public double TotalWeight { get; set; }
    public string DominantGenre { get; set; } = "Eclectic";
    public string DominantMood { get; set; } = "Vibes";
}

/// <summary>The period profile, built from the last 60 days of plays (Meloday's build_period_profile).</summary>
public static class MelodayProfile
{
    public const int LookbackDays = 60;

    /// <summary>
    /// How much a play says about this period today: recent plays count most, plays at this time of
    /// day far more than others, and plays on this weekday (or the next or previous) more still.
    /// </summary>
    public static double ContextScore(DateTime viewedAt, DateTime now, IReadOnlyCollection<int> periodHours)
    {
        var ageDays = (now - viewedAt).TotalSeconds / 86_400;
        if (ageDays > LookbackDays) return 0;
        var recency = 1.0 - ageDays / LookbackDays;
        var hourScore = periodHours.Contains(viewedAt.Hour) ? 1.6 : 0.5;
        var gap = Math.Abs((int)viewedAt.DayOfWeek - (int)now.DayOfWeek);
        var delta = Math.Min(gap, 7 - gap);
        var dayScore = delta switch { 0 => 1.5, 1 => 1.1, _ => 0.75 };
        return recency * hourScore * dayScore;
    }

    /// <param name="plays">Newest first, as Plex returns them.</param>
    /// <param name="excluded">Tracks played too recently to use.</param>
    /// <param name="recentlyUsed">Tracks Meloday used in the last days.</param>
    /// <param name="album">Tags for an album key (null when unknown).</param>
    /// <param name="now">Local time.</param>
    public static PeriodProfile Build(MelodayPeriod period, IReadOnlyList<HistoryPlay> plays, IReadOnlySet<string> excluded,
        IReadOnlySet<string> recentlyUsed, Func<string, AlbumTags?> album, DateTime now)
    {
        var profile = new PeriodProfile();
        var scores = new Dictionary<string, (List<double> Context, HistoryPlay First)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var play in plays)
        {
            if (excluded.Contains(play.RatingKey) || recentlyUsed.Contains(play.RatingKey)) continue;
            var ctx = ContextScore(play.ViewedAt.LocalDateTime, now, period.Hours.ToList());
            if (ctx <= 0) continue;
            if (!scores.TryGetValue(play.RatingKey, out var entry))
            {
                scores[play.RatingKey] = entry = (new List<double>(), play);
                order.Add(play.RatingKey);
            }
            entry.Context.Add(ctx);
        }
        if (scores.Count == 0) return profile;

        foreach (var key in order)
        {
            var (context, first) = scores[key];
            var consistency = Math.Min(context.Count / 5.0, 1.0);
            // Plex's history carries no listen-through, so every play counts as finished (the script's
            // completion ratio of 1): affinity is the context score doubled.
            var affinity = context.Max() * (1.0 + consistency) * 2.0;
            profile.TrackAffinity[key] = affinity;

            var tags = first.AlbumKey is { } albumKey ? album(albumKey) : null;
            if (tags is not null)
            {
                foreach (var g in tags.Genres) Add(profile.GenreWeights, g, affinity);
                foreach (var m in tags.Moods) Add(profile.MoodWeights, m, affinity);
                if (tags.Year is int year && year > 1900 && year <= now.Year) Add(profile.EraWeights, $"{year / 10 * 10}s", affinity);
            }
            if (!string.IsNullOrEmpty(first.Artist)) Add(profile.ArtistWeights, first.Artist, affinity);
            profile.TotalWeight += affinity;
        }

        profile.DefiningKeys.AddRange(order
            .Where(k => profile.TrackAffinity[k] > 0 && scores[k].Context.Count >= 2)
            .OrderByDescending(k => profile.TrackAffinity[k])
            .Take(20));
        profile.DominantGenre = Top(profile.GenreWeights) ?? "Eclectic";
        profile.DominantMood = Top(profile.MoodWeights) ?? "Vibes";
        return profile;
    }

    internal static void Add(Dictionary<string, double> weights, string key, double amount) =>
        weights[key] = weights.GetValueOrDefault(key) + amount;

    /// <summary>The heaviest key (the first one reached, on a tie).</summary>
    internal static string? Top(Dictionary<string, double> weights)
    {
        string? best = null;
        var most = double.MinValue;
        foreach (var (key, weight) in weights)
        {
            if (weight <= most) continue;
            most = weight;
            best = key;
        }
        return best;
    }
}
