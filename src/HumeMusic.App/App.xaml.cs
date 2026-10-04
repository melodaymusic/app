using System.Net;
using HumeMusic.Core;
using HumeMusic.Core.Caching;
using HumeMusic.Core.Logging;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Security;
using HumeMusic.Core.Services;
using HumeMusic.Core.Settings;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace HumeMusic;

public partial class App : Application
{
    private static IServiceProvider? _services;
    private MainWindow? _window;
    private ILogger? _log;

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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Launch();
        }
        catch (Exception ex)
        {
            _log?.LogCritical(ex, "Startup failed");
            Services.GetService<FileLoggerProvider>()?.Dispose();
            Environment.FailFast("Meloday Music failed to start: " + ex.Message, ex);
        }
    }

    private void Launch()
    {
        _services = ConfigureServices();
        _log = Services.GetRequiredService<ILogger<App>>();
        _log.LogInformation("Meloday Music {Version} starting on Windows {Os}", Version, Environment.OSVersion.Version);

        // Design tokens depend on the user's interface size, so they are published before any UI exists.
        UiScale.Apply(UiScale.Default);
        Helpers.Motion.Initialize(Services.GetRequiredService<SettingsStore>().Current.Motion);
        _window = new MainWindow();
        // The saved theme is applied to the window's root, so the window must exist first.
        ApplyTheme(Services.GetRequiredService<SettingsStore>().Current.Theme);
        _window.Activate();
        _ = Services.GetRequiredService<ShellViewModel>().StartAsync();
        // Meloday prepares each part of the day's mix in the background once the library is ready.
        Services.GetRequiredService<MelodayService>();
        // Plex apps (a phone, say) can find and control this player once it's signed in.
        Services.GetRequiredService<CompanionService>().Apply();
        Services.GetRequiredService<TrackNotifier>();
        Services.GetRequiredService<LastFmService>();
        ImportLogosFromCommandLine();
    }

    /// <summary>
    /// <c>MelodayMusic.exe --import-logos "folder"</c> sets artists' logos from the listener's own files
    /// once the library is ready, as Settings' Import Logos does.
    /// </summary>
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

        // One shared HttpClient for Plex API calls. Plex servers are reached via plex.direct
        // HTTPS certificates or plain HTTP on the local network.
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 12,
        })
        { Timeout = Timeout.InfiniteTimeSpan });

        services.AddSingleton(sp => new PlexHttp(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<PlexClientIdentity>(), sp.GetRequiredService<ILogger<PlexHttp>>()));
        services.AddSingleton<PlexTvClient>();
        services.AddSingleton<ConnectionSelector>();
        // Plex's answers are kept on disk too, so the app opens from what it already knows.
        services.AddSingleton<ResponseCache>(_ => new ResponseCache(directory: Path.Combine(AppPaths.Root, "cache", "responses")));
        services.AddSingleton<ICredentialStore>(sp => new DpapiCredentialStore(AppPaths.Credentials, sp.GetRequiredService<ILogger<DpapiCredentialStore>>()));
        services.AddSingleton<PlexSession>();
        services.AddSingleton(sp => new ArtworkCache(AppPaths.ArtworkCache, sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ILogger<ArtworkCache>>()));

        services.AddSingleton(sp => new Core.Pins.PinStore(AppPaths.Pins, sp.GetRequiredService<ILogger<Core.Pins.PinStore>>()));
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AmbientService>();
        services.AddSingleton<DetailCache>();
        services.AddSingleton<Dislikes>();
        services.AddSingleton<PlexPass>();
        services.AddSingleton(sp => new MelodayService(
            sp.GetRequiredService<Core.Services.PlexSession>(), sp.GetRequiredService<PlexPass>(), sp.GetRequiredService<Dislikes>(),
            sp.GetRequiredService<Core.Settings.SettingsStore>(),
            new Core.Security.ProtectedSecret(AppPaths.OpenAiKey, "OpenAI", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            sp.GetRequiredService<ILogger<MelodayService>>()));
        services.AddSingleton<StationService>();
        services.AddSingleton<ArtistRanks>();
        services.AddSingleton<PlaylistCatalog>();
        services.AddSingleton<PlaylistArtistIndex>();
        services.AddSingleton(sp => new ArtistLogoService(
            new Core.Security.ProtectedSecret(AppPaths.FanartKey, "Fanart", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            sp.GetRequiredService<Core.Settings.SettingsStore>(), sp.GetRequiredService<ILogger<ArtistLogoService>>()));
        services.AddSingleton<LogoImport>();
        services.AddSingleton<TagCovers>();
        services.AddSingleton<StaticCovers>();
        // How long the listener has listened today (Home shows it), on this computer.
        services.AddSingleton(_ => new Core.Home.ListeningClock(System.IO.Path.Combine(AppPaths.Root, "listening.json")));
        // What was played lately (Home's Jump back in), on this computer.
        services.AddSingleton(sp => new Core.Home.RecentStore(System.IO.Path.Combine(AppPaths.Root, "recent.json"), sp.GetRequiredService<ILogger<Core.Home.RecentStore>>()));
        // What the listener skips, kept here only, so Meloday's own radio can learn (see LocalRadio).
        services.AddSingleton(_ => new Core.Radio.SkipStore(Path.Combine(AppPaths.Root, "radio", "skips.json")));
        services.AddSingleton<LocalRadio>();
        services.AddSingleton<MixService>();
        services.AddSingleton<HomeLayoutService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ArtworkService>();
        // Playback goes through a router: this PC's own engine, or a cast device's when casting.
        services.AddSingleton<MediaPlayerEngine>();
        services.AddSingleton<MixEngine>();
        services.AddSingleton<AudioAnalysisService>();
        services.AddSingleton<RoutingEngine>();
        services.AddSingleton<IPlaybackEngine>(sp => sp.GetRequiredService<RoutingEngine>());
        services.AddSingleton<CastService>();
        services.AddSingleton<PlaybackReporter>();
        services.AddSingleton<QueueStore>();
        services.AddSingleton<TransitionPlanner>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<PanelService>();
        services.AddSingleton<MiniPlayerService>();
        services.AddSingleton<SleepTimerService>();
        services.AddSingleton<CompanionService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<TrackNotifier>();
        services.AddSingleton<TagHighlights>();
        services.AddSingleton<TaskbarButtons>();
        services.AddSingleton(sp => new LastFmService(
            new Core.Security.ProtectedSecret(Path.Combine(AppPaths.Root, "lastfm.bin"), "LastFm", sp.GetRequiredService<ILogger<Core.Security.ProtectedSecret>>()),
            sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<PlayerService>(), sp.GetRequiredService<ILogger<LastFmService>>()));
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
        services.AddTransient<TagViewModel>();
        services.AddTransient<CollectionViewModel>();

        return services.BuildServiceProvider();
    }

    // Context for crash reports: which page was open when it happened.
    private static string CurrentPageName()
    {
        try { return _services?.GetService<NavigationService>()?.CurrentPage?.Name ?? "no page"; }
        catch { return "unknown"; }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _log?.LogError(e.Exception, "Unhandled UI exception on {Page} at {Width}x{Height}, interface size {Scale:P0}", CurrentPageName(),
            (int)(_window?.Bounds.Width ?? 0), (int)(_window?.Bounds.Height ?? 0), UiScale.Factor);
        // Keep the player alive for recoverable UI errors once the window exists; the error is logged.
        if (_window is null) return;
        e.Handled = true;
        try
        {
            Services.GetRequiredService<NotificationService>().ShowError("Something went wrong", "Meloday Music recovered from an unexpected error.");
        }
        catch
        {
            // Reporting must not throw.
        }
    }
}
