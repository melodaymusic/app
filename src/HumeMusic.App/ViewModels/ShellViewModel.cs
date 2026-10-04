using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HumeMusic.Core.Caching;
using HumeMusic.Core.Services;
using HumeMusic.Core.Settings;
using HumeMusic.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Windows.Networking.Connectivity;

namespace HumeMusic.ViewModels;

public enum RootView
{
    Loading,
    SignIn,
    Setup,
    Main,
}

/// <summary>Top-level application state: which root view is shown and connection health.</summary>
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

    public ShellViewModel(PlexSession session, PlayerService player, SettingsStore settings, ArtworkCache artworkCache, ILogger<ShellViewModel> log)
    {
        _session = session;
        _player = player;
        _settings = settings;
        _artworkCache = artworkCache;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _session.StateChanged += (_, _) => _dispatcher.TryEnqueue(OnSessionStateChanged);
        NetworkInformation.NetworkStatusChanged += _ => _dispatcher.TryEnqueue(OnNetworkChanged);
    }

    [ObservableProperty] public partial RootView View { get; private set; } = RootView.Loading;
    [ObservableProperty] public partial string? LoadingMessage { get; private set; } = "Starting…";

    /// <summary>Incremented whenever the active library changes so pages can reload.</summary>
    [ObservableProperty] public partial int LibraryVersion { get; private set; }

    /// <summary>
    /// Brings Meloday Music back in step with the library: everything kept from Plex (on disk and in
    /// memory) is forgotten and the pages load afresh. Artwork and downloads stay.
    /// </summary>
    public void RefreshLibrary()
    {
        _session.Client?.ClearCache();
        App.Services.GetRequiredService<Services.DetailCache>().Clear();
        App.Services.GetRequiredService<Services.MixService>().Clear();
        App.Services.GetRequiredService<Services.Dislikes>().Invalidate();
        App.Services.GetRequiredService<Services.PlaylistArtistIndex>().Reset();
        App.Services.GetRequiredService<Services.StationService>().Clear();
        LibraryVersion++;
    }

    /// <summary>The library's music playlists, listed in the sidebar.</summary>

    public string LibraryTitle => _session.Section?.Title ?? "Music";
    public string ServerName => _session.Server?.Name ?? string.Empty;

    public async Task StartAsync()
    {
        // Housekeeping off the UI thread.
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
                break;
            case SessionState.Ready:
                var key = $"{_session.Server?.MachineIdentifier}/{_session.Section?.Key}";
                if (key != _lastReadySection)
                {
                    if (_lastReadySection is not null) _player.ClearQueue();
                    _lastReadySection = key;
                    LibraryVersion++;
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
        // Network changed (Wi-Fi switch, VPN, sleep/resume): re-select the best connection.
        if (!_session.IsReady || (DateTime.UtcNow - _lastNetworkReconnect).TotalSeconds < 10) return;
        _lastNetworkReconnect = DateTime.UtcNow;
        _log.LogInformation("Network status changed; re-evaluating server connection");
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            await _session.ReconnectAsync();
        });
    }

    [RelayCommand]
    private async Task RetryConnectionAsync()
    {
        View = RootView.Loading;
        LoadingMessage = "Reconnecting…";
        await Task.Run(() => _session.ReconnectAsync());
    }
}
