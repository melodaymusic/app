using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Radio;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class PulseScheduleService
{
    private readonly PlexSession _session;
    private readonly PulseScheduleStore _store;
    private readonly PulseLineupStore _lineup;
    private readonly LocalRadio _local;
    private readonly Dislikes _dislikes;
    private readonly ILogger<PulseScheduleService> _log;

    public PulseScheduleService(PlexSession session, PulseScheduleStore store, PulseLineupStore lineup, LocalRadio local, Dislikes dislikes, ILogger<PulseScheduleService> log)
    {
        _session = session;
        _store = store;
        _lineup = lineup;
        _local = local;
        _dislikes = dislikes;
        _log = log;
    }

    public event EventHandler? Changed
    {
        add { _store.Changed += value; _lineup.Changed += value; }
        remove { _store.Changed -= value; _lineup.Changed -= value; }
    }

    public string? Scope => _session.Server?.MachineIdentifier is { } server && _session.Section?.Key is { } section
        ? $"{_session.Account?.Id ?? _session.Account?.Username}|{server}|{section}"
        : null;

    public IReadOnlyList<PulseScheduleRule> Rules => Scope is { } scope ? _store.Get(scope) : [];
    public string Version => Scope is { } scope ? _store.Version(scope) + "-" + _lineup.Version(scope) : "NO-LIBRARY";
    public string? SaveError => _store.SaveError ?? _lineup.SaveError;

    /// <summary>Pulse's own programs for this library: the built-in lineup until it's edited.</summary>
    public IReadOnlyList<PulseSlot> Lineup => Scope is { } scope ? _lineup.Get(scope) : PulseLineup.Defaults;
    public bool IsLineupEdited => Scope is { } scope && _lineup.IsEdited(scope);

    public PulseProgramme Programme(DateTimeOffset airtime) => PulseLineup.At(Lineup, airtime);
    public DateTimeOffset ProgrammeBoundary(DateTimeOffset airtime) => PulseLineup.NextBoundary(Lineup, airtime);

    /// <summary>Saves a program into the lineup, making room by shortening whatever it overlaps.</summary>
    public bool SaveSlot(PulseSlot slot)
    {
        if (!slot.IsValid || Scope is not { } scope) return false;
        _lineup.Set(scope, PulseLineup.Place(Lineup, slot));
        return _lineup.SaveError is null;
    }

    public bool RemoveSlot(string id)
    {
        if (Scope is not { } scope) return false;
        var lineup = Lineup;
        if (lineup.All(s => s.Id != id)) return false;
        _lineup.Set(scope, lineup.Where(s => s.Id != id));
        return _lineup.SaveError is null;
    }

    /// <summary>Brings back the built-in lineup, and with <paramref name="clearAdded"/> also removes the programs added to Pulse.</summary>
    public void Reset(bool clearAdded)
    {
        if (Scope is not { } scope) return;
        _lineup.Reset(scope);
        if (clearAdded) _store.Set(scope, []);
    }

    public PulseScheduleOccurrence? At(DateTimeOffset airtime) => PulseSchedule.At(Rules, airtime);

    public DateTimeOffset? NextBoundary(DateTimeOffset airtime) => PulseSchedule.NextBoundary(Rules, airtime);

    public IReadOnlyList<PulseScheduleRule> Conflicts(PulseScheduleRule rule, DateTimeOffset? from = null) =>
        PulseSchedule.Conflicts(rule, Rules, from ?? PulseSchedule.AtLocal(rule.StartsOn, TimeOnly.MinValue, TimeZoneInfo.Local).AddDays(-1));

    public bool Upsert(PulseScheduleRule rule)
    {
        if (!rule.IsValid || Scope is not { } scope || Conflicts(rule).Count > 0) return false;
        _store.Upsert(scope, rule);
        return _store.SaveError is null;
    }

    public bool Remove(string id) => Scope is { } scope && _store.Remove(scope, id);

    public async Task<IReadOnlyList<Track>> TracksAsync(PulseScheduleOccurrence occurrence, CancellationToken ct = default)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        try
        {
            var source = occurrence.Rule.Source;
            IReadOnlyList<Track> pool = source.Kind switch
            {
                PulseScheduleSourceKind.Track => await OneAsync(client.GetTrackAsync(source.Key, ct)).ConfigureAwait(false),
                PulseScheduleSourceKind.Artist => await client.GetArtistTracksAsync(source.Key, ct).ConfigureAwait(false),
                PulseScheduleSourceKind.Album => await client.GetAlbumTracksAsync(source.Key, ct).ConfigureAwait(false),
                PulseScheduleSourceKind.Playlist => await PlaylistAsync(client, source.Key, ct).ConfigureAwait(false),
                PulseScheduleSourceKind.Genre => (await client.GetTracksAsync(section.Key, 0, 2000, "titleSort", ct,
                    new KeyValuePair<string, string?>("genre", source.Key)).ConfigureAwait(false)).Items,
                PulseScheduleSourceKind.Style => await ArtistTagTracksAsync(client, section.Key, "style", source.Key, ct).ConfigureAwait(false),
                PulseScheduleSourceKind.Mood => await ArtistTagTracksAsync(client, section.Key, "mood", source.Key, ct).ConfigureAwait(false),
                PulseScheduleSourceKind.Decade => await AlbumTagTracksAsync(client, section.Key, "decade", source.Key, ct).ConfigureAwait(false),
                PulseScheduleSourceKind.Collection => await CollectionAsync(client, source.Key, ct).ConfigureAwait(false),
                PulseScheduleSourceKind.Programme => await ProgrammeTracksAsync(source, occurrence.StartsAt, ct).ConfigureAwait(false),
                _ => [],
            };

            var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
            var songs = new SongSet();
            var eligible = pool.Where(t => disliked.Eligible(t) && songs.Add(t)).ToList();
            // Shuffled afresh for each airing, so a repeating block doesn't play the same songs every time.
            if (occurrence.Rule.PlayOrder == PulseScheduleOrder.Shuffle)
                Shuffle(eligible, Seed(occurrence.Rule.Id, occurrence.StartsAt));
            return Limit(eligible, occurrence.Rule.Length);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Scheduled Pulse source {Source} is unavailable: {Message}", occurrence.Rule.Source.Title, ex.Message);
            return [];
        }
    }

    private async Task<IReadOnlyList<Track>> ProgrammeTracksAsync(PulseScheduleSource source, DateTimeOffset airtime, CancellationToken ct)
    {
        if (!Enum.TryParse<PulseFormat>(source.Key, ignoreCase: true, out var format)) return [];
        var programme = PulseProgramme.For(format, source.Subtitle);
        return (await _local.ForBlocks().NextAsync(new HashSet<string>(StringComparer.Ordinal), ct, recordFeatures: false, airtime: airtime, forcedProgramme: programme).ConfigureAwait(false))
            .Select(p => p.Track).ToList();
    }

    private static async Task<IReadOnlyList<Track>> OneAsync(Task<Track?> task) => await task.ConfigureAwait(false) is { } track ? [track] : [];

    private static async Task<IReadOnlyList<Track>> PlaylistAsync(PlexServerClient client, string key, CancellationToken ct)
    {
        var tracks = new List<Track>();
        const int size = 300;
        for (var start = 0; start < 20_000; start += size)
        {
            var page = await client.GetPlaylistItemsAsync(key, start, size, ct).ConfigureAwait(false);
            tracks.AddRange(page.Items);
            if (!page.HasMore || page.Items.Count == 0) break;
        }
        return tracks;
    }

    private static async Task<IReadOnlyList<Track>> CollectionAsync(PlexServerClient client, string key, CancellationToken ct)
    {
        var tracks = new List<Track>();
        foreach (var item in await client.GetCollectionItemsAsync(key, ct).ConfigureAwait(false))
        {
            switch (item)
            {
                case Track track: tracks.Add(track); break;
                case Album album: tracks.AddRange(await client.GetAlbumTracksAsync(album.RatingKey, ct).ConfigureAwait(false)); break;
                case Artist artist: tracks.AddRange(await client.GetArtistTracksAsync(artist.RatingKey, ct).ConfigureAwait(false)); break;
            }
            if (tracks.Count >= 5000) break;
        }
        return tracks;
    }

    private static async Task<IReadOnlyList<Track>> ArtistTagTracksAsync(PlexServerClient client, string section, string field, string value, CancellationToken ct)
    {
        var artists = await client.GetArtistsAsync(section, 0, 500, "titleSort", ct, new KeyValuePair<string, string?>(field, value)).ConfigureAwait(false);
        return artists.Items.Count == 0 ? [] : (await client.GetTracksAsync(section, 0, 3000, "titleSort", ct,
            new KeyValuePair<string, string?>("artist.id", string.Join(',', artists.Items.Select(a => a.RatingKey)))).ConfigureAwait(false)).Items;
    }

    private static async Task<IReadOnlyList<Track>> AlbumTagTracksAsync(PlexServerClient client, string section, string field, string value, CancellationToken ct)
    {
        var albums = await client.GetAlbumsAsync(section, 0, 1000, "titleSort", ct, new KeyValuePair<string, string?>(field, value)).ConfigureAwait(false);
        return albums.Items.Count == 0 ? [] : (await client.GetTracksAsync(section, 0, 3000, "titleSort", ct,
            new KeyValuePair<string, string?>("album.id", string.Join(',', albums.Items.Select(a => a.RatingKey)))).ConfigureAwait(false)).Items;
    }

    private static IReadOnlyList<Track> Limit(IReadOnlyList<Track> tracks, PulseScheduleLength length)
    {
        if (tracks.Count == 0) return [];
        if (length.Kind == PulseScheduleLengthKind.OneSong) return [tracks[0]];
        if (length.Kind == PulseScheduleLengthKind.SongCount) return tracks.Take(length.Value).ToList();
        var chosen = new List<Track>();
        long total = 0;
        var target = TimeSpan.FromMinutes(length.Value).TotalMilliseconds;
        foreach (var track in tracks)
        {
            chosen.Add(track);
            total += track.DurationMs > 0 ? track.DurationMs : LiveSchedule.FallbackDurationMs;
            if (total >= target) break;
        }
        return chosen;
    }

    private static int Seed(string id, DateTimeOffset startsAt)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in id + "|" + startsAt.ToUnixTimeSeconds()) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }

    private static void Shuffle<T>(IList<T> items, int seed)
    {
        var random = new Random(seed);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
