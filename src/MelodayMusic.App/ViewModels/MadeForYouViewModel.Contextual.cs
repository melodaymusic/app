using System.Collections.ObjectModel;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MelodayMusic.ViewModels;

public sealed partial class MadeForYouViewModel
{
    private DateTime _contextualLoadedAt;
    private int _contextualVersion = -1;
    private DateTimeOffset? _contextualContext;

    private readonly Dictionary<string, MediaRow> _shelves = [];

    private async Task EnsureShelvesAsync(int libraryVersion)
    {
        var context = DateTimeOffset.Now;
        if (_contextualVersion == libraryVersion && _contextualContext is { } previous
            && !Core.Home.HomeRefresh.ContextChanged(previous, context)
            && DateTime.Now - _contextualLoadedAt < TimeSpan.FromMinutes(30)) return;
        _contextualVersion = libraryVersion;

        if (Session.Client is not { } client || Session.Section is not { } section)
        {
            _shelves.Clear();
            return;
        }

        try
        {
            var now = context;
            var account = Session.Server?.Owned == true ? "1" : null;
            var history = await client.GetHistoryAsync(section.Key, now.AddDays(-400), account, max: 6000);
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
                return (await client.GetItemsAsync(keys)).OfType<Album>().Take(take).ToList();
            }

            IReadOnlyList<(Playlist Playlist, IReadOnlyList<Track> Tracks)> playlistSamples = [];
            try
            {
                var playlists = (await client.GetPlaylistsAsync(0, 16, sort: "updatedAt:desc")).Items;
                playlistSamples = await Task.WhenAll(playlists.Select(async playlist =>
                {
                    var tracks = (await client.GetPlaylistItemsAsync(playlist.RatingKey, 0, 80)).Items;
                    return (playlist, (IReadOnlyList<Track>)tracks);
                }));
            }
            catch (PlexException)
            {
            }

            IReadOnlyList<Playlist> allPlaylists = [];
            try
            {
                allPlaylists = (await client.GetPlaylistsAsync(0, 500)).Items;
            }
            catch (PlexException)
            {
            }
            IReadOnlyList<Playlist> Named(IEnumerable<string> words) => allPlaylists
                .Where(p => !p.Title.StartsWith("Meloday", StringComparison.OrdinalIgnoreCase) && ContextWords.Names(p.Title, words))
                .OrderByDescending(p => p.LastViewedAt ?? p.UpdatedAt ?? DateTimeOffset.MinValue)
                .Take(8).ToList();

            IReadOnlyList<Playlist> PlaylistsFor(IEnumerable<HistoryPlay> source)
            {
                var artists = source.Select(p => p.Artist).Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a!.Trim()).GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).Take(16).Select(g => g.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (artists.Count < 3) return [];

                return playlistSamples.Select(sample =>
                    {
                        var represented = sample.Tracks.Select(t => t.DisplayArtist)
                            .Where(artists.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                        var matching = sample.Tracks.Count(t => artists.Contains(t.DisplayArtist));
                        var ratio = sample.Tracks.Count == 0 ? 0 : matching / (double)sample.Tracks.Count;
                        return (sample.Playlist, Score: ratio + Math.Min(0.5, represented * 0.08), represented);
                    })
                    .Where(x => x.represented >= 3 && x.Score >= 0.34)
                    .OrderByDescending(x => x.Score)
                    .Take(2)
                    .Select(x => x.Playlist)
                    .ToList();
            }

            void Add(double confidence, string id, string title, string subtitle, IEnumerable<MediaItem> items, int named = 0)
            {
                var distinct = items.DistinctBy(i => i.RatingKey).Take(12).Cast<object>().ToList();
                if (named > 0) confidence = Math.Max(confidence, 0.6);
                if (distinct.Count < (named > 0 ? 2 : 4) || confidence < 0.45) return;
                candidates.Add((confidence, new MediaRow
                {
                    Identifier = id,
                    Title = title,
                    Subtitle = subtitle,
                    Items = distinct,
                    CardStyle = CardStyle.Tile,
                }));
            }

            var period = MelodayPeriods.At(now.LocalDateTime);
            var periodPlays = history.Where(p => now - p.ViewedAt <= TimeSpan.FromDays(90)
                && period.Hours.Contains(p.ViewedAt.LocalDateTime.Hour)).ToList();
            var periodAlbums = await AlbumsFor(periodPlays);
            var periodNamed = Named(ContextWords.ForPeriod(period.Name));
            var periodPlaylists = periodNamed.Concat(PlaylistsFor(periodPlays)).DistinctBy(p => p.RatingKey).ToList();
            var periodArtists = periodPlays.Select(p => p.Artist).Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var periodConfidence = Math.Min(1, periodPlays.Count / 24.0) * Math.Min(1, periodArtists / 4.0);
            Add(periodConfidence, "context.time", ShelfTitle(period.Name),
                $"Albums and playlists that repeatedly fit your {period.Name.ToLowerInvariant()} listening",
                periodPlaylists.Cast<MediaItem>().Concat(periodAlbums), periodNamed.Count);

            var dayPlays = history.Where(p => now - p.ViewedAt <= TimeSpan.FromDays(120)
                && p.ViewedAt.LocalDateTime.DayOfWeek == now.DayOfWeek
                && period.Hours.Contains(p.ViewedAt.LocalDateTime.Hour)).ToList();
            var dayAlbums = await AlbumsFor(dayPlays);
            var dayPlaylists = PlaylistsFor(dayPlays);
            var dayConfidence = Math.Min(1, dayPlays.Count / 18.0)
                * Math.Min(1, dayPlays.Select(p => p.Artist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count() / 4.0);
            Add(dayConfidence, "context.day", $"{now:dddd} {ShortPeriod(period.Name)}",
                $"What tends to fit this part of your {now:dddd}s", dayPlaylists.Cast<MediaItem>().Concat(dayAlbums));

            var season = Season(now.Month);
            var seasonMonths = SeasonMonths(now.Month);
            var seasonPlays = history.Where(p => seasonMonths.Contains(p.ViewedAt.LocalDateTime.Month)).ToList();
            var seasonAlbums = await AlbumsFor(seasonPlays);
            var seasonNamed = Named(ContextWords.ForSeason(season));
            var seasonPlaylists = seasonNamed.Concat(PlaylistsFor(seasonPlays)).DistinctBy(p => p.RatingKey).ToList();
            var seasonYears = seasonPlays.Select(p => p.ViewedAt.Year).Distinct().Count();
            var seasonConfidence = Math.Min(1, seasonPlays.Count / 32.0) * (seasonYears >= 2 ? 1 : 0.72);
            Add(seasonConfidence, "context.season", $"Your {season} Rotation",
                $"Music that has kept showing up for you in {season.ToLowerInvariant()}", seasonPlaylists.Cast<MediaItem>().Concat(seasonAlbums), seasonNamed.Count);

            var lastYear = now.AddYears(-1);
            var recentAlbums = history.Where(p => now - p.ViewedAt <= TimeSpan.FromDays(60) && p.AlbumKey is not null)
                .Select(p => p.AlbumKey!).ToHashSet(StringComparer.Ordinal);
            var lastYearPlays = history.Where(p => p.ViewedAt.Year == lastYear.Year
                && Math.Abs(p.ViewedAt.DayOfYear - lastYear.DayOfYear) <= 21).ToList();
            var lastYearAlbums = await AlbumsFor(lastYearPlays, exclude: recentAlbums);
            var lastYearConfidence = Math.Min(1, lastYearPlays.Count / 18.0);
            Add(lastYearConfidence, "context.lastyear", "This Time Last Year",
                "Music you were playing around now that has slipped out of rotation", lastYearAlbums);

            if (Session.Client != client || Session.Section?.Key != section.Key) return;
            _shelves.Clear();
            foreach (var (_, row) in candidates) _shelves[row.Identifier!] = row;
            _contextualLoadedAt = DateTime.Now;
            _contextualContext = context;
        }
        catch (PlexException)
        {
            _shelves.Clear();
        }
    }

    private static string ShelfTitle(string period) => period switch
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
