using HumeMusic.Core.Mixes;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HumeMusic.ViewModels;

public sealed partial class HomeViewModel
{
    private DateTime _discoveryLoadedAt;
    private int _discoveryVersion = -1;

    /// <summary>
    /// Builds a refreshed Discovery Mix from underplayed parts of the listener's own library. The
    /// generated mix uses a small set of promising albums so normal BuiltMix playback can carry it.
    /// </summary>
    public async Task EnsureDiscoveryRowAsync(int libraryVersion)
    {
        if (_discoveryVersion == libraryVersion && DateTime.Now - _discoveryLoadedAt < TimeSpan.FromMinutes(30)) return;
        _discoveryVersion = libraryVersion;
        _discoveryLoadedAt = DateTime.Now;

        for (var i = ContextualRows.Count - 1; i >= 0; i--)
            if (ContextualRows[i].Identifier == "context.discovery") ContextualRows.RemoveAt(i);

        if (Session.Client is not { } client || Session.Section is not { } section) return;
        try
        {
            var disliked = await App.Services.GetRequiredService<Dislikes>().GetAsync();
            var now = DateTimeOffset.Now;
            var recentTask = client.GetTracksAsync(section.Key, 0, 220, "lastViewedAt:desc", default,
                new KeyValuePair<string, string?>("viewCount>>", "0"));
            var poolTask = client.GetTracksAsync(section.Key, 0, 600, "random");
            await Task.WhenAll(recentTask, poolTask);

            var recent = recentTask.Result.Items.Where(disliked.Allows).ToList();
            var pool = poolTask.Result.Items.Where(disliked.Allows).ToList();
            var artistTaste = ListeningProfile.Artists(recent, now, disliked.Artists.Contains).Take(18)
                .ToDictionary(a => a.RatingKey, a => a.Weight, StringComparer.Ordinal);
            var genreTaste = ListeningProfile.Genres(recent, now).Take(14)
                .ToDictionary(g => g.Tag, g => g.Weight, StringComparer.OrdinalIgnoreCase);

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
                if (track.ArtistRatingKey is { } artist && artistTaste.TryGetValue(artist, out var artistWeight))
                    score += Math.Min(1.8, artistWeight * 0.55);
                score += track.Genres.Where(genreTaste.ContainsKey).Sum(g => Math.Min(0.7, genreTaste[g] * 0.12));
                score += (track.UserRating ?? 0) * 0.06;
                score -= Math.Min(5, Math.Max(0, track.SkipCount)) * 0.25;
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
            var items = new List<object> { mix };
            items.AddRange(albums.Take(10));
            ContextualRows.Insert(0, new MediaRow
            {
                Identifier = "context.discovery",
                Title = "Discovery Mix",
                Subtitle = "Underplayed and unheard corners of your library, chosen from what you tend to like",
                Items = items,
                CardStyle = CardStyle.Tile,
            });
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
