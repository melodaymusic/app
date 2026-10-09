using System.ComponentModel;
using HumeMusic.Core.Settings;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using HumeMusic.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace HumeMusic;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly SettingsStore _settings;
    private ShellView? _shellView;
    private SignInView? _signInView;
    private SetupView? _setupView;
    private LoadingView? _loadingView;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Meloday Music";
        _shell = App.Services.GetRequiredService<ShellViewModel>();
        _settings = App.Services.GetRequiredService<SettingsStore>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        // On the full-screen stage nothing of the window shows but the picture.
        var stage = App.Services.GetRequiredService<Services.StageService>();
        stage.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(() => TitleBar.Visibility = stage.IsOn ? Visibility.Collapsed : Visibility.Visible);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MelodayMusic.ico"));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 560;
        }
        RestorePlacement();

        _shell.PropertyChanged += OnShellChanged;
        ShowView(_shell.View);
        RootGrid.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        UpdateTitleBarColors();

        AppWindow.Closing += OnClosing;
        Activated += OnActivated;
        // Minimised, nothing is seen: ongoing motion rests (as when another window is in use).
        AppWindow.Changed += (_, e) =>
        {
            if (!e.DidPresenterChange && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
                Helpers.Motion.SetWindowActive(false);
        };
        PlayIntro();
        UiScale.Changed += OnUiScaleChanged;
        App.Services.GetRequiredService<AmbientService>().PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AmbientService.ForceDark)) ApplyRequestedTheme();
        };
    }

    private Controls.IntroView? _intro;

    /// <summary>
    /// The Meloday intro plays over the whole window as the app starts (the app loads beneath it),
    /// then fades away. Skipped with motion off (Meloday's setting or Windows' animation effects).
    /// </summary>
    private void PlayIntro()
    {
        if (!Helpers.Motion.IsEnabled) return;
        var intro = new Controls.IntroView
        {
            OpacityTransition = new Microsoft.UI.Xaml.ScalarTransition { Duration = TimeSpan.FromMilliseconds(450) },
        };
        Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(intro, 2);
        intro.Finished += OnIntroFinished;
        _intro = intro;
        RootGrid.Children.Add(intro);
        UpdateTitleBarColors();
    }

    private async void OnIntroFinished(object? sender, EventArgs e)
    {
        if (_intro is not { } intro) return;
        // A moment on the finished mark, then the app shows through.
        await Task.Delay(250);
        intro.IsHitTestVisible = false;
        intro.Opacity = 0;
        await Task.Delay(500);
        RootGrid.Children.Remove(intro);
        _intro = null;
        UpdateTitleBarColors();
    }

    private DateTime? _awaySince;

    /// <summary>
    /// Coming back to the app after a while: Plex may have changed meanwhile (playlists edited
    /// elsewhere, new plays), so cached responses are dropped and Home refreshes quietly.
    /// </summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Helpers.Motion.SetWindowActive(args.WindowActivationState != WindowActivationState.Deactivated);
        // The taskbar button exists once the window has been shown: its media buttons go on it.
        App.Services.GetRequiredService<TaskbarButtons>().Attach(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _awaySince ??= DateTime.UtcNow;
            return;
        }
        var away = _awaySince is { } since ? DateTime.UtcNow - since : TimeSpan.Zero;
        _awaySince = null;
        if (away < TimeSpan.FromMinutes(5) || _shell.View != RootView.Main) return;
        App.Services.GetRequiredService<Core.Services.PlexSession>().Client?.MarkStale();
        App.Services.GetRequiredService<Dislikes>().Invalidate();
        _ = App.Services.GetRequiredService<HomeViewModel>().RefreshAsync();
    }

    private AppTheme _theme = AppTheme.System;

    /// <summary>The listener's theme, except on pages that sit on a strong colour (always light text).</summary>
    public void ApplyTheme(AppTheme theme)
    {
        _theme = theme;
        ApplyRequestedTheme();
    }

    private void ApplyRequestedTheme()
    {
        RootGrid.RequestedTheme = App.Services.GetRequiredService<AmbientService>().ForceDark ? ElementTheme.Dark : _theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateTitleBarColors();
    }

    /// <summary>
    /// The interface size changed: the design tokens have been replaced, so the views are rebuilt
    /// from them (a true re-layout, not a bitmap zoom) and the user is returned to the same page.
    /// </summary>
    private void OnUiScaleChanged(object? sender, EventArgs e)
    {
        App.Services.GetRequiredService<NavigationService>().SaveForRestore();
        App.Services.GetRequiredService<AmbientService>().Clear();
        RootContent.Content = null;
        _shellView = null;
        _signInView = null;
        _setupView = null;
        _loadingView = null;
        ShowView(_shell.View);
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.View)) ShowView(_shell.View);
    }

    private void ShowView(RootView view)
    {
        // The main view extends under the title bar (artwork reaches the top of the window); the
        // sign-in, setup and loading screens sit below it.
        var main = view == RootView.Main;
        Microsoft.UI.Xaml.Controls.Grid.SetRow(RootContent, main ? 0 : 1);
        Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(RootContent, main ? 2 : 1);
        RootContent.Content = view switch
        {
            RootView.SignIn => _signInView ??= new SignInView(),
            RootView.Setup => _setupView ??= new SetupView(),
            RootView.Main => _shellView ??= new ShellView(),
            _ => _loadingView ??= new LoadingView(),
        };
    }

    public void UpdateTitleBarColors()
    {
        // The intro is black whatever the theme, so its caption buttons are light.
        var dark = RootGrid.ActualTheme == ElementTheme.Dark || _intro is not null;
        // Window-level markup doesn't re-resolve theme resources, so the title follows the theme here.
        TitleText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Helpers.ThemeColors.Get("HumeTextSecondaryBrush", RootGrid.ActualTheme, dark ? ColorHelper.FromArgb(255, 0xB3, 0xAF, 0xA8) : ColorHelper.FromArgb(255, 0x5E, 0x5A, 0x55)));
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x99, 0, 0, 0);
        tb.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x14, 0, 0, 0);
        tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
        tb.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x0C, 0, 0, 0);
    }

    private void RestorePlacement()
    {
        var s = _settings.Current;
        // Saved sizes are physical pixels; the first-run default is 1320x860 effective pixels.
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        var width = s.WindowWidth ?? (int)(1320 * scale);
        var height = s.WindowHeight ?? (int)(860 * scale);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);

        if (s.WindowX is { } x && s.WindowY is { } y && x >= area.X - 50 && y >= area.Y - 50 && x < area.X + area.Width - 100 && y < area.Y + area.Height - 100)
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        else
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));

        if (s.WindowMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void SavePlacement()
    {
        var s = _settings.Current;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            s.WindowMaximized = p.State == OverlappedPresenterState.Maximized;
            if (p.State == OverlappedPresenterState.Restored)
            {
                s.WindowX = AppWindow.Position.X;
                s.WindowY = AppWindow.Position.Y;
                s.WindowWidth = AppWindow.Size.Width;
                s.WindowHeight = AppWindow.Size.Height;
            }
        }
        _settings.Save();
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing) return;
        // Finish the Plex "stopped" report and persist the queue before the process exits.
        args.Cancel = true;
        _closing = true;
        try
        {
            App.Services.GetRequiredService<MiniPlayerService>().Close(restoreMain: false);
            App.Services.GetRequiredService<TrackNotifier>().Clear();
            SavePlacement();
            // The queue is saved and Plex told first, but never for long: an installer or Windows waiting
            // for the app to close shouldn't be kept waiting (phones following it may not answer).
            var shutdown = Task.WhenAll(
                App.Services.GetRequiredService<PlayerService>().ShutdownAsync(),
                App.Services.GetRequiredService<CompanionService>().StopAsync());
            await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromSeconds(3)));
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogger<MainWindow>>().LogWarning(ex, "Error during shutdown");
        }
        App.Services.GetRequiredService<ILogger<MainWindow>>().LogInformation("Meloday Music exiting");
        App.Services.GetService<Core.Logging.FileLoggerProvider>()?.Dispose();
        Close();
        Application.Current.Exit();
    }
}
