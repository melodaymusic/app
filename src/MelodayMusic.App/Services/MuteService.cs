using MelodayMusic.Core.Models;
using MelodayMusic.Core.Mutes;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

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

    public event EventHandler? Changed;

    private string? Server => _session.Server?.MachineIdentifier;

    public int DefaultDays => MuteLengths.Days(_settings.Current.MuteLength, _settings.Current.MuteCustomDays);

    public string DefaultText => MuteLengths.Describe(DefaultDays);

    public IReadOnlyList<MuteEntry> Active() => Server is { } server ? _store.Active(server) : [];

    public MuteEntry? SongMute(Track track) => Server is { } server ? _store.Get(server, MuteKind.Song, track.RatingKey) : null;

    public MuteEntry? ArtistMute(Track track) => Server is { } server && track.ArtistRatingKey is { } key ? _store.Get(server, MuteKind.Artist, key) : null;

    public MuteEntry? ArtistMute(string artistKey) => Server is { } server ? _store.Get(server, MuteKind.Artist, artistKey) : null;

    public bool IsMuted(Track track) => Server is { } server && _store.IsMuted(server, track.RatingKey, track.ArtistRatingKey);

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
        _mixes.Clear();
        _notifications.ShowInfo($"Unmuted {entry.Title}", entry.Kind == MuteKind.Artist ? "Their songs can be in Meloday again." : "It can be in Meloday again.");
    }

    private async Task AfterMuteAsync(MuteEntry entry, int days)
    {
        var removed = 0;
        try
        {
            _mixes.Clear();
            removed += _meloday.PruneMuted();
            if (!_player.IsLive)
            {
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
