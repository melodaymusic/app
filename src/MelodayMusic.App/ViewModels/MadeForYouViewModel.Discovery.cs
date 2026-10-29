using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MelodayMusic.ViewModels;

public sealed partial class MadeForYouViewModel
{
    private DateTime _discoveryLoadedAt;
    private int _discoveryVersion = -1;
    private BuiltMix? _discovery;

    private async Task EnsureDiscoveryAsync(int libraryVersion)
    {
        if (_discoveryVersion == libraryVersion && DateTime.Now - _discoveryLoadedAt < TimeSpan.FromMinutes(30)) return;
        _discoveryVersion = libraryVersion;
        _discoveryLoadedAt = DateTime.Now;

        _discovery = null;

        if (Session.Client is not { } client || Session.Section is not { } section) return;
        try
        {
            var disliked = await App.Services.GetRequiredService<Dislikes>().GetAsync();
            var now = DateTimeOffset.Now;
            var taste = App.Services.GetRequiredService<TasteService>().Engine;
            var poolTask = client.GetTracksAsync(section.Key, 0, 600, "random");
            await poolTask;

            var pool = poolTask.Result.Items.Where(disliked.Eligible).ToList();

            double Score(Track track)
            {
                if (track.AlbumRatingKey is null) return double.NegativeInfinity;
                if (track.LastViewedAt is { } last && now - last < TimeSpan.FromDays(45)) return double.NegativeInfinity;
                var score = track.ViewCount switch
                {
                    0 => 3.0,
                    1 => 2.0,
                    <= 3 => 1.15,
                    _ => 0.15,
                };
                if (track.LastViewedAt is { } old && now - old > TimeSpan.FromDays(180)) score += 0.8;
                score += 1.8 * taste.ArtistWeight(track.ArtistTitle, track.ArtistRatingKey) + 1.2 * taste.Like(track);
                return score;
            }

            var albumScores = pool
                .Select(t => (Track: t, Score: Score(t)))
                .Where(x => !double.IsNegativeInfinity(x.Score) && x.Track.AlbumRatingKey is not null)
                .GroupBy(x => x.Track.AlbumRatingKey!, StringComparer.Ordinal)
                .Select(g => new
                {
                    Key = g.Key,
                    Score = g.OrderByDescending(x => x.Score).Take(4).Sum(x => x.Score),
                    Artist = g.Select(x => x.Track.ArtistRatingKey ?? x.Track.ArtistTitle).FirstOrDefault(),
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => StableDiscoveryTie(x.Key, now.Date))
                .ToList();

            var chosenKeys = new List<string>();
            var artistCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var album in albumScores)
            {
                var artist = album.Artist ?? album.Key;
                artistCounts.TryGetValue(artist, out var count);
                if (count >= 2) continue;
                chosenKeys.Add(album.Key);
                artistCounts[artist] = count + 1;
                if (chosenKeys.Count >= 12) break;
            }
            if (chosenKeys.Count < 5) return;

            var albums = (await client.GetItemsAsync(chosenKeys)).OfType<Album>().ToList();
            if (albums.Count < 5) return;
            var lead = albums[0];
            var mix = new BuiltMix
            {
                RatingKey = "mix:discovery",
                Title = "Discovery Mix",
                Thumb = lead.Thumb,
                Art = lead.Art,
                AlbumKeys = albums.Select(a => a.RatingKey).ToList(),
            };
            _discovery = mix;
        }
        catch (PlexException)
        {
        }
    }

    private static int StableDiscoveryTie(string text, DateTime date)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in text) hash = hash * 31 + c;
            return hash * 31 + date.DayOfYear + date.Year * 397;
        }
    }
}
