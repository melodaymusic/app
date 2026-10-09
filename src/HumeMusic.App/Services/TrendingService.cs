using HumeMusic.Core.LastFm;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>The songs of Last.fm's chart that this library holds, in chart order, and how long the chart was.</summary>
public sealed record Trending(DateTimeOffset At, int ChartSize, IReadOnlyList<Track> InLibrary);

/// <summary>
/// What is trending on Last.fm, seen through the listener's own library: the chart's songs are looked up in the library
/// (by name and artist) and the ones it holds are what Home shows. Only the chart is read from Last.fm; nothing about
/// the listener goes there. Kept until the chart is refreshed (six hours).
/// </summary>
public sealed class TrendingService
{
    /// <summary>Songs shown at most, and chart entries looked up at most.</summary>
    private const int Wanted = 16, LookUp = 60;

    private readonly LastFmService _lastFm;
    private readonly PlexSession _session;
    private readonly ILogger<TrendingService> _log;
    private Task<Trending?>? _load;

    public TrendingService(LastFmService lastFm, PlexSession session, ILogger<TrendingService> log)
    {
        _lastFm = lastFm;
        _session = session;
        _log = log;
        _lastFm.TrendingChanged += (_, _) => _load = null;
        _session.StateChanged += (_, _) => _load = null;
    }

    public bool IsOn => _lastFm.TrendingOn;

    /// <summary>The chart's songs in the library, or null when trending is off, or Last.fm or the library can't be reached.</summary>
    public Task<Trending?> GetAsync()
    {
        if (!_lastFm.TrendingOn) return Task.FromResult<Trending?>(null);
        if (_load is { IsFaulted: false, IsCanceled: false } load && !(load.IsCompleted && load.Result is null)) return load;
        return _load = LoadAsync();
    }

    private async Task<Trending?> LoadAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return null;
        var chart = await _lastFm.ChartAsync().ConfigureAwait(false);
        if (chart is not { } found) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(4, 4);
        var lookups = found.Tracks.Take(LookUp).Select(async (entry, index) =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var results = await client.SearchAsync(entry.Title, section.Key, 10).ConfigureAwait(false);
                var title = MelodayText.CanonicalTitle(entry.Title);
                var artist = MelodayText.NormalizeArtist(entry.Artist);
                var hit = results.Tracks.FirstOrDefault(t =>
                    MelodayText.CanonicalTitle(t.Title) == title
                    && (MelodayText.NormalizeArtist(t.ArtistTitle) == artist || MelodayText.NormalizeArtist(MelodayText.TrackArtist(t)) == artist));
                return (index, hit);
            }
            catch (PlexException ex)
            {
                _log.LogDebug("A chart song couldn't be looked up: {Message}", ex.Message);
                return (index, (Track?)null);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        var matches = await Task.WhenAll(lookups).ConfigureAwait(false);
        var tracks = matches.Where(m => m.Item2 is not null).OrderBy(m => m.index).Select(m => m.Item2!)
            .Where(t => seen.Add(t.RatingKey)).Take(Wanted).ToList();
        _log.LogInformation("Last.fm's chart: {Count} of {Size} songs are in the library", tracks.Count, found.Tracks.Count);
        return new Trending(found.At, found.Tracks.Count, tracks);
    }
}
