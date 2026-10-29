using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Core;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly ArtworkCache _artwork;
    private readonly ResponseCache _responses;
    private readonly NotificationService _notifications;
    private readonly ArtistLogoService _logos;
    private readonly MelodayService _meloday;
    private readonly Core.Radio.StationFeedbackStore _feedback;
    private readonly Core.Meloday.DupeStore _dupes;
    private readonly MuteService _mutes;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;
    private bool _loading;

    public SettingsViewModel(PlexSession session, SettingsStore settings, ArtworkCache artwork, ResponseCache responses,
        NotificationService notifications, ArtistLogoService logos, MelodayService meloday, Core.Radio.StationFeedbackStore feedback,
        Core.Meloday.DupeStore dupes, MuteService mutes, ILogger<SettingsViewModel> log)
    {
        _dupes = dupes;
        _mutes = mutes;
        _feedback = feedback;
        _logos = logos;
        _meloday = meloday;
        _session = session;
        _settings = settings;
        _artwork = artwork;
        _responses = responses;
        _notifications = notifications;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        RegisterIntegrationEvents();
        Taste.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshTaste);
        MoodTagger.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshMoodTags);
        RefreshMoodTags();
        _session.StateChanged += (_, _) => _dispatcher.TryEnqueue(Refresh);
        _dupes.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshDupes);
        _mutes.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshMuted);
        Analysis.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshAnalysis);
        App.Services.GetRequiredService<MelodayEngine>().Changed += (_, _) => _dispatcher.TryEnqueue(RefreshAnalysis);
    }

    public ObservableCollection<PlexServer> Servers { get; } = [];
    public ObservableCollection<MusicSection> Sections { get; } = [];

    public IReadOnlyList<string> QualityOptions { get; } = ["Original quality (transcode only when needed)", "Always transcode to MP3"];
    public IReadOnlyList<int> BitrateOptions { get; } = [128, 192, 256, 320];
    public IReadOnlyList<string> ThemeOptions { get; } = ["Use Windows setting", "Light", "Dark"];
    public IReadOnlyList<string> InsecureConnectionOptions { get; } = ["Never", "On same network", "Always"];
    [ObservableProperty] public partial int InsecureConnectionIndex { get; set; }
    public string InsecureConnectionNote => InsecureConnectionIndex switch
    {
        1 => "If HTTPS fails, allow an unencrypted connection only on the server's local network.",
        2 => "If HTTPS fails, allow an unencrypted connection on any network. Plex tokens and traffic may be exposed.",
        _ => "Connect using HTTPS. Unencrypted connections are blocked.",
    };

    [ObservableProperty] public partial PlexServer? SelectedServer { get; set; }
    [ObservableProperty] public partial MusicSection? SelectedSection { get; set; }
    [ObservableProperty] public partial int QualityIndex { get; set; }
    [ObservableProperty] public partial int Bitrate { get; set; }
    [ObservableProperty] public partial bool ReportPlayback { get; set; }
    [ObservableProperty] public partial bool RestoreQueue { get; set; }
    [ObservableProperty] public partial bool CloseToTray { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool Crossfade { get; set; }
    [ObservableProperty] public partial int TransitionIndex { get; set; }
    [ObservableProperty] public partial bool SoundCheck { get; set; }
    [ObservableProperty] public partial bool RemoteControl { get; set; }
    [ObservableProperty] public partial bool TrackNotifications { get; set; }
    [ObservableProperty] public partial bool ShowTrends { get; set; }
    [ObservableProperty] public partial bool LyricsFallback { get; set; }
    public IReadOnlyList<string> TransitionOptions { get; } = ["None", "Crossfade", "AutoMix"];
    public bool IsCrossfade => TransitionIndex == 1;
    public string TransitionNote => TransitionIndex switch
    {
        1 => "",
        2 => "Beat-aware transitions. Albums play without mixing.",
        _ => "",
    };
    [ObservableProperty] public partial double CrossfadeSeconds { get; set; } = Core.Playback.Crossfade.DefaultSeconds;
    public double CrossfadeMinimum => Core.Playback.Crossfade.MinSeconds;
    public double CrossfadeMaximum => Core.Playback.Crossfade.MaxSeconds;
    public string CrossfadeText => $"{CrossfadeSeconds:0} s";
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial bool MotionEnabled { get; set; } = true;
    public string MotionNote => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled
        ? ""
        : "Windows' animation effects are off, so the app stays still whatever this is set to";
    [ObservableProperty] public partial string CacheSizeText { get; private set; } = "Calculating…";

    private static LastFmService LastFm => App.Services.GetRequiredService<LastFmService>();
    private bool _lastFmAwaitingApproval;

    public bool LastFmConnected => LastFm.IsConnected;
    public string LastFmUser => LastFm.UserName is { Length: > 0 } name ? $"Connected as {name}" : "Connected";

    public bool LastFmScrobble
    {
        get => LastFm.ScrobblingOn;
        set
        {
            if (value == LastFm.ScrobblingOn) return;
            LastFm.SetScrobbling(value);
            OnPropertyChanged();
        }
    }

    public bool LastFmTrending
    {
        get => LastFm.TrendingOn;
        set
        {
            if (value == LastFm.TrendingOn) return;
            LastFm.SetTrending(value);
            OnPropertyChanged();
        }
    }
    public bool LastFmTrendingArtists
    {
        get => LastFm.TrendingArtistsOn;
        set { LastFm.SetTrendingArtists(value); OnPropertyChanged(); }
    }
    private BackgroundAudioAnalysis Analysis => App.Services.GetRequiredService<BackgroundAudioAnalysis>();
    private MusicIntelligence Intelligence => App.Services.GetRequiredService<MusicIntelligence>();
    public IReadOnlyList<string> AnalysisPaceOptions { get; } = ["Background", "Power through"];
    public int AnalysisPaceIndex
    {
        get => Analysis.FastMode ? 1 : 0;
        set { Analysis.SetFastMode(value == 1); OnPropertyChanged(); OnPropertyChanged(nameof(AnalysisPaceNote)); }
    }
    public string AnalysisPaceNote => Analysis.FastMode
        ? "Faster analysis, using more CPU and network bandwidth. Sampling quality is unchanged."
        : "Lower CPU and network use. Sampling quality is unchanged.";
    public IReadOnlyList<string> MusicEngineOptions { get; } = ["Meloday Engine (Experimental)", "Plex Sonic Analysis"];
    public int MusicEngineIndex
    {
        get => Intelligence.IsMeloday ? 0 : 1;
        set
        {
            Intelligence.Select(value == 1 ? Core.Audio.MusicEngineKind.Plex : Core.Audio.MusicEngineKind.Meloday);
            OnPropertyChanged(); OnPropertyChanged(nameof(MusicEngineNote));
            _ = App.Services.GetRequiredService<HomeViewModel>().RefreshAsync();
        }
    }
    public string MusicEngineNote => Intelligence.IsMeloday
        ? "ME uses its own local sound profiles. It never uses Plex sonic results. While analysis grows, ordinary mixes and radio can use library tags and listening history."
        : Intelligence.IsAvailable ? "Uses Plex Sonic Analysis for sound similarity. ME continues learning in the background so you can switch and compare."
        : "Plex Sonic Analysis requires Plex Pass. Choose Meloday Engine to use local sound analysis without it.";
    public double EnginePercent => Analysis.Summary.Total > 0 ? Math.Min(100, 100.0 * App.Services.GetRequiredService<MelodayEngine>().Count / Analysis.Summary.Total) : 0;
    public string EngineCoverage => Analysis.InventoryKnown
        ? $"{EnginePercent:0.0}% - {App.Services.GetRequiredService<MelodayEngine>().Count:N0} of {Analysis.Summary.Total:N0} tracks have ME sound profiles"
        : "Checking saved ME sound profiles…";
    public double AnalysisPercent => Analysis.Summary.Percent;
    public string AnalysisProgress => Analysis.InventoryKnown
        ? $"{AnalysisPercent:0.0}% of tracks analysed"
        : "Checking saved analysis…";
    public string AnalysisCoverage => Analysis.InventoryKnown
        ? $"{Analysis.Summary.Complete:N0} of {Analysis.Summary.Total:N0} tracks - {Analysis.Summary.Partial:N0} partially analysed"
        : "Counting your library and checking this computer's saved results";
    public string AnalysisStatus => Analysis.Status;
    public string AnalysisTempo => Analysis.Summary.AverageBpm is { } average
        ? $"{average:0} BPM average - {Analysis.Summary.SlowestBpm:0}-{Analysis.Summary.FastestBpm:0} BPM range - {Analysis.Summary.Tempos:N0} tracks with a steady tempo"
        : "No reliable tempos learned yet";
    public string AnalysisKeys => Analysis.Summary.CommonKey is { } key
        ? $"Most common key: {key} - {Analysis.Summary.Keys:N0} tracks with an estimated key"
        : "No reliable musical keys learned yet";
    public string AnalysisRhythm => $"{Analysis.Summary.BeatGrids:N0} tracks with beat grids - {Analysis.Summary.EnergyProfiles:N0} tracks with energy profiles";
    private void RefreshAnalysis()
    {
        foreach (var name in new[] { nameof(AnalysisPercent), nameof(AnalysisProgress), nameof(AnalysisCoverage),
            nameof(AnalysisStatus), nameof(AnalysisTempo), nameof(AnalysisKeys), nameof(AnalysisRhythm), nameof(EnginePercent), nameof(EngineCoverage), nameof(AnalysisPaceIndex), nameof(AnalysisPaceNote) }) OnPropertyChanged(name);
    }

    public bool CanadianBroadcast
    {
        get => _settings.Current.CanadianBroadcast;
        set { _settings.Current.CanadianBroadcast = value; _settings.Save(); OnPropertyChanged(); }
    }
    public string LastFmButtonText => _lastFmAwaitingApproval ? "I've Approved It" : "Connect";
    [ObservableProperty] public partial string? LastFmStatus { get; private set; }

    public async Task<bool> ConnectLastFmAsync(string apiKey, string secret)
    {
        try
        {
            if (!_lastFmAwaitingApproval)
            {
                if ((!LastFm.HasApiKey && string.IsNullOrWhiteSpace(apiKey)) || string.IsNullOrWhiteSpace(secret))
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
            OnPropertyChanged(nameof(LastFmScrobble));
            OnPropertyChanged(nameof(LastFmTrending));
            OnPropertyChanged(nameof(LastFmTrendingArtists));
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
        _lastFmAwaitingApproval = false;
        LastFmStatus = null;
        LastFm.Disconnect();
        OnPropertyChanged(nameof(LastFmButtonText));
        OnPropertyChanged(nameof(LastFmHasApiKey));
        OnPropertyChanged(nameof(LastFmConnected));
        OnPropertyChanged(nameof(LastFmScrobble));
        OnPropertyChanged(nameof(LastFmTrending));
            OnPropertyChanged(nameof(LastFmTrendingArtists));
    }

    [ObservableProperty] public partial string LibraryCacheText { get; private set; } =
        "Pages open from what Meloday Music already knows, and catch up behind. It checks with Plex whenever your library changes.";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void RefreshLibrary()
    {
        App.Services.GetRequiredService<ShellViewModel>().RefreshLibrary();
        LibraryCacheText = "Refreshed: everything loads afresh from Plex.";
    }

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

    public string DownloadsText => Downloads.Count == 0 ? "Nothing downloaded"
        : $"{(Downloads.Count == 1 ? "1 song" : $"{Downloads.Count:N0} songs")} · {Helpers.Format.Bytes(Downloads.TotalBytes)}";

    private static TasteService Taste => App.Services.GetRequiredService<TasteService>();
    [ObservableProperty] public partial double TasteAdventure { get; set; }
    public string TasteAdventureText => TasteAdventure switch { < 25 => "Familiar", < 60 => "Balanced", _ => "Adventurous" };
    [ObservableProperty] public partial string TasteSummary { get; private set; } = string.Empty;
    [ObservableProperty] public partial string TasteLately { get; private set; } = string.Empty;

    partial void OnTasteAdventureChanged(double value)
    {
        OnPropertyChanged(nameof(TasteAdventureText));
        if (_loading) return;
        Taste.SetAdventure(value / 100);
    }

    private void RefreshTaste()
    {
        var engine = Taste.Engine;
        var plays = engine.Profile.Plays;
        var songs = Taste.Memory.Songs;
        TasteSummary = plays == 0 && songs == 0
            ? "Nothing yet. It learns from your Plex listening history once your library is connected, and from what you skip, finish and choose here."
            : $"From {plays:N0} plays in your Plex history, and {songs:N0} {(songs == 1 ? "song" : "songs")} you've skipped, played through or chosen here.";
        var artists = engine.Profile.TopArtists(3).Select(a => a.Name).ToList();
        var tags = engine.Profile.TopTags(3).Select(t => t.Name.ToLowerInvariant()).ToList();
        TasteLately = artists.Count == 0 ? "" : $"Lately: {string.Join(", ", artists)}{(tags.Count > 0 ? $" · {string.Join(", ", tags)}" : "")}";
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ForgetTasteAsync()
    {
        var dialog = Views.Dialogs.Create("Forget What Meloday Learnt?");
        dialog.Content = Views.Dialogs.Text("Meloday forgets the songs you've skipped, played through and chosen on this PC. Your Plex history, plays and ratings stay, so it still knows your taste from those.");
        dialog.PrimaryButtonText = "Forget";
        dialog.PrimaryButtonStyle = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["MelodayDangerButtonStyle"];
        dialog.CloseButtonText = "Keep It";
        Views.Dialogs.PreferClose(dialog);
        if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        Taste.Forget();
        RefreshTaste();
    }

    private static MoodTagService MoodTagger => App.Services.GetRequiredService<MoodTagService>();
    [ObservableProperty] public partial string MoodTagStatus { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool CanTagMoods { get; private set; }
    [ObservableProperty] public partial bool CanUndoMoods { get; private set; }
    [ObservableProperty] public partial bool IsMoodTagging { get; private set; }

    private void RefreshMoodTags()
    {
        var tagger = MoodTagger;
        IsMoodTagging = tagger.IsRunning;
        CanTagMoods = !tagger.IsRunning;
        CanUndoMoods = !tagger.IsRunning && tagger.Tagged > 0;
        MoodTagStatus = tagger.IsRunning ? tagger.Progress
            : tagger.Tagged == 0 ? "Meloday hasn't added moods to any songs yet."
            : $"Meloday has added moods to {tagger.Tagged:N0} {(tagger.Tagged == 1 ? "song" : "songs")} in this library.";
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task TagLibraryMoodsAsync()
    {
        var dialog = Views.Dialogs.Create("Tag Your Whole Library?");
        dialog.Content = Views.Dialogs.Text("Meloday adds the moods the Meloday Engine hears clearly to every analysed song in Plex. Moods your songs already have stay, and you can take everything Meloday added back out at any time. It runs in the background and can be stopped.");
        dialog.PrimaryButtonText = "Tag Library";
        dialog.CloseButtonText = "Not Now";
        Views.Dialogs.PreferClose(dialog);
        if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        var changed = await MoodTagger.TagLibraryAsync();
        _notifications.ShowSuccess("Moods added in Plex", $"{changed:N0} {(changed == 1 ? "song now carries" : "songs now carry")} the moods Meloday hears.");
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task UndoLibraryMoodsAsync()
    {
        var dialog = Views.Dialogs.Create("Remove All of Meloday's Moods?");
        dialog.Content = Views.Dialogs.Text($"Meloday takes the moods it added back out of {MoodTagger.Tagged:N0} songs in Plex and unlocks them where they weren't locked before. Moods that were already there stay.");
        dialog.PrimaryButtonText = "Remove";
        dialog.PrimaryButtonStyle = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["MelodayDangerButtonStyle"];
        dialog.CloseButtonText = "Keep Them";
        Views.Dialogs.PreferClose(dialog);
        if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        var changed = await MoodTagger.UndoLibraryAsync();
        _notifications.ShowInfo("Meloday's moods removed", $"{changed:N0} {(changed == 1 ? "song is" : "songs are")} back as they were.");
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void StopMoodTagging() => MoodTagger.Cancel();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RemoveDownloadsAsync()
    {
        if (Downloads.Count == 0) return;
        var dialog = Views.Dialogs.Create("Remove All Downloads?");
        dialog.Content = Views.Dialogs.Text("The songs stay in your Plex library and play from the server as usual; they just won't play offline.");
        dialog.PrimaryButtonText = "Remove All";
        dialog.PrimaryButtonStyle = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["MelodayDangerButtonStyle"];
        dialog.CloseButtonText = "Keep Them";
        Views.Dialogs.PreferClose(dialog);
        if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        await Downloads.RemoveAllAsync();
        OnPropertyChanged(nameof(DownloadsText));
    }
    [ObservableProperty] public partial bool IsBusy { get; private set; }

    [ObservableProperty] public partial bool HasLogoKey { get; private set; }
    [ObservableProperty] public partial bool ShowArtistLogos { get; set; }
    [ObservableProperty] public partial int LogoColoursIndex { get; set; }
    public IReadOnlyList<string> LogoColourOptions { get; } = ["Colour Drop", "Original colours"];
    [ObservableProperty] public partial bool IsCheckingLogoKey { get; private set; }
    [ObservableProperty] public partial string LogoKeyStatus { get; private set; } = string.Empty;
    public bool NeedsLogoKey => !HasLogoKey;
    partial void OnHasLogoKeyChanged(bool value) => OnPropertyChanged(nameof(NeedsLogoKey));

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
        _ = App.Services.GetRequiredService<Services.MelodayService>().SetKeepOnPlexAsync(value);
    }

    public void SaveMelodaySkipGenres()
    {
        _settings.Current.MelodaySkipGenres = Words(MelodaySkipGenres);
        _settings.Save();
        MelodaySkipGenres = string.Join(", ", _settings.Current.MelodaySkipGenres);
    }

    public void SaveMelodayAvoidWords()
    {
        _settings.Current.MelodayAvoidWords = Words(MelodayAvoidWords);
        _settings.Save();
        MelodayAvoidWords = string.Join(", ", _settings.Current.MelodayAvoidWords);
    }

    private static List<string> Words(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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

    public string AccountDetail => _session.ProfileUuid is not null && _session.Owner is { } owner
        ? $"Plex Home profile · signed in as {owner.Name}"
        : _session.Account?.Email ?? _session.Account?.Username ?? string.Empty;
    public string ConnectionText => _session.Client is { } c
        ? $"{c.Connection.Describe()} · {(c.BaseUri.Scheme == "https" ? "Secure (HTTPS)" : "Unencrypted (HTTP)")} · {c.Connection.Uri.Host}"
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
        InsecureConnectionIndex = (int)c.AllowInsecureConnections;
        OnPropertyChanged(nameof(InsecureConnectionNote));
        QualityIndex = c.Quality == StreamQuality.Original ? 0 : 1;
        Bitrate = c.TranscodeBitrateKbps;
        ReportPlayback = c.ReportPlayback;
        RestoreQueue = c.RestoreQueueOnLaunch;
        CloseToTray = c.CloseToTray;
        try { StartWithWindows = WindowsStartup.IsEnabled; }
        catch (Exception ex) { _log.LogWarning(ex, "Startup setting unavailable"); }
        Crossfade = c.Crossfade;
        TransitionIndex = c.AutoMix ? 2 : c.Crossfade ? 1 : 0;
        SoundCheck = c.SoundCheck;
        RemoteControl = c.RemoteControl;
        TrackNotifications = c.TrackNotifications;
        ShowTrends = c.ShowTrends;
        TasteAdventure = Math.Round(c.TasteAdventure * 100);
        RefreshTaste();
        LyricsFallback = c.LyricsFallback;
        RefreshFeedback();
        RefreshDupes();
        MuteLengthIndex = (int)c.MuteLength;
        MuteCustomDays = c.MuteCustomDays;
        RefreshMuted();
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

    partial void OnShowTrendsChanged(bool value)
    {
        if (_loading) return;
        App.Services.GetRequiredService<ListeningService>().SetTrendsShown(value);
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

    partial void OnInsecureConnectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(InsecureConnectionNote));
        if (_loading || value is < 0 or > 2) return;
        _settings.Current.AllowInsecureConnections = (InsecureConnections)value;
        _settings.Save();
        if (_session.Client is { } client && !ConnectionSecurity.Allows(client.Connection, (InsecureConnections)value))
            App.Services.GetRequiredService<PlayerService>().Stop();
        _ = ReconnectAsync();
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.CloseToTray = value;
        _settings.Save();
        App.MainWindow?.ApplyTraySetting();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        try { WindowsStartup.Set(value); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't change Windows startup");
            _loading = true;
            StartWithWindows = !value;
            _loading = false;
            _notifications.ShowInfo("Couldn't change startup", "Windows didn't allow this change. Try again.");
        }
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

    [ObservableProperty] public partial string FeedbackSummary { get; private set; } = string.Empty;

    private void RefreshFeedback()
    {
        var count = _feedback.Count;
        FeedbackSummary = count == 0 ? "No songs taken off live stations yet" : count == 1 ? "1 song taken off a live station" : $"{count} songs taken off live stations";
    }

    public IReadOnlyList<string> MuteLengthOptions { get; } = ["1 week", "1 month", "Custom"];

    [ObservableProperty] public partial int MuteLengthIndex { get; set; }
    [ObservableProperty] public partial double MuteCustomDays { get; set; } = 14;
    public bool IsCustomMute => MuteLengthIndex == (int)Core.Mutes.MuteLength.Custom;

    partial void OnMuteLengthIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCustomMute));
        OnPropertyChanged(nameof(MuteLengthSummary));
        SaveMuteLength();
    }

    partial void OnMuteCustomDaysChanged(double value) => SaveMuteLength();

    private void SaveMuteLength()
    {
        if (_loading || MuteLengthIndex < 0) return;
        _settings.Current.MuteLength = (Core.Mutes.MuteLength)MuteLengthIndex;
        if (!double.IsNaN(MuteCustomDays)) _settings.Current.MuteCustomDays = (int)Math.Clamp(Math.Round(MuteCustomDays), Core.Mutes.MuteLengths.Shortest, Core.Mutes.MuteLengths.Longest);
        _settings.Save();
        OnPropertyChanged(nameof(MuteLengthSummary));
    }

    public string MuteLengthSummary => IsCustomMute
        ? $"A mute lasts {_mutes.DefaultText}. The Mute menu on Now Playing has Another Length for just once."
        : "The Mute menu on Now Playing has Another Length, for a different time just once.";

    public ObservableCollection<MuteRow> Muted { get; } = [];

    [ObservableProperty] public partial bool HasMuted { get; private set; }
    [ObservableProperty] public partial string MutedSummary { get; private set; } = string.Empty;

    private void RefreshMuted()
    {
        Muted.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _mutes.Active()) Muted.Add(new MuteRow(entry, now));
        HasMuted = Muted.Count > 0;
        MutedSummary = Muted.Count == 0 ? "Nothing is muted" : Muted.Count == 1 ? "1 muted" : $"{Muted.Count} muted";
        OnPropertyChanged(nameof(MuteLengthSummary));
    }

    [RelayCommand]
    private void Unmute(MuteRow? row)
    {
        if (row is not null) _mutes.Unmute(row.Entry);
    }

    public ObservableCollection<DupeRow> Dupes { get; } = [];

    [ObservableProperty] public partial bool HasDupes { get; private set; }
    [ObservableProperty] public partial string DupesSummary { get; private set; } = string.Empty;

    private void RefreshDupes()
    {
        Dupes.Clear();
        if (_session.Server is { } server)
            foreach (var pair in _dupes.For(server.MachineIdentifier)) Dupes.Add(new DupeRow(pair));
        HasDupes = Dupes.Count > 0;
        DupesSummary = Dupes.Count == 0 ? "No dupes yet" : Dupes.Count == 1 ? "1 pair" : $"{Dupes.Count} pairs";
    }

    [RelayCommand]
    private void Unmerge(DupeRow? row)
    {
        if (row is not null) _dupes.Remove(row.Pair.Server, row.Pair.Dupe.Key);
    }

    [RelayCommand]
    private void ResetFeedback()
    {
        _feedback.Clear();
        RefreshFeedback();
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
    private async Task ClearLogsAsync()
    {
        try
        {
            await Task.Run(() => App.Services.GetRequiredService<Core.Logging.FileLoggerProvider>().ClearLogs());
            _notifications.ShowSuccess("Logs cleared", "Old diagnostic logs have been deleted. New diagnostics remain on this PC.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.ShowError("Couldn't clear logs", "A log file is in use or its folder cannot be written.");
        }
    }

    [RelayCommand]
    private async Task SignOutAsync() => await _session.SignOutAsync();

    [RelayCommand]
    private Task SwitchProfileAsync() => Views.ProfileDialog.ShowAsync();
}

public sealed class MuteRow(Core.Mutes.MuteEntry entry, DateTimeOffset now)
{
    public Core.Mutes.MuteEntry Entry { get; } = entry;
    public string Icon => Entry.Kind == Core.Mutes.MuteKind.Artist ? "MicVocal" : "Music";
    public string Title => Entry.Title;
    public string Kind => Entry.Kind == Core.Mutes.MuteKind.Artist ? "Artist" : string.IsNullOrEmpty(Entry.Detail) ? "Song" : $"Song · {Entry.Detail}";
    public string Remaining => $"{Core.Mutes.MuteLengths.Remaining(Entry.Until, now)} · until {Entry.Until.LocalDateTime:MMM d}";
    public string Unmute => $"Unmute {Entry.Title}";
}

public sealed class DupeRow(Core.Meloday.DupePair pair)
{
    public Core.Meloday.DupePair Pair { get; } = pair;
    public string? KeeperThumb => Pair.Keeper.Thumb;
    public string? DupeThumb => Pair.Dupe.Thumb;
    public string KeeperTitle => Pair.Keeper.Title;
    public string DupeTitle => Pair.Dupe.Title;
    public string KeeperDetails => Details(Pair.Keeper);
    public string DupeDetails => Details(Pair.Dupe);
    public string Unmerge => $"Unmerge {Pair.Dupe.Title} from {Pair.Keeper.Title}";

    private static string Details(Core.Meloday.DupeSide side) =>
        string.IsNullOrEmpty(side.Album) ? side.Artist : $"{side.Artist} · {side.Album}";
}
