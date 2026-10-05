using System.Collections.ObjectModel;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;

namespace HumeMusic.ViewModels;

public sealed partial class HomeViewModel
{
    private DateTime _contextualLoadedAt;
    private int _contextualVersion = -1;

    /// <summary>
    /// Short-lived Home shelves chosen for the present context. They are separate from the user's
    /// configured Home rows because a Sunday-night or October shelf should be able to disappear cleanly.
    /// </summary>
    public ObservableCollection<MediaRow> ContextualRows { get; } = [];

    public async Task EnsureContextualRowsAsync(int libraryVersion)
    {
        if (_contextualVersion == libraryVersion && DateTime.Now - _contextualLoadedAt < TimeSpan.FromMinutes(30)) return;
        _contextualVersion = libraryVersion;
        _contextualLoadedAt = DateTime.Now;

        if (_session.Client is not { } client || _session.Section is not { } section)
        {
            ContextualRows.Clear();
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            var account = _session.Server?.Owned == true ? "1" : null;
            var history = await client.GetHistoryAsync(section.Key, now.AddDays(-400), account, max: 6000).ConfigureAwait(false);
            var niches = await _mixes.GetNicheMixesAsync().ConfigureAwait(false);
            var candidates = new List<(double Confidence, MediaRow Row)>();

            async Task<IReadOnlyList<Album>> AlbumsFor(IEnumerable<HistoryPlay> source, int take = 10,
                IReadOnlySet<string>? exclude = null)
            {
                var keys = source.Where(p => p.AlbumKey is not null && (exclude is null || !exclude.Contains(p.AlbumKey!)))
                    .GroupBy(p => p.AlbumKey!, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenByDescending(g => g.Max(p => p.ViewedAt))
                    .Take(take * 2)
                    .Select(g => g.Key)
                    .ToList();
                if (keys.Count == 0) return [];
                return (await client.GetItemsAsync(keys).ConfigureAwait(false)).OfType<Album>().Take(take).ToList();
            }

            void Add(double confidence, string id, string title, string subtitle, IEnumerable<MediaItem> items)
            {
                var distinct = items.DistinctBy(i => i.RatingKey).Take(12).Cast<object>().ToList();
                if (distinct.Count < 4 || confidence < 0.45) return;
                candidates.Add((confidence, new MediaRow
                {
                    Identifier = id,
                    Title = title,
                    Subtitle = subtitle,
                    Items = distinct,
                    CardStyle = CardStyle.Poster,
                }));
            }

            var period = MelodayPeriods.At(now.LocalDateTime);
            var periodPlays = history.Where(p => now - p.ViewedAt <= TimeSpan.FromDays(90)
                && period.Hours.Contains(p.ViewedAt.LocalDateTime.Hour)).ToList();
            var periodAlbums = await AlbumsFor(periodPlays).ConfigureAwait(false);
            var periodMixes = niches.Where(n => string.Equals(n.Context, period.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.Support).Take(3).Cast<MediaItem>();
            var periodArtists = periodPlays.Select(p => p.Artist).Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var periodConfidence = Math.Min(1, periodPlays.Count / 24.0) * Math.Min(1, periodArtists / 4.0);
            Add(periodConfidence, "context.time", PeriodTitle(period.Name),
                $"Music that repeatedly fits your {period.Name.ToLowerInvariant()} listening",
                periodAlbums.Cast<MediaItem>().Concat(periodMixes));

            // A day-specific shelf only appears when this weekday really has enough repeated behaviour.
            var dayPlays = history.Where(p => now - p.ViewedAt <= TimeSpan.FromDays(120)
                && p.ViewedAt.LocalDateTime.DayOfWeek == now.DayOfWeek
                && period.Hours.Contains(p.ViewedAt.LocalDateTime.Hour)).ToList();
            var dayAlbums = await AlbumsFor(dayPlays).ConfigureAwait(false);
            var dayConfidence = Math.Min(1, dayPlays.Count / 18.0)
                * Math.Min(1, dayPlays.Select(p => p.Artist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count() / 4.0);
            Add(dayConfidence, "context.day", $"{now:dddd} {ShortPeriod(period.Name)}",
                $"What tends to fit this part of your {now:dddd}s", dayAlbums);

            // Seasonal shelves are based on the listener's actual history in these months, including
            // previous years, rather than assuming what 'autumn music' should sound like.
            var season = Season(now.Month);
            var seasonMonths = SeasonMonths(now.Month);
            var seasonPlays = history.Where(p => seasonMonths.Contains(p.ViewedAt.LocalDateTime.Month)).ToList();
            var seasonAlbums = await AlbumsFor(seasonPlays).ConfigureAwait(false);
            var seasonYears = seasonPlays.Select(p => p.ViewedAt.Year).Distinct().Count();
            var seasonConfidence = Math.Min(1, seasonPlays.Count / 32.0) * (seasonYears >= 2 ? 1 : 0.72);
            Add(seasonConfidence, "context.season", $"Your {season} Rotation",
                $"Music that has kept showing up for you in {season.ToLowerInvariant()}", seasonAlbums);

            // Personal nostalgia, but do not fill it with albums the listener is already playing now.
            var lastYear = now.AddYears(-1);
            var recentAlbums = history.Where(p => now - p.ViewedAt <= TimeSpan.FromDays(60) && p.AlbumKey is not null)
                .Select(p => p.AlbumKey!).ToHashSet(StringComparer.Ordinal);
            var lastYearPlays = history.Where(p => p.ViewedAt.Year == lastYear.Year
                && Math.Abs(p.ViewedAt.DayOfYear - lastYear.DayOfYear) <= 21).ToList();
            var lastYearAlbums = await AlbumsFor(lastYearPlays, exclude: recentAlbums).ConfigureAwait(false);
            var lastYearConfidence = Math.Min(1, lastYearPlays.Count / 18.0);
            Add(lastYearConfidence, "context.lastyear", "This Time Last Year",
                "Music you were playing around now that has slipped out of rotation", lastYearAlbums);

            var chosen = candidates
                .OrderByDescending(c => c.Confidence)
                .ThenBy(c => c.Row.Identifier, StringComparer.Ordinal)
                .Take(3)
                .Select(c => c.Row)
                .ToList();

            await _dispatcher.EnqueueAsync(() =>
            {
                ContextualRows.Clear();
                foreach (var row in chosen) ContextualRows.Add(row);
            });
        }
        catch (PlexException)
        {
            await _dispatcher.EnqueueAsync(ContextualRows.Clear);
        }
    }

    private static string PeriodTitle(string period) => period switch
    {
        "Late Night" => "Late Night Listening",
        "Night" => "After Dark",
        "Dawn" => "Early Morning Listening",
        "Morning" => "Morning Listening",
        "Afternoon" => "For This Afternoon",
        "Evening" => "For This Evening",
        _ => $"For {period}",
    };

    private static string ShortPeriod(string period) => period switch
    {
        "Late Night" => "Late Night",
        "Dawn" => "Morning",
        _ => period,
    };

    private static string Season(int month) => month switch
    {
        12 or 1 or 2 => "Winter",
        3 or 4 or 5 => "Spring",
        6 or 7 or 8 => "Summer",
        _ => "Autumn",
    };

    private static IReadOnlySet<int> SeasonMonths(int month) => month switch
    {
        12 or 1 or 2 => new HashSet<int> { 12, 1, 2 },
        3 or 4 or 5 => new HashSet<int> { 3, 4, 5 },
        6 or 7 or 8 => new HashSet<int> { 6, 7, 8 },
        _ => new HashSet<int> { 9, 10, 11 },
    };
}
