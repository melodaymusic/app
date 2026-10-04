using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HumeMusic.Core;
using HumeMusic.Core.Caching;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Core.Settings;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace HumeMusic.ViewModels;

/// <summary>One line of Settings' table of what Plex Pass unlocks (see <see cref="SettingsViewModel.PlexPassFeatures"/>).</summary>
public sealed record PlexPassFeature(string Title, string Description, string Without, bool Unlocked)
{
    public string State => Unlocked ? "Unlocked" : "Locked";
    /// <summary>What it does with Plex Pass; without it, what plays instead.</summary>
    public string Detail => Unlocked ? Description : Without;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly ArtworkCache _artwork;
    private readonly ResponseCache _responses;
    private readonly NotificationService _notifications;
    private readonly ArtistLogoService _logos;
    private readonly MelodayService _meloday;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;
    private bool _loading;

    public SettingsViewModel(PlexSession session, SettingsStore settings, ArtworkCache artwork, ResponseCache responses,
        NotificationService notifications, ArtistLogoService logos, MelodayService meloday, ILogger<SettingsViewModel> log)
    {
        _logos = logos;
        _meloday = meloday;
        _session = session;
        _settings = settings;
        _artwork = artwork;
        _responses = responses;
        _notifications = notifications;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _session.StateChanged += (_, _) => _dispatcher.TryEnqueue(Refresh);
    }

    public ObservableCollection<PlexServer> Servers { get; } = [];
    public ObservableCollection<MusicSection> Sections { get; } = [];

    public IReadOnlyList<string> QualityOptions { get; } = ["Original quality (transcode only when needed)", "Always transcode to MP3"];
    public IReadOnlyList<int> BitrateOptions { get; } = [128, 192, 256, 320];
    public IReadOnlyList<string> ThemeOptions { get; } = ["Use Windows setting", "Light", "Dark"];

    [ObservableProperty] public partial PlexServer? SelectedServer { get; set; }
    [ObservableProperty] public partial MusicSection? SelectedSection { get; set; }
    [ObservableProperty] public partial int QualityIndex { get; set; }
    [ObservableProperty] public partial int Bitrate { get; set; }
    [ObservableProperty] public partial bool ReportPlayback { get; set; }
    [ObservableProperty] public partial bool RestoreQueue { get; set; }
    [ObservableProperty] public partial bool Crossfade { get; set; }
    /// <summary>Transitions: 0 none, 1 crossfade, 2 AutoMix.</summary>
    [ObservableProperty] public partial int TransitionIndex { get; set; }
    [ObservableProperty] public partial bool SoundCheck { get; set; }
    [ObservableProperty] public partial bool RemoteControl { get; set; }
    [ObservableProperty] public partial bool TrackNotifications { get; set; }
    [ObservableProperty] public partial bool LyricsFallback { get; set; }
    public IReadOnlyList<string> TransitionOptions { get; } = ["None", "Crossfade", "AutoMix"];
    public bool IsCrossfade => TransitionIndex == 1;
    public string TransitionNote => TransitionIndex switch
    {
        1 => "Each song fades into the next over the same length of time",
        2 => "Songs mix like a DJ set: beats matched and in time, bass swapped on the bar, each blend fitted to how one song ends and the next begins; albums play straight through",
        _ => "Each song ends before the next begins",
    };
    [ObservableProperty] public partial double CrossfadeSeconds { get; set; } = Core.Playback.Crossfade.DefaultSeconds;
    public double CrossfadeMinimum => Core.Playback.Crossfade.MinSeconds;
    public double CrossfadeMaximum => Core.Playback.Crossfade.MaxSeconds;
    public string CrossfadeText => $"{CrossfadeSeconds:0} s";
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial bool MotionEnabled { get; set; } = true;
    public string MotionNote => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled
        ? "Moving artwork, animations and transitions. Off, artwork is still and changes are instant"
        : "Windows' animation effects are off, so the app stays still whatever this is set to";
    [ObservableProperty] public partial string CacheSizeText { get; private set; } = "Calculating…";

    // ------------------------------------------------------------------ Last.fm

    private static LastFmService LastFm => App.Services.GetRequiredService<LastFmService>();
    private bool _lastFmAwaitingApproval;

    public bool LastFmConnected => LastFm.IsConnected;
    public string LastFmUser => LastFm.UserName is { Length: > 0 } name ? $"Scrobbling as {name}" : "Connected";
    public string LastFmButtonText => _lastFmAwaitingApproval ? "I've Approved It" : "Connect";
    [ObservableProperty] public partial string? LastFmStatus { get; private set; }

    /// <summary>
    /// Connecting takes two presses: the first opens last.fm to approve Meloday Music, the second
    /// (once approved) finishes. True when connected.
    /// </summary>
    public async Task<bool> ConnectLastFmAsync(string apiKey, string secret)
    {
        try
        {
            if (!_lastFmAwaitingApproval)
            {
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
                {
                    LastFmStatus = "Enter your Last.fm API key and shared secret.";
                    return false;
                }
                var page = await LastFm.BeginConnectAsync(apiKey, secret);
                await Windows.System.Launcher.LaunchUriAsync(page);
                _lastFmAwaitingApproval = true;
                LastFmStatus = "Approve Meloday Music on the Last.fm page that opened, then press I've Approved It.";
                OnPropertyChanged(nameof(LastFmButtonText));
                return false;
            }
            if (!await LastFm.FinishConnectAsync())
            {
                LastFmStatus = "Last.fm hasn't been approved yet. Approve it on the Last.fm page, then try again.";
                return false;
            }
            _lastFmAwaitingApproval = false;
            LastFmStatus = null;
            OnPropertyChanged(nameof(LastFmButtonText));
            OnPropertyChanged(nameof(LastFmConnected));
            OnPropertyChanged(nameof(LastFmUser));
            return true;
        }
        catch (Exception ex) when (ex is Core.LastFm.LastFmException or HttpRequestException or TaskCanceledException)
        {
            _lastFmAwaitingApproval = false;
            OnPropertyChanged(nameof(LastFmButtonText));
            LastFmStatus = ex is Core.LastFm.LastFmException { Code: 10 or 26 } ? "Last.fm didn't accept that API key." : $"Couldn't reach Last.fm: {ex.Message}";
            return false;
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void DisconnectLastFm()
    {
        LastFm.Disconnect();
        OnPropertyChanged(nameof(LastFmConnected));
    }

    // ------------------------------------------------------------------ keeping in step with the library

    [ObservableProperty] public partial string LibraryCacheText { get; private set; } =
        "Pages open from what Meloday Music already knows, and catch up behind. It checks with Plex whenever your library changes.";

    /// <summary>Forgets everything kept from Plex and loads it afresh.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void RefreshLibrary()
    {
        App.Services.GetRequiredService<ShellViewModel>().RefreshLibrary();
        LibraryCacheText = "Refreshed: everything loads afresh from Plex.";
    }

    /// <summary>Asks Plex to scan the library's folders, then refreshes once it has had a moment.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ScanLibraryAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return;
        try
        {
            await client.ScanLibraryAsync(section.Key);
            LibraryCacheText = "Plex is scanning your music folders. New music appears as it's found; Meloday Music refreshes when Plex says the library changed.";
        }
        catch (PlexException ex)
        {
            LibraryCacheText = $"Plex couldn't start a scan: {ex.Message}";
        }
    }

    private static DownloadService Downloads => App.Services.GetRequiredService<DownloadService>();

    /// <summary>How much is downloaded ("24 songs · 210.4 MB").</summary>
    public string DownloadsText => Downloads.Count == 0 ? "Nothing downloaded"
        : $"{(Downloads.Count == 1 ? "1 song" : $"{Downloads.Count:N0} songs")} · {Helpers.Format.Bytes(Downloads.TotalBytes)}";

    /// <summary>Removes every download, after asking.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RemoveDownloadsAsync()
    {
        if (Downloads.Count == 0) return;
        var dialog = Views.Dialogs.Create("Remove All Downloads?");
        dialog.Content = Views.Dialogs.Text("The songs stay in your Plex library and play from the server as usual; they just won't play offline.");
        dialog.PrimaryButtonText = "Remove All";
        dialog.PrimaryButtonStyle = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["HumeDangerButtonStyle"];
        dialog.CloseButtonText = "Keep Them";
        Views.Dialogs.PreferClose(dialog);
        if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        await Downloads.RemoveAllAsync();
        OnPropertyChanged(nameof(DownloadsText));
    }
    [ObservableProperty] public partial bool IsBusy { get; private set; }

    // Artist logos (fanart.tv). The key itself is never shown again once saved.
    [ObservableProperty] public partial bool HasLogoKey { get; private set; }
    [ObservableProperty] public partial bool ShowArtistLogos { get; set; }
    /// <summary>0: Colour Drop (logos in the page's colour); 1: the logos' own colours.</summary>
    [ObservableProperty] public partial int LogoColoursIndex { get; set; }
    public IReadOnlyList<string> LogoColourOptions { get; } = ["Colour Drop", "Original colours"];
    [ObservableProperty] public partial bool IsCheckingLogoKey { get; private set; }
    [ObservableProperty] public partial string LogoKeyStatus { get; private set; } = string.Empty;
    public bool NeedsLogoKey => !HasLogoKey;
    partial void OnHasLogoKeyChanged(bool value) => OnPropertyChanged(nameof(NeedsLogoKey));

    /// <summary>Checks the key with fanart.tv, then keeps it encrypted. Returns whether it was saved.</summary>
    public async Task<bool> SaveLogoKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        IsCheckingLogoKey = true;
        LogoKeyStatus = "Checking your key with fanart.tv…";
        try
        {
            var result = await _logos.SaveKeyAsync(key);
            LogoKeyStatus = result switch
            {
                Core.Logos.FanartResult.InvalidKey => "fanart.tv didn't accept that key.",
                Core.Logos.FanartResult.Unavailable => "fanart.tv couldn't be reached. Try again in a moment.",
                _ => string.Empty,
            };
            HasLogoKey = _logos.HasKey;
            return result == Core.Logos.FanartResult.Found;
        }
        finally
        {
            IsCheckingLogoKey = false;
        }
    }

    [RelayCommand]
    private void RemoveLogoKey()
    {
        _logos.RemoveKey();
        HasLogoKey = false;
        LogoKeyStatus = string.Empty;
    }

    [ObservableProperty] public partial bool IsImportingLogos { get; private set; }
    [ObservableProperty] public partial string LogoImportStatus { get; private set; } = string.Empty;

    /// <summary>Sets artists' logos from the listener's own folder of logo files.</summary>
    public async Task ImportLogosAsync(string folder)
    {
        IsImportingLogos = true;
        LogoImportStatus = "Importing logos…";
        try
        {
            var result = await App.Services.GetRequiredService<LogoImport>().ImportAsync(folder);
            LogoImportStatus = result switch
            {
                null => "Logos can be imported once your library has loaded.",
                { Found: 0 } => "No logo images were found in that folder.",
                _ => $"Set {result.Imported} of {result.Found} logos."
                     + (result.Unmatched.Count > 0 ? $" {result.Unmatched.Count} had no artist of that name in your library." : ""),
            };
            if (result is { Imported: > 0 } && !ShowArtistLogos) ShowArtistLogos = true;
        }
        finally
        {
            IsImportingLogos = false;
        }
    }

    partial void OnShowArtistLogosChanged(bool value)
    {
        if (_loading) return;
        _logos.SetEnabled(value);
    }

    partial void OnLogoColoursIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        _logos.SetColours(value == 1 ? LogoColours.Original : LogoColours.ColourDrop);
    }

    // ------------------------------------------------------------------ artist rankings

    /// <summary>0: none; 1: the top N (ArtistRankTop); 2: every artist played.</summary>
    [ObservableProperty] public partial int ArtistRankModeIndex { get; set; } = 1;
    [ObservableProperty] public partial double ArtistRankTop { get; set; } = 100;
    public IReadOnlyList<string> ArtistRankOptions { get; } = ["None", "Top artists", "All artists"];
    public bool IsArtistRankTop => ArtistRankModeIndex == 1;

    partial void OnArtistRankModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsArtistRankTop));
        SaveArtistRanks();
    }

    partial void OnArtistRankTopChanged(double value) => SaveArtistRanks();

    private void SaveArtistRanks()
    {
        if (_loading || ArtistRankModeIndex < 0) return;
        var top = double.IsNaN(ArtistRankTop) ? 100 : (int)Math.Clamp(Math.Round(ArtistRankTop), 1, 100_000);
        _settings.Current.ArtistRankLimit = ArtistRankModeIndex switch { 0 => 0, 2 => -1, _ => top };
        _settings.Save();
    }

    // ------------------------------------------------------------------ Meloday

    [ObservableProperty] public partial bool MelodayAiTitles { get; set; }
    [ObservableProperty] public partial bool MelodayPlexPlaylist { get; set; }
    [ObservableProperty] public partial string MelodaySkipGenres { get; set; } = string.Empty;
    [ObservableProperty] public partial string MelodayAvoidWords { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasAiKey { get; private set; }
    [ObservableProperty] public partial bool IsCheckingAiKey { get; private set; }
    [ObservableProperty] public partial string AiKeyStatus { get; private set; } = string.Empty;
    public bool NeedsAiKey => !HasAiKey;
    partial void OnHasAiKeyChanged(bool value) => OnPropertyChanged(nameof(NeedsAiKey));

    partial void OnMelodayAiTitlesChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.MelodayAiTitles = value;
        _settings.Save();
    }

    partial void OnMelodayPlexPlaylistChanged(bool value)
    {
        if (_loading) return;
        // The same switch as on the Meloday page; turned on, the Meloday of now goes to Plex at once.
        _ = App.Services.GetRequiredService<Services.MelodayService>().SetKeepOnPlexAsync(value);
    }

    /// <summary>Saves the genres Meloday skips (comma-separated in the box).</summary>
    public void SaveMelodaySkipGenres()
    {
        _settings.Current.MelodaySkipGenres = Words(MelodaySkipGenres);
        _settings.Save();
        MelodaySkipGenres = string.Join(", ", _settings.Current.MelodaySkipGenres);
    }

    /// <summary>Saves the words AI titles never use (comma-separated in the box).</summary>
    public void SaveMelodayAvoidWords()
    {
        _settings.Current.MelodayAvoidWords = Words(MelodayAvoidWords);
        _settings.Save();
        MelodayAvoidWords = string.Join(", ", _settings.Current.MelodayAvoidWords);
    }

    private static List<string> Words(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Checks the key with OpenAI, then keeps it encrypted. Returns whether it was saved.</summary>
    public async Task<bool> SaveAiKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        IsCheckingAiKey = true;
        AiKeyStatus = "Checking your key with OpenAI\u2026";
        try
        {
            var result = await _meloday.SaveAiKeyAsync(key);
            AiKeyStatus = result switch
            {
                Core.Meloday.OpenAiResult.InvalidKey => "OpenAI didn't accept that key.",
                Core.Meloday.OpenAiResult.Unavailable => "OpenAI couldn't be reached. Try again in a moment.",
                _ => string.Empty,
            };
            HasAiKey = _meloday.HasAiKey;
            return result == Core.Meloday.OpenAiResult.Ok;
        }
        finally
        {
            IsCheckingAiKey = false;
        }
    }

    [RelayCommand]
    private void RemoveAiKey()
    {
        _meloday.RemoveAiKey();
        HasAiKey = false;
        AiKeyStatus = string.Empty;
    }

    public string AccountName => _session.Account?.Name ?? "Not signed in";
    public string PlexPassStatus => "Plex Pass: " + App.Services.GetRequiredService<PlexPass>().Status;

    /// <summary>What Plex Pass unlocks here, locked or unlocked for this account, and what plays instead without it.</summary>
    public IReadOnlyList<PlexPassFeature> PlexPassFeatures
    {
        get
        {
            var has = App.Services.GetRequiredService<PlexPass>().IsAvailable;
            return
            [
                new("Plex's stations", "Library, Deep Cuts and Time Travel radio, and Genre, Style, Mood and Decade radio, made by Plex from its sonic analysis.",
                    "Meloday makes its own Library and Deep Cuts radio, and a radio for every genre, style, mood and decade, from your library.", has),
                new("Artist mixes", "The artist mixes in Mixes for You play Plex's artist radio.",
                    "Meloday builds them from artists near each one: those Plex lists as similar, who share your playlists, and who share their styles and moods.", has),
                new("Sonic Radio", "Radio from any song, album or artist, by how the music sounds.",
                    "Shown as Radio: music from the artist and the artists near them.", has),
                new("Sonic Adventure", "A journey from one song to another, each a small sonic step on from the last.",
                    "Needs Plex Pass: nothing stands in for it.", has),
                new("Meloday's sonic touches", "Meloday's own radio adds songs that sound like your recent ones and orders them as a smooth gradient.",
                    "Meloday still chooses from your listening and tags, without ordering by sound.", has),
                new("Lyrics from Plex", "Synced lyrics from Plex's lyrics provider.",
                    "Lyrics come from LRCLIB instead.", has),
            ];
        }
    }
    public string AccountDetail => _session.Account?.Email ?? _session.Account?.Username ?? string.Empty;
    public string ConnectionText => _session.Client is { } c
        ? $"{c.Connection.Describe()} · {c.Connection.Uri.Host}"
        : "Not connected";
    public string ServerVersion => _session.Server?.ProductVersion is { } v ? $"Plex Media Server {v}" : string.Empty;
    public string Version => $"Version {App.Version}";
    public string DataFolder => AppPaths.Root;

    public void Refresh()
    {
        _loading = true;
        Servers.Clear();
        foreach (var s in _session.Servers) Servers.Add(s);
        SelectedServer = Servers.FirstOrDefault(s => s.MachineIdentifier == _session.Server?.MachineIdentifier);
        Sections.Clear();
        foreach (var s in _session.Sections) Sections.Add(s);
        SelectedSection = Sections.FirstOrDefault(s => s.Key == _session.Section?.Key);

        var c = _settings.Current;
        QualityIndex = c.Quality == StreamQuality.Original ? 0 : 1;
        Bitrate = c.TranscodeBitrateKbps;
        ReportPlayback = c.ReportPlayback;
        RestoreQueue = c.RestoreQueueOnLaunch;
        Crossfade = c.Crossfade;
        TransitionIndex = c.AutoMix ? 2 : c.Crossfade ? 1 : 0;
        SoundCheck = c.SoundCheck;
        RemoteControl = c.RemoteControl;
        TrackNotifications = c.TrackNotifications;
        LyricsFallback = c.LyricsFallback;
        CrossfadeSeconds = c.CrossfadeSeconds;
        ThemeIndex = (int)c.Theme;
        MotionEnabled = c.Motion;
        OnPropertyChanged(nameof(MotionNote));
        ShowArtistLogos = c.ShowArtistLogos;
        LogoColoursIndex = c.LogoColours == LogoColours.Original ? 1 : 0;
        HasLogoKey = _logos.HasKey;
        ArtistRankModeIndex = c.ArtistRankLimit switch { 0 => 0, < 0 => 2, _ => 1 };
        if (c.ArtistRankLimit > 0) ArtistRankTop = c.ArtistRankLimit;
        MelodayAiTitles = c.MelodayAiTitles;
        MelodayPlexPlaylist = c.MelodayPlexPlaylist;
        MelodaySkipGenres = string.Join(", ", c.MelodaySkipGenres);
        MelodayAvoidWords = string.Join(", ", c.MelodayAvoidWords);
        HasAiKey = _meloday.HasAiKey;
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(AccountDetail));
        OnPropertyChanged(nameof(PlexPassStatus));
        OnPropertyChanged(nameof(PlexPassFeatures));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(ServerVersion));
        _loading = false;
        _ = UpdateCacheSizeAsync();
    }

    private async Task UpdateCacheSizeAsync()
    {
        var bytes = await Task.Run(_artwork.SizeBytes);
        CacheSizeText = Helpers.Format.Bytes(bytes);
        OnPropertyChanged(nameof(DownloadsText));
    }

    partial void OnSelectedServerChanged(PlexServer? value)
    {
        if (_loading || value is null || value.MachineIdentifier == _session.Server?.MachineIdentifier) return;
        IsBusy = true;
        _ = Task.Run(() => _session.SelectServerAsync(value)).ContinueWith(_ => _dispatcher.TryEnqueue(() => IsBusy = false));
    }

    partial void OnSelectedSectionChanged(MusicSection? value)
    {
        if (_loading || value is null || value.Key == _session.Section?.Key) return;
        _session.SelectSection(value);
    }

    partial void OnQualityIndexChanged(int value)
    {
        if (_loading) return;
        _settings.Current.Quality = value == 0 ? StreamQuality.Original : StreamQuality.Transcoded;
        _settings.Save();
    }

    partial void OnBitrateChanged(int value)
    {
        if (_loading || value <= 0) return;
        _settings.Current.TranscodeBitrateKbps = value;
        _settings.Save();
    }

    partial void OnReportPlaybackChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.ReportPlayback = value;
        _settings.Save();
    }

    partial void OnTransitionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCrossfade));
        OnPropertyChanged(nameof(TransitionNote));
        if (_loading) return;
        _settings.Current.Crossfade = value == 1;
        _settings.Current.AutoMix = value == 2;
        _settings.Save();
    }

    partial void OnLyricsFallbackChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.LyricsFallback = value;
        _settings.Save();
        App.Services.GetRequiredService<LyricsService>().Clear();
    }

    partial void OnTrackNotificationsChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.TrackNotifications = value;
        _settings.Save();
    }

    partial void OnRemoteControlChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.RemoteControl = value;
        _settings.Save();
        App.Services.GetRequiredService<Services.CompanionService>().Apply();
    }

    partial void OnSoundCheckChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.SoundCheck = value;
        _settings.Save();
    }

    partial void OnCrossfadeChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.Crossfade = value;
        _settings.Save();
    }

    partial void OnCrossfadeSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(CrossfadeText));
        if (_loading) return;
        _settings.Current.CrossfadeSeconds = (int)Math.Round(value);
        _settings.Save();
    }

    partial void OnRestoreQueueChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.RestoreQueueOnLaunch = value;
        _settings.Save();
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        _settings.Current.Theme = (AppTheme)value;
        _settings.Save();
        App.ApplyTheme(_settings.Current.Theme);
    }

    partial void OnMotionEnabledChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.Motion = value;
        _settings.Save();
        Helpers.Motion.Set(value);
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        IsBusy = true;
        try
        {
            await Task.Run(async () =>
            {
                try { await _session.RefreshServersAsync(); }
                catch (PlexException ex) { _log.LogWarning("Server refresh failed: {Message}", ex.Message); }
                _responses.Clear();
                await _session.ReconnectAsync();
            });
            Refresh();
            if (_session.IsReady) _notifications.ShowSuccess("Reconnected", ConnectionText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await Task.Run(() =>
        {
            _artwork.Clear();
            _responses.Clear();
        });
        await UpdateCacheSizeAsync();
        _notifications.ShowSuccess("Cache cleared", "Artwork will be downloaded again as you browse.");
    }

    [RelayCommand]
    private async Task OpenLogsAsync()
    {
        Directory.CreateDirectory(AppPaths.Logs);
        await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.Logs);
    }

    [RelayCommand]
    private async Task SignOutAsync() => await _session.SignOutAsync();
}
