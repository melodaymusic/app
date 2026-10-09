using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core;
using HumeMusic.Core.LastFm;
using HumeMusic.Core.Models;
using HumeMusic.Core.Security;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// Scrobbling to Last.fm, for listeners whose Plex account doesn't already do it: Now Playing as each
/// song starts, and a scrobble once it has played long enough (half the song or four minutes). Songs
/// that can't be sent at the moment wait and go with the next. The connection (the listener's own API
/// key and secret, and the session) is kept encrypted on this computer.
/// </summary>
public sealed partial class LastFmService : ObservableObject
{
    private readonly ISecretStore _store;
    private readonly SettingsStore _settings;
    private readonly LastFmClient _client;
    private readonly PlayerService _player;
    private readonly ILogger<LastFmService> _log;
    private readonly string _pendingPath = Path.Combine(AppPaths.Root, "lastfm-pending.json");
    private LastFmAccount? _account;
    private List<Scrobble> _pending = [];

    // The song playing: when it started and how long it has actually been heard.
    private Track? _track;
    private DateTimeOffset _startedAt;
    private TimeSpan _heard;
    private TimeSpan _lastPosition;
    private bool _scrobbled;

    public LastFmService(ISecretStore store, SettingsStore settings, HttpClient http, PlayerService player, ILogger<LastFmService> log)
    {
        _store = store;
        _settings = settings;
        _client = new LastFmClient(http);
        _player = player;
        _log = log;
        _account = Load();
        _pending = LoadPending();
        _player.PropertyChanged += OnPlayerChanged;
    }

    public bool IsConnected => _account?.IsConnected == true;
    public string? UserName => _account?.UserName;

    /// <summary>Whether what is played is sent to Last.fm: connected, and the listener has it on (a connection from before the choice existed is on).</summary>
    public bool ScrobblingOn => IsConnected && (_settings.Current.LastFmScrobble ?? true);

    /// <summary>Whether Last.fm's chart is shown on Home: connected, and the listener has it on.</summary>
    public bool TrendingOn => IsConnected && _settings.Current.LastFmTrending;

    /// <summary>Turns scrobbling on or off. Turning it off also drops what was waiting to be sent: nothing leaves after the listener said no.</summary>
    public void SetScrobbling(bool on)
    {
        _settings.Current.LastFmScrobble = on;
        _settings.Save();
        if (!on)
        {
            _track = null;
            _pending.Clear();
            SavePending();
        }
        OnPropertyChanged(nameof(ScrobblingOn));
    }

    public void SetTrending(bool on)
    {
        _settings.Current.LastFmTrending = on;
        _settings.Save();
        if (!on) _chart = null;
        OnPropertyChanged(nameof(TrendingOn));
        TrendingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the trending choice changes (Home shows or drops its row).</summary>
    public event EventHandler? TrendingChanged;

    // ------------------------------------------------------------------ the chart

    private static readonly TimeSpan ChartLife = TimeSpan.FromHours(6);
    private (DateTimeOffset At, IReadOnlyList<ChartTrack> Tracks)? _chart;

    /// <summary>
    /// Last.fm's chart of what the world is listening to now (kept six hours), or null when trending is off or Last.fm
    /// can't be reached. Reading the chart sends nothing about the listener.
    /// </summary>
    public async Task<(DateTimeOffset At, IReadOnlyList<ChartTrack> Tracks)?> ChartAsync(CancellationToken ct = default)
    {
        if (!TrendingOn || _account is null) return null;
        if (_chart is { } kept && DateTimeOffset.UtcNow - kept.At < ChartLife) return kept;
        try
        {
            var tracks = await _client.GetChartTopTracksAsync(_account, 100, ct).ConfigureAwait(false);
            _chart = (DateTimeOffset.UtcNow, tracks);
            return _chart;
        }
        catch (Exception ex) when (ex is LastFmException or HttpRequestException or TaskCanceledException)
        {
            _log.LogDebug("Last.fm's chart unavailable: {Message}", ex.Message);
            return _chart;
        }
    }

    // ------------------------------------------------------------------ connecting

    private string? _token;

    /// <summary>
    /// The first step of connecting: keeps the key and secret, and returns the page where the listener
    /// approves Meloday Music on last.fm.
    /// </summary>
    public async Task<Uri> BeginConnectAsync(string apiKey, string secret)
    {
        var account = new LastFmAccount { ApiKey = apiKey.Trim(), Secret = secret.Trim() };
        _token = await _client.GetTokenAsync(account);
        _account = account;
        return LastFmClient.AuthorizeUri(account, _token);
    }

    /// <summary>The second step, once approved: true when Last.fm has made the session.</summary>
    public async Task<bool> FinishConnectAsync()
    {
        if (_account is null || _token is null) return false;
        try
        {
            var (key, name) = await _client.GetSessionAsync(_account, _token);
            _account.SessionKey = key;
            _account.UserName = name;
            _token = null;
            // A new connection sends nothing and shows nothing until the listener turns each on.
            _settings.Current.LastFmScrobble = false;
            _settings.Current.LastFmTrending = false;
            _settings.Save();
            Save();
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(UserName));
            _log.LogInformation("Connected to Last.fm");
            return true;
        }
        catch (LastFmException ex) when (ex.Code is 14 or 15 or 4)
        {
            // Not approved yet (14), or the token lapsed.
            return false;
        }
    }

    public void Disconnect()
    {
        _account = null;
        _token = null;
        _store.Clear();
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(UserName));
    }

    private LastFmAccount? Load()
    {
        try
        {
            return _store.Load() is { } json ? JsonSerializer.Deserialize<LastFmAccount>(json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Save()
    {
        if (_account is not null) _store.Save(JsonSerializer.Serialize(_account));
    }

    // ------------------------------------------------------------------ scrobbling

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ScrobblingOn) return;
        switch (e.PropertyName)
        {
            case nameof(PlayerService.CurrentTrack):
                Finish();
                if (_player.CurrentTrack is { } track)
                {
                    _track = track;
                    _startedAt = DateTimeOffset.UtcNow;
                    _heard = TimeSpan.Zero;
                    _lastPosition = _player.Position;
                    _scrobbled = false;
                    _ = SendNowPlayingAsync(track);
                }
                break;
            case nameof(PlayerService.Position):
                // Only time actually heard counts: a seek forward isn't listening.
                var position = _player.Position;
                var step = position - _lastPosition;
                if (_player.IsPlaying && step > TimeSpan.Zero && step < TimeSpan.FromSeconds(2)) _heard += step;
                _lastPosition = position;
                if (!_scrobbled && _track is { } current && LastFmClient.ShouldScrobble(current.Duration, _heard))
                {
                    _scrobbled = true;
                    _pending.Add(ToScrobble(current, _startedAt));
                    _ = FlushAsync();
                }
                break;
        }
    }

    private void Finish()
    {
        _track = null;
    }

    private static Scrobble ToScrobble(Track t, DateTimeOffset startedAt) =>
        new(t.DisplayArtist, t.Title, t.AlbumTitle, t.ArtistTitle, t.TrackNumber, t.Duration, startedAt);

    private async Task SendNowPlayingAsync(Track track)
    {
        try
        {
            await _client.UpdateNowPlayingAsync(_account!, ToScrobble(track, DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is LastFmException or HttpRequestException or TaskCanceledException)
        {
            Handle(ex);
        }
    }

    private int _flushing;

    private async Task FlushAsync()
    {
        if (Interlocked.Exchange(ref _flushing, 1) == 1) return;
        try
        {
            while (_pending.Count > 0 && ScrobblingOn)
            {
                var batch = _pending.Take(50).ToList();
                await _client.ScrobbleAsync(_account!, batch);
                _pending.RemoveRange(0, batch.Count);
                _log.LogDebug("Scrobbled {Count} song(s) to Last.fm", batch.Count);
            }
        }
        catch (Exception ex) when (ex is LastFmException or HttpRequestException or TaskCanceledException)
        {
            Handle(ex);
        }
        finally
        {
            SavePending();
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    private void Handle(Exception ex)
    {
        if (ex is LastFmException { Code: 9 })
        {
            // The listener revoked access on last.fm: they connect again from Settings.
            _log.LogWarning("Last.fm no longer accepts this session");
            if (_account is not null) _account.SessionKey = null;
            Save();
            OnPropertyChanged(nameof(IsConnected));
            return;
        }
        _log.LogDebug("Last.fm unavailable: {Message}", ex.Message);
    }

    private List<Scrobble> LoadPending()
    {
        try
        {
            return File.Exists(_pendingPath) ? JsonSerializer.Deserialize<List<Scrobble>>(File.ReadAllText(_pendingPath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private void SavePending()
    {
        try
        {
            if (_pending.Count == 0)
            {
                if (File.Exists(_pendingPath)) File.Delete(_pendingPath);
                return;
            }
            File.WriteAllText(_pendingPath, JsonSerializer.Serialize(_pending));
        }
        catch (IOException ex)
        {
            _log.LogDebug("Couldn't keep unsent scrobbles: {Message}", ex.Message);
        }
    }
}
