using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Radio;

public enum PulseFormat { Rotation, NewMusic, Chart, Genre, Throwbacks, WeeklyPicks, ReleaseCountdown }
public sealed record PulseProgramme(string Title, PulseFormat Format, string? Genre = null)
{
    public static PulseProgramme At(DateTimeOffset time) => time.Hour switch
    {
        >= 6 and < 9 => new("Morning Pulse", PulseFormat.Rotation),
        >= 9 and < 12 => new("New Music", PulseFormat.NewMusic),
        >= 12 and < 14 => new("Chart Pulse", PulseFormat.Chart),
        >= 14 and < 16 => new("Then & Now", PulseFormat.Throwbacks),
        >= 16 and < 18 => new("Pop Focus", PulseFormat.Genre, "pop"),
        >= 18 and < 20 => time.DayOfWeek == DayOfWeek.Friday ? time.Hour == 19 ? new("New Release Countdown", PulseFormat.ReleaseCountdown) : new("Weekly Picks", PulseFormat.WeeklyPicks) : new("Rap & R&B", PulseFormat.Genre, "rap"),
        >= 20 and < 22 => new("Alternative Focus", PulseFormat.Genre, "alternative"),
        _ => new("Continuous Pulse", PulseFormat.Rotation),
    };

    public static PulseProgramme For(PulseFormat format, string? genre = null) => format switch
    {
        PulseFormat.NewMusic => new("New Music", format),
        PulseFormat.Chart => new("Chart Pulse", format),
        PulseFormat.Genre => new(string.IsNullOrWhiteSpace(genre) ? "Genre Focus" : $"{genre} Focus", format, genre),
        PulseFormat.Throwbacks => new("Then & Now", format),
        PulseFormat.WeeklyPicks => new("Weekly Picks", format),
        PulseFormat.ReleaseCountdown => new("New Release Countdown", format),
        _ => new("Continuous Pulse", PulseFormat.Rotation),
    };

    public string Description => Format switch
    {
        PulseFormat.Rotation when Title == "Morning Pulse" => "Easy favourites to start the day",
        PulseFormat.Rotation => "Your whole library on rotation",
        PulseFormat.NewMusic => "Fresh releases and recent additions",
        PulseFormat.Chart => "What's trending in your library",
        PulseFormat.Throwbacks => "Older favourites mixed with new ones",
        PulseFormat.WeeklyPicks => "The week's strongest new songs",
        PulseFormat.ReleaseCountdown => "The top new songs, counted down",
        PulseFormat.Genre when Genre == "rap" => "Hip-hop and R&B from your library",
        PulseFormat.Genre when !string.IsNullOrWhiteSpace(Genre) => $"{char.ToUpperInvariant(Genre[0])}{Genre[1..]} from your library",
        _ => "One genre from your library",
    };

    public HeadlinerPools Select(HeadlinerPools pools, DateTimeOffset time)
    {
        var all = pools.Played.Concat(pools.Fresh).Concat(pools.Trending).DistinctBy(t => t.RatingKey).ToList();
        var selected = Format switch
        {
            PulseFormat.NewMusic => pools.Fresh.Concat(pools.Trending).DistinctBy(t => t.RatingKey).ToList(),
            PulseFormat.Chart => pools.Trending.ToList(),
            PulseFormat.Genre => all.Where(t => t.Genres.Any(g => g.Contains(Genre!, StringComparison.OrdinalIgnoreCase)
                || Genre == "rap" && (g.Contains("hip", StringComparison.OrdinalIgnoreCase) || g.Contains("r&b", StringComparison.OrdinalIgnoreCase)))).ToList(),
            PulseFormat.Throwbacks => all.Where(t => t.Year <= time.Year - 10).Take(40).Concat(pools.Fresh.Take(40)).ToList(),
            PulseFormat.WeeklyPicks => pools.Fresh.OrderByDescending(t => ArtistBlocks.HitScore(t)).Take(40).ToList(),
            PulseFormat.ReleaseCountdown => pools.Fresh.OrderByDescending(t => ArtistBlocks.HitScore(t)).Take(20).Reverse().ToList(),
            _ => all,
        };
        if (selected.Select(LiveRotation.ArtistOf).Distinct().Count() < 8) return pools;
        var keys = selected.Select(t => t.RatingKey).ToHashSet();
        return new(selected, pools.Fresh.Where(t => keys.Contains(t.RatingKey)).ToList(), pools.Trending.Where(t => keys.Contains(t.RatingKey)).ToList());
    }

    public IReadOnlyList<BlockPick>? OrderedPicks(HeadlinerPools pools)
    {
        var tracks = Format switch
        {
            PulseFormat.Chart => pools.Trending.Reverse().ToList(),
            PulseFormat.ReleaseCountdown => pools.Fresh.OrderByDescending(t => ArtistBlocks.HitScore(t)).Take(20).Reverse().ToList(),
            _ => null,
        };
        if (tracks is null || tracks.Select(LiveRotation.ArtistOf).Distinct().Count() < 8) return null;
        return tracks.DistinctBy(LiveRotation.ArtistOf).Take(20).Select(t => new BlockPick(t, 0)).ToList();
    }

    public static DateTimeOffset NextBoundary(DateTimeOffset at)
    {
        var tomorrow = new DateTimeOffset(at.Year, at.Month, at.Day, 0, 0, 0, at.Offset).AddDays(1);
        foreach (var hour in new[] { 6, 9, 12, 14, 16, 18, 19, 20, 22 })
        {
            var candidate = tomorrow.AddDays(-1).AddHours(hour);
            if (candidate > at && At(candidate) != At(at)) return candidate;
        }
        return tomorrow.AddHours(6);
    }
}
