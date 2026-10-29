using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class ArtistRanks
{
    private const int PageSize = 500;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly ILogger _log;
    private Task<Dictionary<string, int>>? _load;
    private DateTime _loadedAt;
    private string? _key;

    public ArtistRanks(PlexSession session, SettingsStore settings, ILogger<ArtistRanks> log)
    {
        _session = session;
        _settings = settings;
        _log = log;
    }

    public async Task<int?> GetAsync(string artistKey)
    {
        var limit = _settings.Current.ArtistRankLimit;
        if (limit == 0 || _session.Client is not { } client || _session.Section is not { } section) return null;
        var key = $"{client.Server.MachineIdentifier}|{section.Key}|{limit}";
        if (_load is null || _key != key || DateTime.UtcNow - _loadedAt > Lifetime || _load.IsFaulted)
        {
            _key = key;
            _loadedAt = DateTime.UtcNow;
            _load = LoadAsync(client, section.Key, limit);
        }
        var ranks = await _load;
        return ranks.TryGetValue(artistKey, out var rank) && LibraryRank.IsShown(rank, limit) ? rank : null;
    }

    private async Task<Dictionary<string, int>> LoadAsync(PlexServerClient client, string section, int limit)
    {
        var played = new List<(string Key, int Plays)>();
        try
        {
            for (var start = 0; ; start += PageSize)
            {
                var page = await client.GetArtistsAsync(section, start, PageSize, "viewCount:desc", default,
                    new KeyValuePair<string, string?>("viewCount>>", "0")).ConfigureAwait(false);
                played.AddRange(page.Items.Where(a => !ListeningProfile.IsPlaceholderArtist(a.Title)).Select(a => (a.RatingKey, a.ViewCount)));
                var done = page.Items.Count < PageSize || (limit > 0 && played.Count > limit && played[^1].Plays < played[limit - 1].Plays);
                if (done) break;
            }
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Artist ranks unavailable: {Message}", ex.Message);
            _load = null;
        }
        return LibraryRank.Rank(played);
    }
}
