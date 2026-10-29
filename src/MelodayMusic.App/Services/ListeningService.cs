using System.ComponentModel;
using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Pins;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using MelodayMusic.Core.Trends;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class ListeningService
{
    private static readonly TimeSpan TypicalSong = TimeSpan.FromMinutes(3.5);
    private const int HistoryMax = 100_000;

    private readonly ListeningLog _listeningLog;
    private readonly PlayerService _player;
    private readonly PlexSession _session;
    private readonly Core.Home.RecentStore _recents;
    private readonly SettingsStore _settings;
    private readonly ILogger<ListeningService> _log;

    private Track? _track;
    private DateTimeOffset _startedAt;
    private TimeSpan _heard;
    private TimeSpan _lastPosition;
    private bool _counted;
    private (Track Track, DateTimeOffset At, ListenSource Source)? _toLog;

    private (PinKind Kind, bool Meloday, DateTime At)? _chosen;
    private ListenSource _source = ListenSource.Library;
    private int _readingHistory;

    public ListeningService(ListeningLog listeningLog, PlayerService player, PlexSession session, Core.Home.RecentStore recents,
        SettingsStore settings, ILogger<ListeningService> log)
    {
        _listeningLog = listeningLog;
        _player = player;
        _session = session;
        _recents = recents;
        _settings = settings;
        _log = log;
        _player.PropertyChanged += OnPlayerChanged;
        _player.QueueReplaced += OnQueueReplaced;
        _recents.Changed += OnChosen;
        _session.StateChanged += (_, _) => _ = CountHistoryAsync();
        _ = CountHistoryAsync();
    }

    public ListeningLog Log => _listeningLog;

    public bool TrendsShown => _settings.Current.ShowTrends;

    public event EventHandler? ShownChanged;

    public void SetTrendsShown(bool shown)
    {
        if (_settings.Current.ShowTrends == shown) return;
        _settings.Current.ShowTrends = shown;
        _settings.Save();
        ShownChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Flush() => LogFinished();

    private void OnChosen(object? sender, EventArgs e)
    {
        if (_session.Server?.MachineIdentifier is not { } server || _recents.Get(server, 1) is not [var latest]) return;
        var meloday = latest.Kind == PinKind.Station && latest.ToItem() is Station { StationKind: StationKind.Meloday };
        _chosen = (latest.Kind, meloday, DateTime.UtcNow);
    }

    private void OnQueueReplaced(object? sender, EventArgs e)
    {
        var chosen = _chosen;
        _chosen = null;
        if (chosen is { } c && DateTime.UtcNow - c.At > TimeSpan.FromSeconds(30)) chosen = null;
        _source = chosen switch
        {
            { Meloday: true } => ListenSource.Meloday,
            { Kind: PinKind.Station } => ListenSource.Radio,
            { Kind: PinKind.ArtistMix or PinKind.TagMix or PinKind.BuiltMix } => ListenSource.Mix,
            _ => ListenSource.Library,
        };
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerService.CurrentTrack):
                LogFinished();
                _track = _player.CurrentTrack;
                _startedAt = DateTimeOffset.Now;
                _heard = TimeSpan.Zero;
                _lastPosition = _player.Position;
                _counted = false;
                break;
            case nameof(PlayerService.Position):
                var position = _player.Position;
                var step = position - _lastPosition;
                _lastPosition = position;
                if (_track is not { } track || !_player.IsPlaying || step <= TimeSpan.Zero || step >= TimeSpan.FromSeconds(2)) return;
                _heard += step;
                if (!_counted && LastFmClient.ShouldScrobble(track.Duration, _heard))
                {
                    _counted = true;
                    _toLog = (track, _startedAt, _player.IsLive ? ListenSource.Radio : _source);
                }
                break;
        }
    }

    private void LogFinished()
    {
        if (_toLog is not { } done) return;
        _toLog = null;
        var play = new LoggedPlay(done.At, done.Track.RatingKey, done.Track.AlbumRatingKey, done.Track.ArtistTitle ?? done.Track.DisplayArtist,
            (int)Math.Round(_heard.TotalSeconds)) { Source = done.Source };
        _ = Task.Run(() =>
        {
            try { _listeningLog.Add(play); }
            catch (Exception ex) { _log.LogWarning(ex, "Could not add {Track} to the listening log", done.Track.Title); }
        });
    }

    private async Task CountHistoryAsync()
    {
        if (_listeningLog.HistoryCounted || !_session.IsReady || _session.Client is not { } client || _session.Section is not { } section) return;
        if (Interlocked.Exchange(ref _readingHistory, 1) == 1) return;
        try
        {
            var account = _session.Server?.Owned == true ? "1" : null;
            var plays = await client.GetHistoryAsync(section.Key, DateTimeOffset.FromUnixTimeSeconds(1), account, HistoryMax).ConfigureAwait(false);
            var history = plays.Select(p => new LoggedPlay(p.ViewedAt.ToLocalTime(), p.RatingKey, p.AlbumKey, p.Artist,
                (int)(p.DurationMs is > 0 and var ms ? TimeSpan.FromMilliseconds(ms) : TypicalSong).TotalSeconds)
            {
                Estimated = p.DurationMs is not > 0,
            });
            await Task.Run(() => _listeningLog.MergeHistory(history)).ConfigureAwait(false);
            _log.LogInformation("Trends: added {Plays} plays from Plex history", plays.Count);
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogInformation("Trends: Plex history unavailable ({Message})", ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _readingHistory, 0);
        }
    }
}
