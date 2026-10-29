using System.Net;
using MelodayMusic.Core;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Logging;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace MelodayMusic;

public partial class App : Application
{
    private static IServiceProvider? _services;
    private MainWindow? _window;
    private ILogger? _log;
    private Mutex? _installMutex;
    public static void ReleaseInstallMutex() => (Current as App)?._installMutex?.Dispose();

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log?.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => _log?.LogCritical(e.ExceptionObject as Exception, "Fatal unhandled exception");
    }

    public static IServiceProvider Services => _services ?? throw new InvalidOperationException("Services not initialised");

    public static MainWindow? MainWindow => (Current as App)?._window;

    public static string Version =>
        typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            WaitForEarlierInstance();
            var instance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("MelodayMusic");
            if (!instance.IsCurrent)
            {
                await instance.RedirectActivationToAsync(Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs());
                Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--quit", StringComparer.OrdinalIgnoreCase)) { Exit(); return; }
            instance.Activated += (_, activation) => _window?.DispatcherQueue.TryEnqueue(() =>
            {
                if (activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
                    && System.Text.RegularExpressions.Regex.IsMatch(launch.Arguments, @"(?:^|\s)--quit(?:\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    _window.Quit();
                else _window.ShowFromTray();
            });
            Launch();
        }
        catch (Exception ex)
        {
            _log?.LogCritical(ex, "Startup failed");
            _services?.GetService<FileLoggerProvider>()?.Dispose();
            Environment.FailFast("Meloday Music failed to start: " + ex.Message, ex);
        }
    }

    /// <summary>After a profile switch the new instance is started with --after and the old one's process id; it waits for that one to finish closing.</summary>
    private static void WaitForEarlierInstance()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(args, a => a.Equals("--after", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var id)) return;
        try
        {
            using var earlier = System.Diagnostics.Process.GetProcessById(id);
            earlier.WaitForExit(15000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
    }

    /// <summary>Reads which Plex Home member was last in use, so their own settings and history are the ones loaded.</summary>
    private static void UseStoredProfile()
    {
        var stored = new DpapiCredentialStore(AppPaths.Credentials, Microsoft.Extensions.Logging.Abstractions.NullLogger<DpapiCredentialStore>.Instance).Load();
        try
        {
            AppPaths.UseProfile(stored?.ProfileUuid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.UseProfile(null);
        }
    }

    private void Launch()
    {
        _installMutex = new Mutex(false, @"Local\MelodayMusic-" + Environment.UserName);
        UseStoredProfile();
        _services = ConfigureServices();
        _log = Services.GetRequiredService<ILogger<App>>();
        _log.LogInformation("Meloday Music {Version} starting on Windows {Os}", Version, Environment.OSVersion.Version);

        try
        {
            using var fonts = new Microsoft.Graphics.Canvas.Text.CanvasFontSet(
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Nunito.ttf")));
            if (fonts.Fonts.Count == 0) _log.LogWarning("The bundled Nunito typeface has no faces");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "The bundled Nunito typeface could not be loaded");
        }

        UiScale.Apply(UiScale.Default);
        Helpers.Motion.Initialize(Services.GetRequiredService<SettingsStore>().Current.Motion);
        _window = new MainWindow();
        var navigation = Services.GetRequiredService<NavigationService>();
        Services.GetRequiredService<PlayerService>().PageItem = () => navigation.CurrentItem;
        // The session raises this from whatever thread its plex.tv call finished on; only the queue is safe to touch from there.
        var ui = _window.DispatcherQueue;
        Services.GetRequiredService<PlexSession>().ProfileChanged += (_, _) => ui.TryEnqueue(() => _window?.Restart());
        ApplyTheme(Services.GetRequiredService<SettingsStore>().Current.Theme);
        _window.Activate();
        _ = Services.GetRequiredService<ShellViewModel>().StartAsync();
        Services.GetRequiredService<MelodayService>();
        Services.GetRequiredService<CompanionService>().Apply();
        Services.GetRequiredService<TrackNotifier>();
        Services.GetRequiredService<LastFmService>();
        Services.GetRequiredService<ListenBrainzService>();
        Services.GetRequiredService<ListeningService>();
        Services.GetRequiredService<TasteService>();
        Services.GetRequiredService<MadePlaylistService>();
        Services.GetRequiredService<TrendingService>();
        Services.GetRequiredService<BackgroundAudioAnalysis>();
        ImportLogosFromCommandLine();
        RunUiSmoke();
    }

    partial void RunUiSmoke();

    private void ImportLogosFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(args, a => a.Equals("--import-logos", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= args.Length) return;
        var folder = args[at + 1];
        var session = Services.GetRequiredService<Core.Services.PlexSession>();
        var started = 0;
        async void Run()
        {
            if (!session.IsReady || Interlocked.Exchange(ref started, 1) == 1) return;
            session.StateChanged -= OnState;
            var result = await Services.GetRequiredService<LogoImport>().ImportAsync(folder);
            _log?.LogInformation("Logo import from the command line: {Imported} of {Found} set, {Unmatched} unmatched",
                result?.Imported ?? 0, result?.Found ?? 0, result?.Unmatched.Count ?? 0);
        }
        void OnState(object? sender, EventArgs e) => Run();
        session.StateChanged += OnState;
        Run();
    }

    public static void ApplyTheme(AppTheme theme)
    {
        MainWindow?.ApplyTheme(theme);
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var fileLogger = new FileLoggerProvider(AppPaths.Logs,
#if DEBUG
            LogLevel.Debug
#else
            LogLevel.Information
#endif
        );
        services.AddSingleton(fileLogger);
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddProvider(fileLogger);
            b.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton(sp => new SettingsStore(AppPaths.Settings, sp.GetRequiredService<ILogger<SettingsStore>>()));
        services.AddSingleton(sp => new PlexClientIdentity(
            sp.GetRequiredService<SettingsStore>().Current.ClientIdentifier,
            Version,
            Environment.MachineName,
            Environment.OSVersion.Version.ToString()));

        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 12,
        })
        { Timeout = Timeout.InfiniteTimeSpan });

        services.AddKeyedSingleton<HttpClient>("plex", (_, _) => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 12,
        }) { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton(sp => new PlexHttp(sp.GetRequiredKeyedService<HttpClient>("plex"), sp.GetRequiredService<PlexClientIdentity>(), sp.GetRequiredService<ILogger<PlexHttp>>()));
        services.AddSingleton<PlexTvClient>();
        services.AddSingleton<ConnectionSelector>();
        services.AddSingleton<ResponseCache>(_ => new ResponseCache(directory: Path.Combine(AppPaths.Root, "cache", "responses")));
        services.AddSingleton<ICredentialStore>(sp => new DpapiCredentialStore(AppPaths.Credentials, sp.GetRequiredService<ILogger<DpapiCredentialStore>>()));
        services.AddSingleton<PlexSession>();
        services.AddSingleton(sp => new ArtworkCache(AppPaths.ArtworkCache, sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ILogger<ArtworkCache>>()));

        services.AddSingleton(sp => new Core.Pins.PinStore(AppPaths.Pins, sp.GetRequiredService<ILogger<Core.Pins.PinStore>>()));
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AmbientService>();
        services.AddSingleton<DetailCache>();
        services.AddSingleton(_ => new Core.Mutes.MuteStore(AppPaths.Mutes));
        services.AddSingleton<Dislikes>();
        services.AddSingleton<PlexPass>();
        services.AddSingleton(_ => new Core.Meloday.DupeStore(AppPaths.MelodayDupes));
        services.AddSingleton(sp => new MelodayService(
            sp.GetRequiredService<Core.Services.PlexSession>(), sp.GetRequiredService<PlexPass>(), sp.GetRequiredService<MusicIntelligence>(), sp.GetRequiredService<Dislikes>(),
            sp.GetRequiredService<Core.Settings.SettingsStore>(), sp.GetRequiredService<Core.Meloday.DupeStore>(), sp.GetRequiredService<Core.Mutes.MuteStore>(),
            new Core.Security.ProtectedSecret(AppPaths.OpenAiKey, "OpenAI", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            sp.GetRequiredService<TasteService>(), sp.GetRequiredService<ILogger<MelodayService>>()));
        services.AddSingleton<StationService>();
        services.AddSingleton<ArtistRanks>();
        services.AddSingleton<PlaylistCatalog>();
        services.AddSingleton<PlaylistArtistIndex>();
        services.AddSingleton<PlexLogoService>();
        services.AddSingleton(sp => new ArtistLogoService(
            new Core.Security.ProtectedSecret(AppPaths.FanartKey, "Fanart", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            sp.GetRequiredService<Core.Settings.SettingsStore>(), sp.GetRequiredService<PlexLogoService>(), sp.GetRequiredService<ILogger<ArtistLogoService>>()));
        services.AddSingleton<LogoImport>();
        services.AddSingleton<TagCovers>();
        services.AddSingleton<StaticCovers>();
        services.AddSingleton<ShapeCovers>();
        services.AddSingleton<VisualizerService>();
        services.AddSingleton<StageService>();
        services.AddSingleton(_ => new Core.Home.ListeningClock(System.IO.Path.Combine(AppPaths.Root, "listening.json")));
        services.AddSingleton(sp => new Core.Trends.ListeningLog(System.IO.Path.Combine(AppPaths.Root, "listening-log.jsonl"), log: sp.GetRequiredService<ILogger<Core.Trends.ListeningLog>>()));
        services.AddSingleton<ListeningService>();
        services.AddSingleton(sp => new Core.Home.RecentStore(System.IO.Path.Combine(AppPaths.Root, "recent.json"), sp.GetRequiredService<ILogger<Core.Home.RecentStore>>()));
        services.AddSingleton(_ => new Core.Taste.Exposure(Path.Combine(AppPaths.Root, "taste", "exposure.json")));
        services.AddSingleton(_ => new Core.Taste.TasteExclusions(Path.Combine(AppPaths.Root, "taste", "excluded.json")));
        services.AddSingleton(_ => new Core.Taste.TasteMemory(Path.Combine(AppPaths.Root, "taste", "memory.json"), legacyPath: Path.Combine(AppPaths.Root, "radio", "skips.json")));
        services.AddSingleton<TasteService>();
        services.AddSingleton<MadePlaylistService>();
        services.AddSingleton<MoodTagService>();
        services.AddSingleton(_ => new Core.Radio.LiveScheduleStore(AppPaths.LiveStations));
        services.AddSingleton(_ => new Core.Radio.PulseScheduleStore(AppPaths.PulseSchedule));
        services.AddSingleton(_ => new Core.Radio.RadioPlaybackStore(Path.Combine(AppPaths.Root, "radio", "played.json")));
        services.AddSingleton(_ => new Core.Radio.StationFeedbackStore(AppPaths.StationFeedback));
        services.AddSingleton(_ => new Core.Radio.HeadlinerHistory(AppPaths.HeadlinerHistory));
        services.AddSingleton<LiveRadio>();
        services.AddSingleton<LocalRadio>();
        services.AddSingleton<PulseScheduleService>();
        services.AddSingleton<MixService>();
        services.AddSingleton<HomeLayoutService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ArtworkService>();
        services.AddSingleton<MediaPlayerEngine>();
        services.AddSingleton<MixEngine>();
        services.AddSingleton<AudioAnalysisService>();
        services.AddSingleton<BackgroundAudioAnalysis>();
        services.AddSingleton<MelodayEngine>();
        services.AddSingleton<MusicIntelligence>();
        services.AddSingleton<RoutingEngine>();
        services.AddSingleton<IPlaybackEngine>(sp => sp.GetRequiredService<RoutingEngine>());
        services.AddSingleton<CastService>();
        services.AddSingleton<PlaybackReporter>();
        services.AddSingleton<QueueStore>();
        services.AddSingleton<TransitionPlanner>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<FeaturedArtistService>();
        services.AddSingleton<PanelService>();
        services.AddSingleton<MiniPlayerService>();
        services.AddSingleton<SleepTimerService>();
        services.AddSingleton<MuteService>();
        services.AddSingleton<CompanionService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<TrackNotifier>();
        services.AddSingleton<TagHighlights>();
        services.AddSingleton<TaskbarPreview>();
        services.AddSingleton<TaskbarButtons>();
        services.AddSingleton(sp => new LastFmService(
            new Core.Security.ProtectedSecret(Path.Combine(AppPaths.Root, "lastfm.bin"), "LastFm", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            sp.GetRequiredService<Core.Settings.SettingsStore>(), sp.GetRequiredKeyedService<HttpClient>("integrations"), sp.GetRequiredService<PlayerService>(), sp.GetRequiredService<ILogger<LastFmService>>()));
        services.AddSingleton<TrendingService>();
        services.AddKeyedSingleton<HttpClient>("integrations", (_, _) => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { Timeout = TimeSpan.FromSeconds(25) });
        services.AddSingleton<CatalogueService>();
        services.AddSingleton(sp => new ListenBrainzService(
            new Core.Security.ProtectedSecret(Path.Combine(AppPaths.Root, "listenbrainz.bin"), "ListenBrainz", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            new Core.Security.ProtectedSecret(Path.Combine(AppPaths.Root, "listenbrainz-pending.bin"), "ListenBrainzPending", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            sp.GetRequiredService<Core.Settings.SettingsStore>(), sp.GetRequiredKeyedService<HttpClient>("integrations"),
            sp.GetRequiredService<PlayerService>(), sp.GetRequiredService<ILogger<ListenBrainzService>>()));
        services.AddSingleton<EssentialsService>();
        services.AddSingleton<PlayerService>();
        services.AddSingleton<LibraryActions>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<SignInViewModel>();
        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<RadioViewModel>();
        services.AddSingleton<MelodayViewModel>();
        services.AddSingleton<ArtistsViewModel>();
        services.AddSingleton<AlbumsViewModel>();
        services.AddSingleton<TracksViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        services.AddSingleton<CollectionsViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ArtistViewModel>();
        services.AddTransient<AlbumViewModel>();
        services.AddTransient<PlaylistViewModel>();
        services.AddTransient<FoldersViewModel>();
        services.AddTransient<TagViewModel>();
        services.AddTransient<CollectionViewModel>();

        return services.BuildServiceProvider();
    }

    private static string CurrentPageName()
    {
        try { return _services?.GetService<NavigationService>()?.CurrentPage?.Name ?? "no page"; }
        catch { return "unknown"; }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _log?.LogError(e.Exception, "Unhandled UI exception on {Page} at {Width}x{Height}, interface size {Scale:P0}", CurrentPageName(),
            (int)(_window?.Bounds.Width ?? 0), (int)(_window?.Bounds.Height ?? 0), UiScale.Factor);
        if (_window is null) return;
        e.Handled = true;
        try
        {
            Services.GetRequiredService<NotificationService>().ShowError("Something went wrong", "Meloday Music recovered from an unexpected error.");
        }
        catch
        {
        }
    }
}
