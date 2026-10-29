using System.ComponentModel;
using System.Text.Json;
using MelodayMusic.Core.Discovery;
using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class ListenBrainzService
{
    private sealed record Account(string Token, string User);
    private readonly ISecretStore _store, _pendingStore;
    private readonly SettingsStore _settings;
    private readonly ListenBrainzClient _client;
    private readonly PlayerService _player;
    private readonly ILogger<ListenBrainzService> _log;
    private readonly List<Scrobble> _pending = [];
    private readonly object _gate = new();
    private readonly Timer _retry;
    private Account? _account;
    private CancellationTokenSource _connection = new();
    private Track? _track;
    private DateTimeOffset _started;
    private TimeSpan _heard, _position;
    private bool _sent;
    private int _flushing;
    public event EventHandler? Changed;
    public bool IsConnected => _account is not null;
    public string? UserName => _account?.User;
    public bool ScrobblingOn => IsConnected && _settings.Current.ListenBrainzScrobble;
    public string Status { get; private set; } = "";
    public string Statistics { get; private set; } = "";

    public ListenBrainzService(ISecretStore store, ISecretStore pendingStore, SettingsStore settings, HttpClient http,
        PlayerService player, ILogger<ListenBrainzService> log)
    {
        _store = store; _pendingStore = pendingStore; _settings = settings; _client = new(http); _player = player; _log = log;
        try
        {
            _account = store.Load() is { } json ? JsonSerializer.Deserialize<Account>(json) : null;
            if (_account is not null && settings.Current.ListenBrainzScrobble && pendingStore.Load() is { } pending)
                _pending.AddRange((JsonSerializer.Deserialize<List<Scrobble>>(pending) ?? []).TakeLast(1000));
        }
        catch (JsonException) { _account = null; _pending.Clear(); }
        _player.PropertyChanged += OnPlayerChanged;
        _retry = new Timer(_ => _ = FlushAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
    }

    public async Task<bool> ConnectAsync(string token)
    {
        if (!Guid.TryParse(token.Trim(), out _)) { Status = "Enter your ListenBrainz user token."; Changed?.Invoke(this, EventArgs.Empty); return false; }
        try
        {
            var user = await _client.ValidateAsync(token.Trim());
            if (string.IsNullOrWhiteSpace(user)) { Status = "ListenBrainz didn't accept that token."; Changed?.Invoke(this, EventArgs.Empty); return false; }
            Disconnect();
            _account = new(token.Trim(), user);
            _store.Save(JsonSerializer.Serialize(_account));
            Status = "";
            Changed?.Invoke(this, EventArgs.Empty);
            await RefreshStatisticsAsync();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        { Status = "Couldn't connect to ListenBrainz. Try again."; Changed?.Invoke(this, EventArgs.Empty); return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { _account = null; Status = "Couldn't securely save the ListenBrainz connection on this PC."; Changed?.Invoke(this, EventArgs.Empty); return false; }
    }

    public void Disconnect()
    {
        _connection.Cancel(); _connection.Dispose(); _connection = new();
        _account = null; _track = null; _store.Clear();
        _settings.Current.ListenBrainzScrobble = false; _settings.Save();
        lock (_gate) { _pending.Clear(); _pendingStore.Clear(); }
        Status = Statistics = ""; Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetScrobbling(bool on)
    {
        _connection.Cancel(); _connection.Dispose(); _connection = new();
        _settings.Current.ListenBrainzScrobble = on && IsConnected; _settings.Save();
        _track = null;
        if (!on) lock (_gate) { _pending.Clear(); _pendingStore.Clear(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshStatisticsAsync()
    {
        if (_account is not { } account) return;
        try
        {
            var count = await _client.CountAsync(account.User, _connection.Token);
            if (_account != account) return;
            Statistics = $"{count:N0} listens on ListenBrainz";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        { if (_account == account) Statistics = "Listening statistics are unavailable right now."; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ScrobblingOn) return;
        if (e.PropertyName == nameof(PlayerService.CurrentTrack))
        {
            _track = _player.CurrentTrack; _started = DateTimeOffset.UtcNow; _heard = TimeSpan.Zero;
            _position = _player.Position; _sent = false;
        }
        else if (e.PropertyName == nameof(PlayerService.Position) && _track is { } track)
        {
            var step = _player.Position - _position;
            if (_player.IsPlaying && step > TimeSpan.Zero && step < TimeSpan.FromSeconds(2)) _heard += step;
            _position = _player.Position;
            if (_sent || !LastFmClient.ShouldScrobble(track.Duration, _heard)) return;
            _sent = true;
            lock (_gate)
            {
                _pending.Add(new(track.DisplayArtist, track.Title, track.AlbumTitle, track.ArtistTitle, track.TrackNumber, track.Duration, _started));
                if (_pending.Count > 1000) _pending.RemoveRange(0, _pending.Count - 1000);
                SavePending();
            }
            _ = FlushAsync();
        }
    }

    private async Task FlushAsync()
    {
        if (!ScrobblingOn || Interlocked.Exchange(ref _flushing, 1) == 1) return;
        var account = _account;
        var ct = _connection.Token;
        try
        {
            while (ScrobblingOn && _account == account && account is not null)
            {
                List<Scrobble> batch;
                lock (_gate) batch = _pending.Take(25).ToList();
                if (batch.Count == 0) break;
                await _client.SubmitAsync(account.Token, batch, ct);
                lock (_gate)
                {
                    if (_account != account || ct.IsCancellationRequested) break;
                    foreach (var listen in batch) _pending.Remove(listen);
                    SavePending();
                }
                Status = "";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (!ct.IsCancellationRequested && _account == account)
            {
                if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
                { Disconnect(); Status = "Your ListenBrainz token is no longer valid. Connect again."; }
                else Status = "Listens are saved and will retry when ListenBrainz is available.";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (IOException ex) { _log.LogWarning(ex, "ListenBrainz retry queue could not be saved"); }
        finally { Interlocked.Exchange(ref _flushing, 0); Changed?.Invoke(this, EventArgs.Empty); }
    }

    private void SavePending()
    {
        try
        {
            if (_pending.Count == 0) _pendingStore.Clear();
            else _pendingStore.Save(JsonSerializer.Serialize(_pending));
        }
        catch (IOException ex) { _log.LogWarning(ex, "ListenBrainz retry queue could not be saved"); }
    }
}
