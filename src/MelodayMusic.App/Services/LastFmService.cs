using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core;
using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

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
    public bool HasApiKey => _account?.HasApiKey == true;
    public string? UserName => _account?.UserName;

    public bool ScrobblingOn => IsConnected && (_settings.Current.LastFmScrobble ?? true);

    public bool TrendingArtistsOn => HasApiKey && _settings.Current.LastFmTrendingArtists;
    public void SetTrendingArtists(bool on)
    {
        _settings.Current.LastFmTrendingArtists = on;
        _settings.Save();
        OnPropertyChanged(nameof(TrendingArtistsOn));
        TrendingChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TrendingOn => HasApiKey && _settings.Current.LastFmTrending;
    public Task<IReadOnlyList<ChartTrack>> ChartPageAsync(int page) => _account is { HasApiKey: true } account
        ? _client.GetChartTopTracksAsync(account, 100, default, page) : Task.FromResult<IReadOnlyList<ChartTrack>>([]);
    public Task<IReadOnlyList<string>> ArtistChartPageAsync(int page) => _account is { HasApiKey: true } account
        ? _client.GetChartArtistsAsync(account, page) : Task.FromResult<IReadOnlyList<string>>([]);

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

    public event EventHandler? TrendingChanged;

    private static readonly TimeSpan ChartLife = TimeSpan.FromHours(6);
    private (DateTimeOffset At, IReadOnlyList<ChartTrack> Tracks)? _chart;

    public async Task<(DateTimeOffset At, IReadOnlyList<ChartTrack> Tracks)?> ChartAsync(CancellationToken ct = default)
    {
        if (!TrendingOn || _account is null) return null;
        if (_chart is { } kept && DateTimeOffset.UtcNow - kept.At < ChartLife) return kept;
        try
        {
            var tracks = (await LiveChartReader.GetAsync().WaitAsync(ct).ConfigureAwait(false)).Tracks;
            _chart = (DateTimeOffset.UtcNow, tracks);
            return _chart;
        }
        catch (Exception ex) when (ex is LastFmException or HttpRequestException or TaskCanceledException)
        {
            _log.LogDebug("Last.fm's chart unavailable: {Message}", ex.Message);
            return _chart;
        }
    }

    private string? _token;
    private LastFmAccount? _connectingAccount;

    public async Task SaveApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new LastFmException("Enter your own Last.fm API key.");
        var account = new LastFmAccount { ApiKey = apiKey.Trim() };
        await _client.GetChartTopTracksAsync(account, 1);
        if (_account?.ApiKey == account.ApiKey) return;
        Disconnect();
        _account = account;
        _settings.Current.LastFmTrending = false;
        _settings.Current.LastFmTrendingArtists = false;
        _settings.Save();
        Save();
        OnPropertyChanged(nameof(HasApiKey));
        TrendingChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<Uri> BeginConnectAsync(string apiKey, string secret)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = _account?.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
            throw new LastFmException("Enter your API key and shared secret to connect your account.");
        var account = new LastFmAccount { ApiKey = apiKey.Trim(), Secret = secret.Trim() };
        _token = await _client.GetTokenAsync(account);
        _connectingAccount = account;
        return LastFmClient.AuthorizeUri(account, _token);
    }

    public async Task<bool> FinishConnectAsync()
    {
        if (_connectingAccount is not { } account || _token is null) return false;
        try
        {
            var (key, name) = await _client.GetSessionAsync(account, _token);
            account.SessionKey = key;
            account.UserName = name;
            _account = account;
            _pending.Clear(); SavePending();
            _connectingAccount = null;
            _token = null;
            _settings.Current.LastFmScrobble = false;
            _settings.Current.LastFmTrending = false;
            _settings.Current.LastFmTrendingArtists = false;
            _settings.Save();
            Save();
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(HasApiKey));
            OnPropertyChanged(nameof(UserName));
            TrendingChanged?.Invoke(this, EventArgs.Empty);
            _log.LogInformation("Connected to Last.fm");
            return true;
        }
        catch (LastFmException ex) when (ex.Code is 14 or 15 or 4)
        {
            return false;
        }
    }

    public void Disconnect()
    {
        _track = null;
        _pending.Clear();
        SavePending();
        _account = null;
        _connectingAccount = null;
        _token = null;
        _store.Clear();
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(UserName));
        _chart = null;
        TrendingChanged?.Invoke(this, EventArgs.Empty);
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
                var account = _account!;
                var batch = _pending.Take(50).ToList();
                await _client.ScrobbleAsync(account, batch);
                if (_account != account || !ScrobblingOn) break;
                foreach (var scrobble in batch) _pending.Remove(scrobble);
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
