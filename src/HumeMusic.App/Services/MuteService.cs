using HumeMusic.Core.Models;
using HumeMusic.Core.Mutes;
using HumeMusic.Core.Services;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// Muting a song or an artist for a while: the listener doesn't want to hear them for now, without rating them down. A muted song
/// or artist stays out of everything Meloday makes (the daily Meloday, mixes, live stations, radios: see <see cref="DislikedSet"/>)
/// until the time is up; what the listener picks themselves (an album, a playlist, a song played on purpose) is never stopped.
/// Muting also takes the muted music out of what is queued up next, and moves on from a muted song that is playing.
/// </summary>
public sealed class MuteService
{
    private readonly PlexSession _session;
    private readonly MuteStore _store;
    private readonly SettingsStore _settings;
    private readonly PlayerService _player;
    private readonly MixService _mixes;
    private readonly MelodayService _meloday;
    private readonly NotificationService _notifications;
    private readonly ILogger<MuteService> _log;

    public MuteService(PlexSession session, MuteStore store, SettingsStore settings, PlayerService player, MixService mixes, MelodayService meloday,
        NotificationService notifications, ILogger<MuteService> log)
    {
        _session = session;
        _store = store;
        _settings = settings;
        _player = player;
        _mixes = mixes;
        _meloday = meloday;
        _notifications = notifications;
        _log = log;
        _store.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when something is muted or let back.</summary>
    public event EventHandler? Changed;

    private string? Server => _session.Server?.MachineIdentifier;

    /// <summary>How long a mute lasts unless the listener says otherwise (Settings).</summary>
    public int DefaultDays => MuteLengths.Days(_settings.Current.MuteLength, _settings.Current.MuteCustomDays);

    public string DefaultText => MuteLengths.Describe(DefaultDays);

    public IReadOnlyList<MuteEntry> Active() => Server is { } server ? _store.Active(server) : [];

    public MuteEntry? SongMute(Track track) => Server is { } server ? _store.Get(server, MuteKind.Song, track.RatingKey) : null;

    public MuteEntry? ArtistMute(Track track) => Server is { } server && track.ArtistRatingKey is { } key ? _store.Get(server, MuteKind.Artist, key) : null;

    public MuteEntry? ArtistMute(string artistKey) => Server is { } server ? _store.Get(server, MuteKind.Artist, artistKey) : null;

    /// <summary>Whether the song, or its artist, is muted.</summary>
    public bool IsMuted(Track track) => Server is { } server && _store.IsMuted(server, track.RatingKey, track.ArtistRatingKey);

    // ------------------------------------------------------------------ muting

    public Task MuteSongAsync(Track track, int? days = null)
    {
        if (Server is not { } server) return Task.CompletedTask;
        var length = days ?? DefaultDays;
        var entry = _store.Mute(server, MuteKind.Song, track.RatingKey, track.Title, track.DisplayArtist, length);
        return AfterMuteAsync(entry, length);
    }

    public Task MuteArtistAsync(Track track, int? days = null) =>
        track.ArtistRatingKey is { } key ? MuteArtistAsync(key, track.ArtistTitle ?? track.DisplayArtist, days) : Task.CompletedTask;

    public Task MuteArtistAsync(string artistKey, string name, int? days = null)
    {
        if (Server is not { } server) return Task.CompletedTask;
        var length = days ?? DefaultDays;
        var entry = _store.Mute(server, MuteKind.Artist, artistKey, name, null, length);
        return AfterMuteAsync(entry, length);
    }

    public void Unmute(MuteEntry entry)
    {
        if (!_store.Unmute(entry.Server, entry.Kind, entry.Key)) return;
        // Mixes were made without them: make them again, with them.
        _mixes.Clear();
        _notifications.ShowInfo($"Unmuted {entry.Title}", entry.Kind == MuteKind.Artist ? "Their songs can be in Meloday again." : "It can be in Meloday again.");
    }

    /// <summary>Makes what has been made without the muted music (mixes, the Meloday on show, what is queued), and says what was done.</summary>
    private async Task AfterMuteAsync(MuteEntry entry, int days)
    {
        var removed = 0;
        try
        {
            _mixes.Clear();
            removed += _meloday.PruneMuted();
            if (!_player.IsLive)
            {
                // What is queued up next, then the song that is playing if it is one of these.
                foreach (var queued in _player.Queue.UpNext.Where(e => IsMuted(e.Track)).ToList())
                {
                    _player.RemoveFromQueue(queued.Id);
                    removed++;
                }
                if (_player.CurrentTrack is { } playing && IsMuted(playing))
                {
                    if (_player.Queue.HasNext) await _player.NextAsync();
                    else _player.Pause();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _log.LogWarning(ex, "Couldn't take the muted music out of the queue");
        }
        var what = entry.Kind == MuteKind.Artist ? entry.Title : $"“{entry.Title}”";
        var queued2 = removed > 0 ? $" {removed} {(removed == 1 ? "song was" : "songs were")} taken out of what's coming up." : string.Empty;
        _notifications.ShowInfo($"Muted for {MuteLengths.Describe(days)}", $"{what} stays out of Meloday until {entry.Until.LocalDateTime:MMM d}.{queued2}");
    }
}
