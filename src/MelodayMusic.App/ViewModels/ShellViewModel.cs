using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Windows.Networking.Connectivity;

namespace MelodayMusic.ViewModels;

public enum RootView
{
    Loading,
    SignIn,
    Setup,
    Main,
}

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly PlexSession _session;
    private readonly PlayerService _player;
    private readonly SettingsStore _settings;
    private readonly ArtworkCache _artworkCache;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;
    private DateTime _lastNetworkReconnect = DateTime.MinValue;
    private string? _lastReadySection;
    private bool _resumePlayback;
    private string? _sleepTrack;
    private bool _reconnecting;
    private readonly DispatcherQueueTimer _recoveryTimer;
    private int _recoveryAttempt;

    public ShellViewModel(PlexSession session, PlayerService player, SettingsStore settings, ArtworkCache artworkCache, ILogger<ShellViewModel> log)
    {
        _session = session;
        _player = player;
        _settings = settings;
        _artworkCache = artworkCache;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _recoveryTimer = _dispatcher.CreateTimer();
        _recoveryTimer.IsRepeating = false;
        _recoveryTimer.Tick += (_, _) => _ = ReconnectAndRefreshAsync(delay: false);
        _session.StateChanged += (_, _) => _dispatcher.TryEnqueue(OnSessionStateChanged);
        NetworkInformation.NetworkStatusChanged += _ => _dispatcher.TryEnqueue(OnNetworkChanged);
    }

    [ObservableProperty] public partial RootView View { get; private set; } = RootView.Loading;
    [ObservableProperty] public partial string? LoadingMessage { get; private set; } = "Starting…";

    [ObservableProperty] public partial int LibraryVersion { get; private set; }

    public void RefreshLibrary()
    {
        _session.Client?.ClearCache();
        App.Services.GetRequiredService<Services.DetailCache>().Clear();
        App.Services.GetRequiredService<Services.MixService>().Clear();
        App.Services.GetRequiredService<Services.Dislikes>().Invalidate();
        App.Services.GetRequiredService<Services.PlaylistArtistIndex>().Reset();
        App.Services.GetRequiredService<Services.StationService>().Clear();
        Controls.ArtworkMemory.Clear();
        LibraryVersion++;
    }

    public string LibraryTitle => _session.Section?.Title ?? "Music";
    public string ServerName => _session.Server?.Name ?? string.Empty;

    public async Task StartAsync()
    {
        _ = Task.Run(() => _artworkCache.Trim(_settings.Current.ArtworkCacheLimitMb * 1024L * 1024L));
        try
        {
            await Task.Run(() => _session.InitializeAsync());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Startup failed");
            View = RootView.Setup;
        }
    }

    private void OnSessionStateChanged()
    {
        switch (_session.State)
        {
            case SessionState.Starting:
                View = RootView.Loading;
                break;
            case SessionState.Connecting:
                LoadingMessage = _session.StatusMessage;
                if (View != RootView.Main) View = RootView.Loading;
                break;
            case SessionState.SignedOut:
                _player.ClearQueue();
                View = RootView.SignIn;
                break;
            case SessionState.ChooseServer:
            case SessionState.ChooseLibrary:
            case SessionState.NoMusicLibrary:
            case SessionState.ServerUnavailable:
                View = RootView.Setup;
                ScheduleRecovery();
                break;
            case SessionState.Ready:
                _recoveryTimer.Stop();
                _recoveryAttempt = 0;
                var key = $"{_session.Server?.MachineIdentifier}/{_session.Section?.Key}";
                if (key != _lastReadySection)
                {
                    if (_lastReadySection is not null) _player.ClearQueue();
                    _lastReadySection = key;
                    LibraryVersion++;
                    Controls.ArtworkMemory.Clear();
                    App.Services.GetRequiredService<Services.DetailCache>().Clear();
                    App.Services.GetRequiredService<Services.MixService>().Clear();
                    App.Services.GetRequiredService<Services.Dislikes>().Invalidate();
                    App.Services.GetRequiredService<Services.PlaylistArtistIndex>().Reset();
                    App.Services.GetRequiredService<Services.StationService>().Clear();
                    _player.RestoreQueue();
                }
                OnPropertyChanged(nameof(LibraryTitle));
                OnPropertyChanged(nameof(ServerName));
                View = RootView.Main;
                break;
        }
    }

    private void OnNetworkChanged()
    {
        if (_session.Server is null || _session.State == SessionState.SignedOut || (DateTime.UtcNow - _lastNetworkReconnect).TotalSeconds < 10) return;
        _lastNetworkReconnect = DateTime.UtcNow;
        _log.LogInformation("Network status changed; re-evaluating server connection");
        _ = ReconnectAndRefreshAsync(delay: true);
    }

    public void PrepareForSleep()
    {
        if (_resumePlayback) return;
        _resumePlayback = _player.IsPlaying;
        _sleepTrack = _player.CurrentTrack?.RatingKey;
        if (_resumePlayback) _player.Pause();
        _ = _player.SaveQueueAsync();
    }

    public Task ResumeAfterSleepAsync() => ReconnectAndRefreshAsync(delay: true);

    private async Task ReconnectAndRefreshAsync(bool delay)
    {
        if (_reconnecting || _session.Server is null || _session.State == SessionState.SignedOut) return;
        _reconnecting = true;
        _recoveryTimer.Stop();
        try
        {
            if (delay) await Task.Delay(1500);
            if (!await Task.Run(() => _session.ReconnectAsync())) return;
            _session.Client?.MarkStale();
            App.Services.GetRequiredService<MelodayService>().CatchUp();
            App.Services.GetRequiredService<Dislikes>().Invalidate();
            if (_resumePlayback && _player.CurrentTrack?.RatingKey == _sleepTrack)
            {
                _resumePlayback = false;
                _player.Play();
            }
            else _resumePlayback = false;
            await App.Services.GetRequiredService<HomeViewModel>().RefreshAsync();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Background reconnection failed; saved playback is preserved"); }
        finally { _reconnecting = false; ScheduleRecovery(); }
    }

    private void ScheduleRecovery()
    {
        if (_session.State != SessionState.ServerUnavailable || _session.Server is null || _recoveryTimer.IsRunning) return;
        _recoveryTimer.Interval = TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Min(4, _recoveryAttempt++))));
        _recoveryTimer.Start();
    }

    [RelayCommand]
    private async Task RetryConnectionAsync()
    {
        View = RootView.Loading;
        LoadingMessage = "Reconnecting…";
        await Task.Run(() => _session.ReconnectAsync());
    }
}
